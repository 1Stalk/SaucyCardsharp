using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.UI;
using Saucy.TripleTriad.UI;
using System;

namespace Saucy.TripleTriad.UniversalEngine;

public static unsafe class UniversalMoveExecutor
{
    private static bool _isExecuting;

    public static bool CanExecute(AddonTripleTriad* addon, bool isPlayerTurn, DateTime turnStartTime, int delayMs)
    {
        if (addon is null || _isExecuting)
        {
            return false;
        }

        var turnState = (byte)addon->TurnState;
        if (!TriadTurnState.CanAct(turnState, isPlayerTurn))
        {
            return false;
        }

        var elapsedMs = (DateTime.UtcNow - turnStartTime).TotalMilliseconds;
        if (elapsedMs < delayMs)
        {
            return false;
        }

        // Only allow banner animation to delay move if under 1200ms and in normal board pick phase
        if (elapsedMs < 1200 && TriadTurnState.IsBoardPickPhase(turnState))
        {
            if (TriadTurnState.IsTurnBannerVisible(&addon->AtkUnitBase))
            {
                return false;
            }
        }

        return true;
    }

    public static bool Execute(AddonTripleTriad* addon, int cardIdx, int boardPos)
    {
        if (addon is null || cardIdx < 0 || cardIdx >= 5 || boardPos < 0 || boardPos >= 9 || _isExecuting)
        {
            return false;
        }

        _isExecuting = true;
        try
        {
            Callback.Fire(&addon->AtkUnitBase, true, 14, (uint)boardPos + ((uint)cardIdx << 16));
            addon->AtkUnitBase.Update(0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[UniversalMoveExecutor] Card placement failed");
            return false;
        }
        finally
        {
            _isExecuting = false;
        }
    }
}
