using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework;

namespace Saucy.TripleTriad.TournamentDraft;

public static unsafe class TournamentDraftManager
{
    private static TournamentDraftResult? _lastEvaluation;
    private static DateTime _lastEvalTime = DateTime.MinValue;

    public static bool IsDraftWindowOpen(out List<string> rules)
    {
        rules = [];
        if (!TournamentDraftReader.TryGetDraftAddon(out _))
        {
            _lastEvaluation = null;
            return false;
        }

        var state = TournamentDraftReader.ReadDraftState();
        if (state != null)
        {
            rules = state.Rules;
        }

        return true;
    }

    public static TournamentDraftResult? GetCurrentEvaluation()
    {
        if (!TournamentDraftReader.TryGetDraftAddon(out _))
        {
            _lastEvaluation = null;
            return null;
        }

        // Cache evaluation for 500ms to avoid re-evaluating every frame
        if (_lastEvaluation != null && (DateTime.UtcNow - _lastEvalTime).TotalMilliseconds < 500)
        {
            return _lastEvaluation;
        }

        var state = TournamentDraftReader.ReadDraftState();
        if (state == null)
        {
            return null;
        }

        _lastEvaluation = TournamentDraftEvaluator.Evaluate(state);
        _lastEvalTime = DateTime.UtcNow;
        return _lastEvaluation;
    }

    public static bool ApplyOptimalDraft(out string resultMessage)
    {
        resultMessage = string.Empty;

        if (!TournamentDraftReader.TryGetDraftAddon(out var addon))
        {
            resultMessage = "Open Tournament draft window is not open.";
            return false;
        }

        var state = TournamentDraftReader.ReadDraftState();
        if (state == null)
        {
            resultMessage = "Failed to parse cards from draft window.";
            return false;
        }

        var result = TournamentDraftEvaluator.Evaluate(state);

        // Click Set 1 Radio Button (Node 11, 12, or 13)
        var s1NodeId = TournamentDraftReader.Set1NodeIds[result.Set1Option];
        ClickRadioButton(addon, s1NodeId);

        // Click Set 2 Radio Button (Node 25, 26, or 27)
        var s2NodeId = TournamentDraftReader.Set2NodeIds[result.Set2Option];
        ClickRadioButton(addon, s2NodeId);

        // Click Set 3 Radio Button (Node 39, 40, or 41)
        var s3NodeId = TournamentDraftReader.Set3NodeIds[result.Set3Option];
        ClickRadioButton(addon, s3NodeId);

        addon->Update(0);

        var cardNames = string.Join(", ", result.SelectedCards.Select(c => c.Name));
        resultMessage = $"✓ Selected Set 1 [{result.Set1Option + 1}], Set 2 [{result.Set2Option + 1}], Set 3 [{result.Set3Option + 1}] ({cardNames})";

        Svc.Log.Information($"[TournamentDraftManager] {resultMessage} | {result.Summary}");
        return true;
    }

    private static void ClickRadioButton(AtkUnitBase* addon, uint nodeId)
    {
        try
        {
            var compNode = addon->GetComponentNodeById(nodeId);
            if (compNode != null && compNode->Component != null)
            {
                var rb = (AtkComponentRadioButton*)compNode->Component;
                AddonButton.TryClick(addon, &rb->AtkComponentButton);
            }
            else
            {
                // Fallback to component button
                AddonButton.TryClick(addon, nodeId);
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[TournamentDraftManager] Failed to click radio button NodeId {nodeId}");
        }
    }
}
