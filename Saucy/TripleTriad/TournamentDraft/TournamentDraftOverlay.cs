using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Saucy.Framework;

namespace Saucy.TripleTriad.TournamentDraft;

public unsafe class TournamentDraftOverlay : Window
{
    private string _statusMessage = string.Empty;

    public TournamentDraftOverlay() : base(
        "Saucy Open Tournament Drafter###SaucyDraftOverlay",
        ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        RespectCloseHotkey = false;
    }

    public override bool DrawConditions()
    {
        if (P is null || !C.UniversalTriadDraftOverlayEnabled)
        {
            return false;
        }

        return TournamentDraftReader.TryGetDraftAddon(out _);
    }

    public override void Draw()
    {
        var draftOpen = TournamentDraftManager.IsDraftWindowOpen(out var rules);
        if (!draftOpen)
        {
            _statusMessage = string.Empty;
            return;
        }

        var rulesStr = rules.Count > 0 ? string.Join(", ", rules) : Loc.T("Draft");
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), Loc.T("Tournament Rules: {0}", rulesStr));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 2));

        var eval = TournamentDraftManager.GetCurrentEvaluation();
        if (eval != null)
        {
            if (!string.IsNullOrEmpty(eval.Summary))
            {
                ImGui.TextWrapped(Loc.T("{0}", eval.Summary));
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
        }

        ImGui.Dummy(new Vector2(0, 4));

        if (ImGui.Button(Loc.T("✨ Select Optimal Deck"), new Vector2(300f * ImGuiHelpers.GlobalScale, 36f * ImGuiHelpers.GlobalScale)))
        {
            TournamentDraftManager.ApplyOptimalDraft(out _statusMessage);
        }

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            ImGui.Dummy(new Vector2(0, 2));
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.3f, 1.0f), _statusMessage);
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f), Loc.T("→ Review your cards and click 'Confirm' in the game window."));
        }
        else
        {
            ImGui.TextDisabled(Loc.T("※ Applies all 3 draft choices. Click Confirm in game to finalize."));
        }
    }
}
