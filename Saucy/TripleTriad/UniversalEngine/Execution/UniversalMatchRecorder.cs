using Saucy.TripleTriad.GameLogic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Saucy.TripleTriad.UniversalEngine;

public static class UniversalMatchRecorder
{
    private static readonly string LogsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XIVLauncher", "pluginConfigs", "Saucy", "UniversalMatchLogs");

    private static MatchSession? _activeMatch;

    public static void OpenLogsFolder()
    {
        try
        {
            var dir = LogsDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[UniversalMatchRecorder] Failed to open logs directory");
        }
    }

    public static void StartMatch(UniversalTriadState state, UniversalEngineMode mode)
    {
        if (!C.UniversalTriadRecordMatches)
        {
            return;
        }

        if (_activeMatch != null && !_activeMatch.IsFinished)
        {
            _activeMatch.Finish("Game closed or reset before final card placed");
        }

        _activeMatch = new MatchSession(state, mode);
    }

    public static void RecordPlayerMove(
        UniversalTriadState state,
        int cardIdx,
        int boardPos,
        float ev,
        float posScore,
        int evalMs,
        string strategy,
        IReadOnlyList<UniversalTriadSolver.CandidateMove>? candidates = null)
    {
        if (!C.UniversalTriadRecordMatches || _activeMatch == null)
        {
            return;
        }

        _activeMatch.RecordPlayerTurn(state, cardIdx, boardPos, ev, posScore, evalMs, strategy, candidates);
    }

    // Compatibility alias for RecordPlayerMove
    public static void RecordBlueMove(
        UniversalTriadState state,
        int cardIdx,
        int boardPos,
        float ev,
        float posScore,
        int evalMs,
        string strategy,
        IReadOnlyList<UniversalTriadSolver.CandidateMove>? candidates = null)
    {
        RecordPlayerMove(state, cardIdx, boardPos, ev, posScore, evalMs, strategy, candidates);
    }

    public static void RecordOpponentMove(
        UniversalTriadState state,
        int boardPos,
        TriadCard? card,
        List<int> capturedSlots)
    {
        if (!C.UniversalTriadRecordMatches || _activeMatch == null)
        {
            return;
        }

        _activeMatch.RecordOpponentTurn(state, boardPos, card, capturedSlots);
    }

    // Compatibility alias for RecordOpponentMove
    public static void RecordRedMove(
        UniversalTriadState state,
        int boardPos,
        TriadCard? card,
        List<int> capturedSlots)
    {
        RecordOpponentMove(state, boardPos, card, capturedSlots);
    }

    public static void EndMatch(int localScore, int oppScore, string outcome)
    {
        if (_activeMatch == null)
        {
            return;
        }

        _activeMatch.Finish($"{outcome} (Player {localScore} - Opponent {oppScore})");
        _activeMatch = null;
    }

    public static void AbortMatch(string reason)
    {
        if (_activeMatch == null)
        {
            return;
        }

        _activeMatch.Finish($"Aborted: {reason}");
        _activeMatch = null;
    }

    private class MatchSession
    {
        public string FileName { get; }
        public DateTime StartTime { get; }
        public UniversalEngineMode Mode { get; }
        public bool IsFinished { get; private set; }

        private readonly StringBuilder _sb = new();
        private int _plyCount;

