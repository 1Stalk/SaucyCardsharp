using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Lumina.Excel.Sheets;
using Saucy.TripleTriad.TournamentDraft;
using Saucy.TripleTriad.UniversalEngine;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
namespace Saucy.TripleTriad;

internal static class TriadSettingsUi
{
    private static int DraftMatchCount = 1;
    private static string _lastDraftStatus = string.Empty;

    internal enum TriadAutoplayMode
    {
        None,
        Npc,
        OpenTournament,
        PvP,
    }

    private static void SetAutoplayMode(TriadAutoplayMode targetMode, GameNpcInfo? runTargetNpc = null)
    {
        switch (targetMode)
        {
            case TriadAutoplayMode.Npc:
                C.UniversalTriadTournamentEnabled = false;
                C.UniversalTriadAutoplayEnabled = false;
                UniversalTriadManager.Reset();

                if (!TriadNpcProximity.IsRelevantTriadNpcNearby())
                {
                    TriadRunSession.ModuleEnabled = false;
                    var npcName = TriadNpcProximity.ResolveTriadNpcForProximityCheck()?.Name;
                    DuoLog.Warning(string.IsNullOrEmpty(npcName)
                        ? Loc.T("No Triple Triad NPC nearby. Move closer to the NPC you want to play.")
                        : Loc.T("No Triple Triad NPC nearby ({0}). Move closer to the NPC you want to play.", npcName));
                }
                else
                {
                    TriadRunSession.ModuleEnabled = true;
                    CommitDraftMatchCount();
                    GoldSaucerArcadeMachineHelper.DisableConflictingModules();
                    TriadRunSession.BeginAutomationSession();
                    TriadCardFarmSession.SyncDisplay(runTargetNpc);
                    TriadAutomator.RunModule();
                }

                C.Save();
                break;

            case TriadAutoplayMode.OpenTournament:
                TriadRunSession.StopAllAutomation(announce: false);
                C.UniversalTriadAutoplayEnabled = false;
                C.UniversalTriadTournamentEnabled = true;
                UniversalTriadManager.Reset();
                C.Save();
                break;

            case TriadAutoplayMode.PvP:
                TriadRunSession.StopAllAutomation(announce: false);
                C.UniversalTriadTournamentEnabled = false;
                C.UniversalTriadAutoplayEnabled = true;
                UniversalTriadManager.Reset();
                C.Save();
                break;

            case TriadAutoplayMode.None:
            default:
                if (TriadRunSession.ModuleEnabled)
                {
                    TriadRunSession.StopAllAutomation(announce: false);
                }
                C.UniversalTriadTournamentEnabled = false;
                C.UniversalTriadAutoplayEnabled = false;
                UniversalTriadManager.Reset();
                C.Save();
                break;
        }
    }

    public static void Draw()
    {
        DraftMatchCount = Math.Max(1, C.TriadMatchCount);
        var runTargetNpc = TriadRunTarget.Resolve();

        DrawAutoplaySection(runTargetNpc);

        ImGui.Dummy(new(0, 4));

        DrawEngineSettings();

        ImGui.Dummy(new(0, 4));

        DrawGeneralSettings();

        ImGui.Dummy(new(0, 4));

        SaucyTheme.DrawCard(Loc.T("Open Tournament (Draft)"), null, DrawOpenTournamentBody);
        SaucyTheme.DrawCard(Loc.T("Deck"), null, DrawDeckBody);
        SaucyTheme.DrawCard(Loc.T("Run mode"), null, DrawRunModeBody);
        SaucyTheme.DrawCard(Loc.T("Travel"), Loc.T("Map navigation"), DrawTravelMount);
        SaucyTheme.DrawCard(Loc.T("Notifications"), null, DrawNotificationsBody);
        SaucyTheme.DrawCard(Loc.T("Dependencies"), Loc.T("Optional integrations"), DrawDependencies);
    }

