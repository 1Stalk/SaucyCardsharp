using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.TripleTriad.GameLogic;
using System;
namespace Saucy.TripleTriad.UI;

internal static class TriadTurnState
{
    public const int PlayerTurnAtkValueIndex = 23;
    private const int MaxBannerWalkDepth = 24;

    private static readonly string[] TurnBannerMarkers =
    [
        " TURN",
        " TURN'",
        " TOUR ",
        " TOUR DE ",
        " ZUG",
        " AM ZUG",
        " TURNO",
        " TURNO DE ",
        " VEZ DE ",
        "のターン",
        "回合",
        "턴",
        " ВАШ ХОД",
        " ХОД СОПЕРНИКА",
        " ХОД ПРОТИВНИКА",
        " ТВОЙ ХОД",
        " хід"
    ];

    public static unsafe bool ReadIsPlayerTurn(AtkUnitBase* unit, ETriadCardOwner localColor = ETriadCardOwner.Blue)
    {
        if (unit is null || unit->AtkValuesCount <= PlayerTurnAtkValueIndex)
        {
            return false;
        }

        ref var value = ref unit->AtkValues[PlayerTurnAtkValueIndex];
        var turnVal = value.Type switch
        {
            AtkValueType.Int => value.Int,
            AtkValueType.UInt => (int)value.UInt,
            _ => -1
        };

        if (turnVal <= 0)
        {
            return false;
        }

        // In FFXIV's AddonTripleTriad, AtkValues[23] represents whether the local player's input
        // is active (1 = active turn, 0 = inactive/waiting for opponent). It does NOT change to 2 for Red.
        // We check (turnVal is 1 or 2) to ensure both Blue and Red local turns are recognized.
        return turnVal is 1 or 2;
    }

    public static bool IsBoardPickPhase(byte turnState) => turnState == (byte)TurnState.NormalMove;

    public static bool IsForcedCardPickPhase(byte turnState) => turnState == (byte)TurnState.MaskedMove;

    public static bool CanAct(byte turnState, bool isPlayerTurn) =>
        IsForcedCardPickPhase(turnState) ||
        (IsBoardPickPhase(turnState) && isPlayerTurn);

    public static bool CanBlueAct(byte turnState, bool isPlayerTurn) => CanAct(turnState, isPlayerTurn);

    public static unsafe bool IsTurnBannerVisible(AtkUnitBase* unit) =>
        unit is not null && HasTurnBannerText(unit->RootNode, 0);

    private static unsafe bool HasTurnBannerText(AtkResNode* node, int depth)
    {
        if (depth > MaxBannerWalkDepth || !GUINodeUtils.IsNodeVisible(node))
        {
            return false;
        }

        if ((int)node->Type == (int)NodeType.Text && IsTurnBannerText(GUINodeUtils.GetNodeText(node)))
        {
            return true;
        }

        foreach (var child in GUINodeUtils.GetImmediateChildNodes(node) ?? [])
        {
            if (HasTurnBannerText(child, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTurnBannerText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 96)
        {
            return false;
        }

        foreach (var marker in TurnBannerMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