        public MatchSession(UniversalTriadState state, UniversalEngineMode mode)
        {
            StartTime = DateTime.UtcNow;
            Mode = mode;
            FileName = $"match_{StartTime:yyyyMMdd_HHmmss}.log";

            _sb.AppendLine("================================================================================");
            _sb.AppendLine("TRIPLE TRIAD MATCH RECORD");
            _sb.AppendLine("================================================================================");
            _sb.AppendLine($"Timestamp : {StartTime:yyyy-MM-dd HH:mm:ss} UTC");
            _sb.AppendLine($"EngineMode: {mode}");
            _sb.AppendLine($"Rules     : {(state.RuleNames.Count > 0 ? string.Join(", ", state.RuleNames) : "None (Standard)")}");
            _sb.AppendLine($"Side      : {state.LocalPlayerColor} (Bot) vs {state.OpponentColor} (Opponent)");
            _sb.AppendLine();

            _sb.AppendLine($"INITIAL BLUE HAND ({(state.LocalPlayerColor == ETriadCardOwner.Blue ? "Player" : "Opponent")}):");
            for (var i = 0; i < 5; i++)
            {
                var card = state.BlueHand[i];
                if (card != null && state.BlueCardRevealed[i])
                {
                    _sb.AppendLine($"  Slot #{i + 1}: {card.Name} (★{(int)card.Rarity + 1}) [U:{FormatSide(card.Sides[0])} L:{FormatSide(card.Sides[3])} D:{FormatSide(card.Sides[2])} R:{FormatSide(card.Sides[1])}]");
                }
                else
                {
                    _sb.AppendLine($"  Slot #{i + 1}: [Hidden]");
                }
            }
            _sb.AppendLine();

            _sb.AppendLine($"INITIAL RED HAND ({(state.LocalPlayerColor == ETriadCardOwner.Red ? "Player" : "Opponent")}):");
            for (var i = 0; i < 5; i++)
            {
                var card = state.RedHand[i];
                if (card != null && state.RedCardRevealed[i])
                {
                    _sb.AppendLine($"  Slot #{i + 1}: {card.Name} (★{(int)card.Rarity + 1}) [U:{FormatSide(card.Sides[0])} L:{FormatSide(card.Sides[3])} D:{FormatSide(card.Sides[2])} R:{FormatSide(card.Sides[1])}]");
                }
                else
                {
                    _sb.AppendLine($"  Slot #{i + 1}: [Hidden]");
                }
            }
            _sb.AppendLine();
        }

        public void RecordPlayerTurn(
            UniversalTriadState state,
            int cardIdx,
            int boardPos,
            float ev,
            float posScore,
            int evalMs,
            string strategy,
            IReadOnlyList<UniversalTriadSolver.CandidateMove>? candidates)
        {
            _plyCount++;
            var card = (cardIdx is >= 0 and < 5) ? state.LocalPlayerHand[cardIdx] : null;
            var cardName = card?.Name ?? $"Slot #{cardIdx + 1}";
            var (x, y) = (boardPos % 3, boardPos / 3);

            _sb.AppendLine("--------------------------------------------------------------------------------");
            _sb.AppendLine($"PLY {_plyCount}: {state.LocalPlayerColor} (Bot)");
            _sb.AppendLine("--------------------------------------------------------------------------------");
            _sb.AppendLine($"Action        : Placed '{cardName}' at Tile ({x}, {y}) [Slot {boardPos}]");
            _sb.AppendLine($"Strategy      : {strategy}");
            _sb.AppendLine($"Decision EV   : {ev:P1} (Positional Safety: {posScore:F2}, Solve Time: {evalMs}ms)");

            if (candidates != null && candidates.Count > 0)
            {
                _sb.AppendLine("Evaluated Top Candidates:");
                var topList = candidates
                    .OrderByDescending(c => c.EV)
                    .ThenByDescending(c => c.PositionalScore)
                    .Take(5)
                    .ToList();

                for (var idx = 0; idx < topList.Count; idx++)
                {
                    var cand = topList[idx];
                    var candCard = (cand.CardIdx is >= 0 and < 5) ? state.LocalPlayerHand[cand.CardIdx] : null;
                    var cName = candCard?.Name ?? $"#{cand.CardIdx + 1}";
                    var isChosen = cand.CardIdx == cardIdx && cand.BoardPos == boardPos;
                    var mark = isChosen ? " <-- CHOSEN" : "";
                    _sb.AppendLine($"  {idx + 1}. {cName} -> ({cand.BoardPos % 3}, {cand.BoardPos / 3}) | EV: {cand.EV:P1}, Pos: {cand.PositionalScore:F2}{mark}");
                }
            }

            _sb.AppendLine();
            AppendAsciiBoard(state, boardPos, state.LocalPlayerColor, card);
        }

