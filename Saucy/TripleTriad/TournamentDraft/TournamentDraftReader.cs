using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework;
using static ECommons.GenericHelpers;

namespace Saucy.TripleTriad.TournamentDraft;

public static unsafe class TournamentDraftReader
{
    public const string DraftAddonName = "TripleTriadPickUpDeckSelect";
    public const string ConfirmationAddonName = "TripleTriadDeckConfirmation";

    public static readonly uint[] Set1NodeIds = [11, 12, 13];
    public static readonly uint[] Set2NodeIds = [25, 26, 27];
    public static readonly uint[] Set3NodeIds = [39, 40, 41];

    public static bool TryGetDraftAddon(out AtkUnitBase* addon)
    {
        if (TryGetAddonByName<AtkUnitBase>(DraftAddonName, out addon) && IsAddonReady(addon) && addon->IsVisible)
        {
            return true;
        }

        addon = null;
        return false;
    }

    public static bool TryGetConfirmationAddon(out AtkUnitBase* addon)
    {
        if (TryGetAddonByName<AtkUnitBase>(ConfirmationAddonName, out addon) && IsAddonReady(addon) && addon->IsVisible)
        {
            return true;
        }

        addon = null;
        return false;
    }

    public static TournamentDraftState? ReadDraftState()
    {
        if (!TryGetDraftAddon(out var addon))
        {
            return null;
        }

        var state = new TournamentDraftState
        {
            Rules = ExtractRules(addon)
        };

        // Set 1: Nodes 11, 12, 13 (2 cards each)
        for (var i = 0; i < Set1NodeIds.Length; i++)
        {
            var opt = ReadOption(addon, 0, i, Set1NodeIds[i], isPair: true);
            if (opt != null) state.Set1Options.Add(opt);
        }

        // Set 2: Nodes 25, 26, 27 (2 cards each)
        for (var i = 0; i < Set2NodeIds.Length; i++)
        {
            var opt = ReadOption(addon, 1, i, Set2NodeIds[i], isPair: true);
            if (opt != null) state.Set2Options.Add(opt);
        }

        // Set 3: Nodes 39, 40, 41 (1 card each)
        for (var i = 0; i < Set3NodeIds.Length; i++)
        {
            var opt = ReadOption(addon, 2, i, Set3NodeIds[i], isPair: false);
            if (opt != null) state.Set3Options.Add(opt);
        }

        return state.IsValid ? state : null;
    }

    private static List<string> ExtractRules(AtkUnitBase* addon)
    {
        var rules = new List<string>();

        // Node 5 is the rules TextNode in TripleTriadPickUpDeckSelect
        var ruleNode = addon->GetTextNodeById(5);
        if (ruleNode != null && ruleNode->AtkResNode.IsVisible())
        {
            var text = ruleNode->NodeText.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var parts = text.Split(['/', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                rules.AddRange(parts.Distinct());
            }
        }

        // Fallback: check UIState modifiers if available
        if (rules.Count == 0 && TriadRun.profileGS != null)
        {
            // Keep at least draft rule
            rules.Add("Draft");
        }

        return rules;
    }

    private static TournamentDraftOption? ReadOption(
        AtkUnitBase* addon,
        int setIndex,
        int optionIndex,
        uint nodeId,
        bool isPair)
    {
        var compNode = (AtkComponentNode*)addon->GetNodeById(nodeId);
        if (compNode == null || compNode->Component == null)
        {
            return null;
        }

        var opt = new TournamentDraftOption
        {
            SetIndex = setIndex,
            OptionIndex = optionIndex,
            NodeId = nodeId
        };

        // Card 1 is at SubNode 2
        var card1 = ReadCardFromComponent(compNode->Component, 2);
        if (card1 != null)
        {
            opt.Cards.Add(card1);
        }

        if (isPair)
        {
            // Card 2 is at SubNode 3
            var card2 = ReadCardFromComponent(compNode->Component, 3);
            if (card2 != null)
            {
                opt.Cards.Add(card2);
            }
        }

        return opt.Cards.Count > 0 ? opt : null;
    }

    private static TournamentDraftCard? ReadCardFromComponent(AtkComponentBase* parentComp, uint cardSubNodeId)
    {
        var cardNode = (AtkComponentNode*)parentComp->UldManager.SearchNodeById(cardSubNodeId);
        if (cardNode == null || cardNode->Component == null)
        {
            return null;
        }

        var cardComp = cardNode->Component;

        // Subnodes in CardTripleTriad:
        // Node 14: Up stat
        // Node 15: Down stat
        // Node 16: Left stat
        // Node 17: Right stat
        var upNode = (AtkTextNode*)cardComp->UldManager.SearchNodeById(14);
        var downNode = (AtkTextNode*)cardComp->UldManager.SearchNodeById(15);
        var leftNode = (AtkTextNode*)cardComp->UldManager.SearchNodeById(16);
        var rightNode = (AtkTextNode*)cardComp->UldManager.SearchNodeById(17);

        var numU = ParseStat(upNode != null ? upNode->NodeText.ToString() : null);
        var numD = ParseStat(downNode != null ? downNode->NodeText.ToString() : null);
        var numL = ParseStat(leftNode != null ? leftNode->NodeText.ToString() : null);
        var numR = ParseStat(rightNode != null ? rightNode->NodeText.ToString() : null);

        if (numU == 0 && numD == 0 && numL == 0 && numR == 0)
        {
            return null;
        }

        // Try lookup in TriadCardDB
        var matched = TriadCardDB.Get().Find(numU, numL, numD, numR);
        if (matched != null)
        {
            return new TournamentDraftCard(
                matched.Name,
                numU,
                numL,
                numD,
                numR,
                (int)matched.Rarity,
                matched.Type.ToString());
        }

        return new TournamentDraftCard(
            $"Card [{numU}-{numL}-{numD}-{numR}]",
            numU,
            numL,
            numD,
            numR,
            1);
    }

    private static int ParseStat(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        s = s.Trim();
        if (string.Equals(s, "A", StringComparison.OrdinalIgnoreCase)) return 10;
        if (int.TryParse(s, out var v)) return v;
        return 0;
    }
}