    private static void DrawAutoplaySection(GameNpcInfo? runTargetNpc)
    {
        ImGui.TextUnformatted(Loc.T("Autoplay Modes:"));

        var isNpc = TriadRunSession.ModuleEnabled;
        if (ImGui.Checkbox(Loc.T("Enable NPC Autoplay (Card Farming)"), ref isNpc))
        {
            SetAutoplayMode(isNpc ? TriadAutoplayMode.Npc : TriadAutoplayMode.None, runTargetNpc);
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Accepts match invites, selects decks, plays cards, and handles rematches. Turn on before or during match prep."));

        var isTournament = C.UniversalTriadTournamentEnabled;
        if (ImGui.Checkbox(Loc.T("Enable Open Tournament Autoplay"), ref isTournament))
        {
            SetAutoplayMode(isTournament ? TriadAutoplayMode.OpenTournament : TriadAutoplayMode.None, runTargetNpc);
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Autoplays matches in Gold Saucer Open Tournaments using XIVcardsharp solver against tournament draft decks."));

        var isPvp = C.UniversalTriadAutoplayEnabled;
        if (ImGui.Checkbox(Loc.T("Enable PvP / Friendly Autoplay"), ref isPvp))
        {
            SetAutoplayMode(isPvp ? TriadAutoplayMode.PvP : TriadAutoplayMode.None, runTargetNpc);
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Autoplays custom or friendly PvP matches in Battlehall or open-world testing using XIVcardsharp solver."));
    }

    private static void DrawEngineSettings()
    {
        ImGui.TextUnformatted(Loc.T("XIVcardsharp Engine Settings:"));

        var delay = Math.Clamp(C.UniversalTriadMoveDelayMs, 100, 3000);
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt(Loc.T("Move delay"), ref delay, 100, 3000, "%d ms"))
        {
            C.UniversalTriadMoveDelayMs = delay;
            C.Save();
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(Loc.T("Delay before placing a card once your turn begins. Applies across NPC, Tournament, and PvP matches."));

        var samples = Math.Clamp(C.UniversalTriadSampleCount, 50, 500);
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt(Loc.T("MCTS Samples"), ref samples, 50, 500, "%d"))
        {
            C.UniversalTriadSampleCount = samples;
            C.Save();
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(Loc.T("Opponent deck samples evaluated per imperfect-information turn (50–500). Higher = stronger play, lower = faster. Recommended: 300."));

        var maxCores = Environment.ProcessorCount;
        var threads = Configuration.ClampUniversalTriadMaxThreads(C.UniversalTriadMaxThreads);
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt(Loc.T("Solver threads (0 = all)###UniversalTriadThreads"), ref threads, 0, maxCores, threads == 0 ? Loc.TPrintf("All") : "%d"))
        {
            C.UniversalTriadMaxThreads = Configuration.ClampUniversalTriadMaxThreads(threads);
            C.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                Loc.T("Uses {0} of {1} logical cores for parallel solving.", SaucyParallelism.UniversalTriadThreads, maxCores));
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Parallel threads during Monte Carlo solving across NPC, Tournament, and PvP matches (0 = all cores). Separate from Deck Optimizer threads.") +
            (SaucyParallelism.IsWineHost
                ? "\n\n" + Loc.T("Linux / Wine (XLCore, Steam Deck): solving threads are capped to half your logical cores no matter what you pick here to prevent wine crashes.")
                : ""));

        var record = C.UniversalTriadRecordMatches;
        if (ImGui.Checkbox(Loc.T("Record Match Logs"), ref record))
        {
            C.UniversalTriadRecordMatches = record;
            C.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Open Logs Folder")))
        {
            UniversalMatchRecorder.OpenLogsFolder();
        }

        DrawEngineStatusReadout();
    }

    private static void DrawGeneralSettings()
    {
        var autoOpen = C.OpenAutomatically;
        if (ImGui.Checkbox(Loc.T("Open window when challenging an NPC"), ref autoOpen))
        {
            C.OpenAutomatically = autoOpen;
            C.Save();
        }

        var collectionUi = C.CollectionUiEnabled;
        if (ImGui.Checkbox(Loc.T("Gold Saucer card search panels"), ref collectionUi))
        {
            C.CollectionUiEnabled = collectionUi;
            C.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Shows a searchable card list beside the Gold Saucer card UI, including Edit Deck (TriadBuddy-style [No.1] ordering). Also shows NPC search on the main card collection screen."));

        var autoRegister = C.AutoRegisterTriadCards;
        if (ImGui.Checkbox(Loc.T("Register won cards automatically"), ref autoRegister))
        {
            C.AutoRegisterTriadCards = autoRegister;
            C.Save();
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Uses Triple Triad card items in your inventory that aren't in your collection yet, once triad automation is idle."));
    }

    private static void DrawDeckOptimizerSettings()
    {
        using var indent = ImRaii.PushIndent();
        var showOptimizerChatSpam = C.ShowOptimizerChatSpam;
        if (ImGui.Checkbox(Loc.T("Show deck automation messages in chat"), ref showOptimizerChatSpam))
        {
            C.ShowOptimizerChatSpam = showOptimizerChatSpam;
            C.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Shows [Saucy] deck optimizer, deck selection, and profile-write messages in chat. Does not hide the game's own lines (e.g. \"in play for the next match\")."));

        DrawDeckOptimizerMaxThreadsSlider();
        DrawDeckOptimizerTimeoutSlider();

        var skipBeatenOrCompleted = C.SkipOptimizedDeckForBeatenOrCompletedNpcs;
        if (ImGui.Checkbox(Loc.T("Skip beaten or completed NPCs"), ref skipBeatenOrCompleted))
        {
            C.SkipOptimizedDeckForBeatenOrCompletedNpcs = skipBeatenOrCompleted;
            C.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Don't start a background deck build when you target an NPC you've already beaten or whose cards you already own. Still builds at match prep if you challenge them."));

        var pauseForQuestionable = C.PauseOptimizedDeckBuildWhileQuestionable;
        if (ImGui.Checkbox(Loc.T("Pause while Questionable is running"), ref pauseForQuestionable))
        {
            C.PauseOptimizedDeckBuildWhileQuestionable = pauseForQuestionable;
            C.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Pauses background deck builds while Questionable (/qst) is questing. Match prep still builds if you challenge an NPC."));

        DrawDeckOptimizerStatus();
    }

    private static void DrawDeckOptimizerMaxThreadsSlider()
    {
        var threads = Configuration.ClampDeckOptimizerMaxThreads(C.DeckOptimizerMaxThreads);
        var maxCores = Environment.ProcessorCount;
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt(Loc.T("Deck optimizer threads (0 = all)###DeckOptimizerThreads"), ref threads, 0, maxCores, threads == 0 ? Loc.TPrintf("All") : "%d"))
        {
            C.DeckOptimizerMaxThreads = Configuration.ClampDeckOptimizerMaxThreads(threads);
            C.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                Loc.T("Uses {0} of {1} logical cores for parallel deck tests.", SaucyParallelism.DeckOptimizerThreads, maxCores));
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Parallel threads while building an optimized deck (0 = all cores). Separate from match solver threads.") +
            (SaucyParallelism.IsWineHost
                ? "\n\n" + Loc.T("Linux / Wine (XLCore, Steam Deck): deck builds are capped to half your logical cores no matter what you pick here. Using every core for parallel deck builds can hard-crash the game under Wine.")
                : ""));
    }

    private static void DrawDeckOptimizerTimeoutSlider()
    {
        var timeout = Math.Clamp(C.DeckOptimizerTimeoutMinutes, 1, 15);
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt(Loc.T("Optimizer timeout (min)"), ref timeout, 1, 15, "%d " + Loc.TPrintf("min")))
        {
            C.DeckOptimizerTimeoutMinutes = Math.Clamp(timeout, 1, 15);
            C.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Cancels a background deck build after this long. If the build is not finished by deck select, Saucy falls back to your best profile deck. Map navigation waits until a deck is ready."));
    }

    private static void DrawDeckBody()
    {
        if (TriadRun.profileGS?.GetPlayerDecks() is not { } profileDecks || profileDecks.Count() == 0)
        {
            ImGui.TextWrapped(Loc.T("Challenge an NPC once to load your profile decks here."));
            return;
        }

        var useAutoDeck = C.UseSimmedDeck;
        if (ImGui.Checkbox(Loc.T("Auto-pick best deck"), ref useAutoDeck))
        {
            C.UseSimmedDeck = useAutoDeck;
            C.Save();
            if (!useAutoDeck)
            {
                TriadRun.ResetDeckOptimizerState();
            }
        }

        var targetedNpc = TriadTargetNpc.FromWorldTarget();
        var autoPickNpc = targetedNpc ?? TriadRun.preGameNpc;
        if (C.UseSimmedDeck && autoPickNpc != null)
        {
            var autoPickSummary = TriadRun.GetAutoPickDeckSummary(autoPickNpc);
            if (!string.IsNullOrEmpty(autoPickSummary))
            {
                ImGui.SameLine();
                ImGui.TextDisabled(autoPickSummary);
            }
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Picks a deck at deck select. Default: highest opening win % among your profile decks. If none of those decks have 5 cards, Saucy uses the game's Recommended button."));

        if (C.UseSimmedDeck)
        {
            using var indent = ImRaii.PushIndent();

            if (autoPickNpc != null)
            {
                TriadRun.RefreshPrepRulesFromLive();
                TriadRun.EnsurePreviewEvalForNpc(autoPickNpc);
                if (TriadRun.ShouldBuildOptimizedDeck())
                {
                    TriadRun.EnsureOptimizedDeckPreviewEval(autoPickNpc);
                }
            }

            if (C.AlwaysBuildOptimizedDeck && C.UseCachedOptimizedDeckIfAvailable)
            {
                C.UseCachedOptimizedDeckIfAvailable = false;
                C.Save();
            }

            var useCachedDeck = C.UseCachedOptimizedDeckIfAvailable;
            if (ImGui.Checkbox(Loc.T("Use cached deck if available"), ref useCachedDeck))
            {
                if (useCachedDeck)
                {
                    C.UseCachedOptimizedDeckIfAvailable = true;
                    C.AlwaysBuildOptimizedDeck = false;
                    if (!TriadCardFarmSession.IsModeActive())
                    {
                        TriadRun.CancelDeckOptimizerJob(userCancelled: true);
                    }
                }
                else
                {
                    C.UseCachedOptimizedDeckIfAvailable = false;
                    TriadRun.ResetDeckOptimizerState();
                }

                C.Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                Loc.T("At match prep, loads a matching cached deck into profile slot 5 when one exists for this NPC and rules. Auto-pick still sims your profile decks and picks the highest opening win %. Cannot be combined with Build optimized deck."));

            var alwaysBuild = C.AlwaysBuildOptimizedDeck;
            if (ImGui.Checkbox(Loc.T("Build optimized deck"), ref alwaysBuild))
            {
                if (alwaysBuild)
                {
                    C.AlwaysBuildOptimizedDeck = true;
                    C.UseCachedOptimizedDeckIfAvailable = false;
                    TriadRun.ResetDeckOptimizerState();
                }
                else
                {
                    C.AlwaysBuildOptimizedDeck = false;
                    if (!TriadCardFarmSession.IsModeActive())
                    {
                        TriadRun.CancelDeckOptimizerJob(userCancelled: true);
                    }
                }

                C.Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                Loc.T("At match prep, builds a deck from your owned cards when no valid cache or existing \"NPC (Saucy)\" profile deck fits this NPC and rules. Rebuilds if you have gained {0} or more new cards since the last build for that NPC. Saves to profile slot 5 and selects it. Cannot be combined with Use cached deck.",
                    TriadOptimizedDeckCacheStore.RebuildAfterNewCardCount));

            if (C.AlwaysBuildOptimizedDeck)
            {
                DrawDeckOptimizerSettings();
            }

            return;
        }

        if (targetedNpc != null)
        {
            TriadRun.RefreshPrepRulesFromLive();
            TriadRun.EnsurePreviewEvalForNpc(targetedNpc);

            if (TriadRun.IsPreviewEvalPendingForNpc(targetedNpc))
            {
                ImGui.TextDisabled(Loc.T("Target NPC: {0} (calculating win %…)", targetedNpc.Name));
            }
            else
            {
                ImGui.TextDisabled(Loc.T("Target NPC: {0}", targetedNpc.Name));
            }

            ImGui.Spacing();
        }

        var selectedDeck = C.SelectedDeckIndex;
        var decks = TriadRun.profileGS.GetPlayerDecks()!;
        var previewName = Loc.T("(none)");
        if (selectedDeck == Configuration.GameRecommendedDeckIndex)
        {
            previewName = Loc.T("Game recommended");
        }
        else if (selectedDeck >= 0 && selectedDeck < decks.Count() && decks[selectedDeck] != null)
        {
            var rawName = decks[selectedDeck]!.name ?? string.Empty;
            var previewData = targetedNpc != null ? TriadRun.GetDeckPreviewData(targetedNpc, selectedDeck) : null;
            previewName = TriadDeckEvalDisplay.FormatDeckNameWithWinChance(rawName, previewData);
            if (string.IsNullOrEmpty(previewName))
            {
                previewName = Loc.T("(none)");
            }
        }

        ImGui.TextUnformatted(Loc.T("Select deck"));
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Game recommended uses FFXIV's built-in deck suggestion for the current match (not Saucy sims)."));
        ImGui.SetNextItemWidth(300f * ImGuiHelpers.GlobalScale);
        using var deckCombo = ImRaii.Combo("##SaucyDeckSelect", previewName);
        if (deckCombo)
        {
            if (ImGui.Selectable(Loc.T("(none)") + "###SaucyClearDeckSelection", selectedDeck == -1))
            {
                C.SelectedDeckIndex = -1;
                C.Save();
            }

            if (ImGui.Selectable(Loc.T("Game recommended") + "###SaucyGameRecommendedDeck",
                selectedDeck == Configuration.GameRecommendedDeckIndex))
            {
                C.SelectedDeckIndex = Configuration.GameRecommendedDeckIndex;
                C.Save();
            }

            foreach (var deck in decks)
            {
                if (deck is null)
                {
                    continue;
                }

                if (ImGui.Selectable(FormatDeckLabel(deck.id, deck.name, targetedNpc), deck.id == selectedDeck))
                {
                    C.SelectedDeckIndex = deck.id;
                    C.Save();
                }
            }
        }
    }

    private static void DrawRunModeBody()
    {
        ImGui.TextWrapped(
            Loc.T("Choose when Saucy stops playing. On plugin load no option is selected and Saucy rematches until automation is disabled."));
        ImGui.Dummy(new(0, 4));

        if (ImGui.RadioButton(Loc.T("Fixed match count"), TriadRunSession.PlayXTimes))
        {
            CommitDraftMatchCount();
            TriadRunSession.ApplyRunMode(TriadRunMode.PlayXTimes, matchCount: DraftMatchCount);
        }

        if (TriadRunSession.PlayXTimes)
        {
            using var subIndent = ImRaii.PushIndent();
            ImGui.Text(Loc.T("How many times:"));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(GoldSaucerRunSettingsUi.CompactCountInputWidth * ImGuiHelpers.GlobalScale);
            var count = Math.Max(1, C.TriadMatchCount);
            if (ImGui.InputInt("###TriadMatchCount", ref count) ||
                ImGui.IsItemDeactivatedAfterEdit())
            {
                ApplyMatchCount(count);
            }

            DraftMatchCount = Math.Max(1, count);

            var remaining = TriadRunSession.ModuleEnabled
                ? Math.Max(0, TriadRunSession.NumberOfTimes)
                : Math.Max(1, C.TriadMatchCount);
            ImGui.TextDisabled(Loc.T("Matches left this session: {0}", remaining));
        }

        if (ImGui.RadioButton(Loc.T("Stop after first card drop"), TriadRunSession.PlayUntilCardDrops))
        {
            TriadRunSession.ApplyRunMode(TriadRunMode.PlayUntilAnyCard);
        }

        if (ImGui.RadioButton(Loc.T("Farm all NPC cards once"), TriadRunSession.PlayUntilAllCardsDropOnce))
        {
            TriadRunSession.ApplyRunMode(TriadRunMode.PlayUntilAllCards, TriadRunTarget.Resolve());
        }

        if (TriadRunSession.NoRunModeSelected)
        {
            ImGui.TextDisabled(Loc.T("No stop condition — runs until automation is disabled."));
            ImGui.TextDisabled(Loc.T("Stops rematching while Duty Finder is ready."));
        }

        if (TriadRunSession.PlayUntilAllCardsDropOnce)
        {
            using var subIndent = ImRaii.PushIndent();

            TriadRunTarget.RefreshFromPrep();
            var runTargetNpc = TriadRunTarget.Resolve();
            var onMatchRegistration = uiReaderPrep.HasMatchRequestUI || TriadUiState.IsMatchRegistrationVisible();

            if (runTargetNpc != null)
            {
                ImGui.TextDisabled(Loc.T("NPC: {0}", TriadNpcDB.Get().FindByID(runTargetNpc.npcId).Name));
                if (onMatchRegistration)
                {
                    ImGui.TextDisabled(Loc.T("(match registration open)"));
                }
            }
            else if (onMatchRegistration)
            {
                ImGui.TextDisabled(Loc.T("NPC: reading match registration…"));
            }
            else
            {
                ImGui.TextDisabled(Loc.T("NPC: open match registration to list missing cards."));
            }

            var onlyUnobtained = C.OnlyUnobtainedCards;
            if (ImGui.Checkbox(Loc.T("Missing cards only"), ref onlyUnobtained))
            {
                C.OnlyUnobtainedCards = onlyUnobtained;
                C.Save();
                if (runTargetNpc != null)
                {
                    TriadCardFarmSession.StartTargets(runTargetNpc);
                }
            }

            if (runTargetNpc != null)
            {
                TriadCardFarmSession.SyncDisplay(runTargetNpc);
            }

            foreach (var entry in TriadCardFarmSession.TempCardsWonList)
            {
                var cardInfo = GameCardDB.Get().FindById((int)entry.Key);
                var cardName = cardInfo != null
                    ? TriadCardDB.Get().FindById(cardInfo.CardId)?.Name ?? Loc.T("Card #{0}", entry.Key)
                    : Loc.T("Card #{0}", entry.Key);
                ImGui.Text($"\u2022 {cardName} \u2014 {entry.Value}/1");
            }

            if (onlyUnobtained && runTargetNpc != null &&
                !TriadCardFarmSession.HasUnobtainedNpcRewards(runTargetNpc))
            {
                SaucyTheme.TextErrorWrapped(Loc.T("You already have every card from this NPC. Uncheck \"Missing cards only\" or choose a different NPC."));
            }
            else if (onlyUnobtained && TriadCardFarmSession.TempCardsWonList.Count == 0)
            {
                SaucyTheme.TextErrorWrapped(Loc.T("Start a match with an NPC to see which cards are still missing."));
            }
        }

        ImGui.Dummy(new(0, 4));
        var logOutAfterRun = C.LogOutAfterTriadRun;
        if (ImGui.Checkbox(Loc.T("Log out when run completes"), ref logOutAfterRun))
        {
            C.LogOutAfterTriadRun = logOutAfterRun;
            C.Save();
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Logs out of the game when a run finishes: fixed match count reaches zero, card drop mode triggers, or card farm completes."));
    }

    private static void CommitDraftMatchCount()
    {
        if (!TriadRunSession.PlayXTimes)
        {
            return;
        }

        ApplyMatchCount(DraftMatchCount);
    }

    private static void ApplyMatchCount(int count) => TriadRunSession.SyncPlayXTimesSession(Math.Max(1, count), true);

    private static void DrawNotificationsBody()
    {
        var playSound = C.PlaySound;
        if (ImGui.Checkbox(Loc.T("Play sound when run completes"), ref playSound))
        {
            C.PlaySound = playSound;
            C.Save();
        }

        if (playSound)
        {
            using var _ = ImRaii.PushIndent();
            DrawSoundPicker();
        }
    }

    private static void DrawSoundPicker()
    {
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        using var soundCombo = ImRaii.Combo("###SelectSound", C.SelectedSound);
        if (soundCombo)
        {
            var path = Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory!.FullName, "Sounds");
            Directory.CreateDirectory(path);
            foreach (var file in new DirectoryInfo(path).GetFiles())
            {
                var name = Path.GetFileNameWithoutExtension(file.FullName);
                if (ImGui.Selectable(name, C.SelectedSound == name))
                {
                    C.SelectedSound = name;
                    C.Save();
                }
            }
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.FolderOpen))
        {
            Process.Start("explorer.exe", Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory!.FullName, "Sounds"));
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Open sound folder — drop MP3s here to add your own."));
        }
    }

    private static string FormatDeckLabel(int deckId, string deckName, TriadNpc? targetNpc)
    {
        if (string.IsNullOrWhiteSpace(deckName))
        {
            deckName = Loc.T("Deck {0}", deckId + 1);
        }

        if (targetNpc == null)
        {
            return deckName;
        }

        return TriadDeckEvalDisplay.FormatDeckNameWithWinChance(deckName, TriadRun.GetDeckPreviewData(targetNpc, deckId));
    }

    private static void DrawDependencies() =>
        PluginDependenciesUi.Draw(
            Loc.T("Optional plugins for pathing to NPCs on the map, teleporting when needed, and starting unlock quests from Saucy."),
            [
                PluginDependenciesUi.Vnavmesh(
                    Loc.T("Walk to Triple Triad NPCs from Saucy map links after you arrive in the zone.")),
                PluginDependenciesUi.LifestreamPlugin(
                    Loc.T("Teleport to the nearest aetheryte before pathing when the NPC is far away or in another zone.")),
                PluginDependenciesUi.QuestionablePlugin(
                    Loc.T("Start Triple Triad unlock quests from Saucy card and NPC search."))
            ]);

    private static void DrawTravelMount()
    {
        ImGui.TextWrapped(Loc.T("Mount used before vnavmesh pathing to Triple Triad NPCs."));
        ImGui.Dummy(new(0, 4));

        var selectedMountId = C.TriadCollection.TravelMountId;
        ImGui.SetNextItemWidth(280f * ImGuiHelpers.GlobalScale);
        using var mountCombo = ImRaii.Combo("##TriadTravelMount", GetTravelMountPreviewLabel(selectedMountId));
        if (mountCombo)
        {
            if (ImGui.Selectable(Loc.T("Mount roulette"), selectedMountId == 0))
            {
                C.TriadCollection.TravelMountId = 0;
                C.Save();
            }

            foreach (var mount in GetOwnedTravelMounts())
            {
                if (ImGui.Selectable(mount.Name, selectedMountId == mount.Id))
                {
                    C.TriadCollection.TravelMountId = mount.Id;
                    C.Save();
                }
            }
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            Loc.T("Default uses the game's Mount Roulette general action. Pick a mount to always summon that one before map navigation."));
    }

    private static string GetTravelMountPreviewLabel(uint mountId)
    {
        if (mountId == 0)
        {
            return Loc.T("Mount roulette");
        }

        var mountSheet = Svc.Data.GetExcelSheet<Mount>();
        var row = mountSheet?.GetRowOrDefault(mountId);
        if (row == null)
        {
            return Loc.T("Mount #{0} (unavailable)", mountId);
        }

        var name = row.Value.Singular.ExtractText();
        if (!TravelMountHelper.IsMountUnlocked(mountId))
        {
            return Loc.T("{0} (unavailable)", name);
        }

        return name;
    }

    private static (uint Id, string Name)[] GetOwnedTravelMounts()
    {
        var mountSheet = Svc.Data.GetExcelSheet<Mount>();

        return
        [
            .. mountSheet
                .Where(mount => mount.RowId != 0 && TravelMountHelper.IsMountUnlocked(mount.RowId))
                .Select(mount => (Id: mount.RowId, Name: mount.Singular.ExtractText()))
                .Where(mount => !string.IsNullOrWhiteSpace(mount.Name))
                .OrderBy(mount => mount.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static void DrawDeckOptimizerStatus()
    {
        if (!TriadRun.ShouldBuildOptimizedDeck() ||
            !TriadDeckOptimizerJobs.TryGetActive(out var job))
        {
            return;
        }

        ImGui.Spacing();
        SaucyTheme.TextWarning(Loc.T("Building deck for {0}…", job.NpcName));

        var openingLabel = job.FormatBestWinChance();
        // The pending sentinel is not a win chance. Without this it renders as
        // "Opening win chance: … (updating…)" and the calculating line below is unreachable.
        var hasOpeningLabel = !string.IsNullOrEmpty(openingLabel) &&
                              openingLabel != TriadDeckOptimizerJobSnapshot.PendingWinChance;
        if (hasOpeningLabel)
        {
            ImGui.Text(Loc.T("Opening win chance: {0}", openingLabel));
            if (job.OpeningEvalInFlight)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(Loc.T("(updating…)"));
            }
        }
        else if (job.OpeningEvalInFlight)
        {
            ImGui.TextDisabled(Loc.T("Opening win chance: calculating…"));
        }

        var progress = Math.Clamp(job.ProgressPercent, 0, 100) / 100f;
        ImGui.ProgressBar(progress, new Vector2(-1, 0));

        ImGui.TextDisabled(Loc.T("Cards owned: {0}", job.NumOwnedCards.ToString("N0")));
        ImGui.TextDisabled(Loc.T("Possible decks: {0}", job.NumPossibleDecksDesc));
        ImGui.TextDisabled(Loc.T("Tested: {0}", job.NumTestedDecksDesc));
        ImGui.TextDisabled(Loc.T("Progress: {0}%", job.ProgressPercent));
        ImGui.TextDisabled(Loc.T("Time left: {0}", job.FormatTimeLeftDesc()));

        if (ImGui.Button(Loc.T("Cancel build"), new(-1, 0)))
        {
            TriadRun.CancelDeckOptimizerJob(userCancelled: true);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Stops the current background deck build."));
        }
    }

    private static void DrawOpenTournamentBody()
    {
        var draftOpen = TournamentDraftManager.IsDraftWindowOpen(out var rules);
        if (draftOpen)
        {
            var rulesStr = rules.Count > 0 ? string.Join(", ", rules) : Loc.T("Standard");
            ImGui.TextColored(new Vector4(0.4f, 0.85f, 1.0f, 1.0f), Loc.T("● Draft Active — Tournament Rules: {0}", rulesStr));
            ImGui.Dummy(new Vector2(0, 2));

            var eval = TournamentDraftManager.GetCurrentEvaluation();
            if (eval != null)
            {
                if (!string.IsNullOrEmpty(eval.Summary))
                {
                    ImGui.TextDisabled(eval.Summary);
                    ImGui.Dummy(new Vector2(0, 2));
                }

                var greenColor = new Vector4(0.2f, 0.95f, 0.35f, 1.0f);
                var s1Cards = eval.Set1Cards.Count > 0
                    ? string.Join(", ", eval.Set1Cards.Select(c => c.Name))
                    : string.Join(", ", eval.SelectedCards.Take(2).Select(c => c.Name));
                var s2Cards = eval.Set2Cards.Count > 0
                    ? string.Join(", ", eval.Set2Cards.Select(c => c.Name))
                    : string.Join(", ", eval.SelectedCards.Skip(2).Take(2).Select(c => c.Name));
                var s3Cards = eval.Set3Cards.Count > 0
                    ? string.Join(", ", eval.Set3Cards.Select(c => c.Name))
                    : string.Join(", ", eval.SelectedCards.Skip(4).Take(1).Select(c => c.Name));

                ImGui.TextColored(greenColor, Loc.T("Row 1: Choose Option {0}  →  {1}", eval.Set1Option + 1, s1Cards));
                ImGui.TextColored(greenColor, Loc.T("Row 2: Choose Option {0}  →  {1}", eval.Set2Option + 1, s2Cards));
                ImGui.TextColored(greenColor, Loc.T("Row 3: Choose Option {0}  →  {1}", eval.Set3Option + 1, s3Cards));

                ImGui.Dummy(new Vector2(0, 4));

                if (ImGui.Button(Loc.T("✨ Auto-Select in Game"), new Vector2(240f * ImGuiHelpers.GlobalScale, 28f * ImGuiHelpers.GlobalScale)))
                {
                    TournamentDraftManager.ApplyOptimalDraft(out _lastDraftStatus);
                }

                if (!string.IsNullOrEmpty(_lastDraftStatus))
                {
                    ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.3f, 1.0f), Loc.T("✓ Radio buttons selected in game."));
                    ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f), Loc.T("→ Now click 'Confirm' in the game window."));
                }
                else
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f), Loc.T("→ Select options above or click Auto-Select, then click 'Confirm' in game."));
                }
            }
            else
            {
                ImGui.TextDisabled(Loc.T("Reading draft cards from game window…"));
            }
        }
        else
        {
            _lastDraftStatus = string.Empty;
            ImGui.TextDisabled(Loc.T("Draft window is not open."));
            ImGui.TextDisabled(Loc.T("Enter an 8-player Open Tournament in Battlehall and open the draft window."));
            ImGui.TextDisabled(Loc.T("Optimal choices will be evaluated and displayed here automatically."));
        }
    }

    private static void DrawEngineStatusReadout()
    {
        var status = UniversalTriadManager.StatusSummary;
        var engineState = UniversalTriadManager.Status;
        if (engineState == UniversalEngineStatus.Evaluating)
        {
            SaucyTheme.TextWarning(Loc.T("Solver status: {0}", status));
        }
        else if (engineState is UniversalEngineStatus.MoveReady or UniversalEngineStatus.Executing)
        {
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.3f, 1.0f), Loc.T("Solver status: {0}", status));
        }
        else
        {
            ImGui.TextDisabled(Loc.T("Solver status: {0}", status));
        }
    }
}