        public void RecordOpponentTurn(
            UniversalTriadState state,
            int boardPos,
            TriadCard? card,
            List<int> capturedSlots)
        {
            _plyCount++;
            var cardName = card?.Name ?? "Unknown";
            var (x, y) = (boardPos % 3, boardPos / 3);

            _sb.AppendLine("--------------------------------------------------------------------------------");
            _sb.AppendLine($"PLY {_plyCount}: {state.OpponentColor} (Opponent)");
            _sb.AppendLine("--------------------------------------------------------------------------------");
            _sb.AppendLine($"Action        : Placed '{cardName}' at Tile ({x}, {y}) [Slot {boardPos}]");
            if (capturedSlots.Count > 0)
            {
                var caps = string.Join(", ", capturedSlots.Select(s => $"Tile ({s % 3}, {s / 3})"));
                _sb.AppendLine($"Flips         : Captured {capturedSlots.Count} card(s) at [{caps}]");
            }
            else
            {
                _sb.AppendLine("Flips         : None");
            }

            _sb.AppendLine();
            AppendAsciiBoard(state, boardPos, state.OpponentColor, card);
        }

        public void Finish(string outcome)
        {
            if (IsFinished)
            {
                return;
            }
            IsFinished = true;

            var duration = (DateTime.UtcNow - StartTime).TotalSeconds;
            _sb.AppendLine("================================================================================");
            _sb.AppendLine($"FINAL RESULT: {outcome}");
            _sb.AppendLine($"Total Plies : {_plyCount}");
            _sb.AppendLine($"Duration    : {duration:F1}s");
            _sb.AppendLine("================================================================================");

            try
            {
                var dir = LogsDirectory;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var fullPath = Path.Combine(dir, FileName);
                File.WriteAllText(fullPath, _sb.ToString());
                Svc.Log.Information($"[UniversalMatchRecorder] Match logged successfully to {fullPath}");
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, "[UniversalMatchRecorder] Failed to write match log file");
            }
        }

        private void AppendAsciiBoard(
            UniversalTriadState state,
            int lastMoveSlot,
            ETriadCardOwner lastMoveOwner,
            TriadCard? lastMoveCard)
        {
            var boardOwners = (ETriadCardOwner[])state.BoardOwners.Clone();
            var boardCards = (TriadCard?[])state.BoardCards.Clone();

            if (lastMoveSlot is >= 0 and < 9)
            {
                boardOwners[lastMoveSlot] = lastMoveOwner;
                boardCards[lastMoveSlot] = lastMoveCard ?? boardCards[lastMoveSlot];
            }

            var blueCount = 0;
            var redCount = 0;

            _sb.AppendLine("Board Snapshot:");
            _sb.AppendLine("+--------------+--------------+--------------+");
            for (var row = 0; row < 3; row++)
            {
                var lineOwner = "|";
                var lineName = "|";

                for (var col = 0; col < 3; col++)
                {
                    var slot = col + (row * 3);
                    var owner = boardOwners[slot];
                    var card = boardCards[slot];

                    var ownerTag = owner == ETriadCardOwner.Blue ? "BLUE" :
                                   owner == ETriadCardOwner.Red ? "RED " : " .  ";

                    if (owner == ETriadCardOwner.Blue) blueCount++;
                    else if (owner == ETriadCardOwner.Red) redCount++;

                    var rawName = card?.Name ?? ".";
                    if (rawName.Length > 12)
                    {
                        rawName = rawName[..12];
                    }

                    lineOwner += $" {ownerTag,-12} |";
                    lineName += $" {rawName,-12} |";
                }

                _sb.AppendLine(lineOwner);
                _sb.AppendLine(lineName);
                _sb.AppendLine("+--------------+--------------+--------------+");
            }

            var blueInHand = 5 - (state.BlueCardPlaced.Count(p => p));
            var redInHand = 5 - (state.RedCardPlaced.Count(p => p));
            var blueTotal = blueCount + blueInHand;
            var redTotal = redCount + redInHand;

            _sb.AppendLine($"Score on Board: Blue {blueCount} vs Red {redCount} (Total Cards Held: Blue {blueTotal} - Red {redTotal})");
            _sb.AppendLine();
        }

        private static string FormatSide(int val) => val >= 10 ? "A" : val.ToString();
    }
}
