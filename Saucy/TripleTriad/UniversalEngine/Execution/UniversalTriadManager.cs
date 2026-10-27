using FFXIVClientStructs.FFXIV.Client.UI;
using Saucy.TripleTriad.GameLogic;
using Saucy.TripleTriad.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Saucy.TripleTriad.UniversalEngine;

public static unsafe class UniversalTriadManager
{
    public static UniversalEngineStatus Status { get; private set; } = UniversalEngineStatus.Idle;
    public static string StatusSummary { get; private set; } = "Idle";

    public static void SetStatus(UniversalEngineStatus status, string summary)
    {
        Status = status;
        StatusSummary = summary;
    }

    private static Task<(int cardIdx, int boardPos, float ev, int evalMs, List<UniversalTriadSolver.CandidateMove> candidates)>? _evalTask;
    private static DateTime _turnStartTime;
    private static bool _turnStartTracked;
    private static int _lastCardsPlacedCount = -1;
    private static int _bestCardIdx = -1;
    private static int _bestBoardPos = -1;
    private static float _bestEv;
    private static float _bestPosScore;
    private static int _lastEvalMs;
    private static string _lastStrategy = "";
    private static List<UniversalTriadSolver.CandidateMove> _lastCandidates = [];
    private static bool _matchStarted;
    private static readonly List<TriadCard> _initialRoundPlayerCards = [];
    private static readonly TriadCard?[] _prevBoardCards = new TriadCard?[9];
    private static readonly ETriadCardOwner[] _prevBoardOwners = new ETriadCardOwner[9];
    private static int _pendingOpponentSlot = -1;
    private static TriadCard? _pendingOpponentCard;
    private static DateTime _pendingOpponentTime = DateTime.MinValue;
    private static bool _waitingForMatchEnd;
    private static DateTime _matchEndWaitTime = DateTime.MinValue;
    private static bool _moveSubmittedThisTurn;
    private static int _submittedCardsPlacedCount = -1;
    private static DateTime _moveSubmittedTime = DateTime.MinValue;

    public static void Tick()
    {
        if (!C.UniversalTriadAutoplayEnabled && !C.UniversalTriadTournamentEnabled)
        {
            Reset();
            return;
        }

        if (!TriadUiState.IsBoardVisible())
        {
            Reset();
            return;
        }

        if (!TriadLocalClientStructs.TryGetBoard(out var addon, false))
        {
            Reset();
            return;
        }

        if (!UniversalStateReader.TryReadState(addon, out var state) || state == null)
        {
            return;
        }

        if (Data.TriadCardDB.Get().cards.Count < 50)
        {
            TriadGameAgentCardsharp.SyncCardDatabase();
        }

        var mode = C.UniversalTriadTournamentEnabled
            ? UniversalEngineMode.OpenTournamentDraft
            : UniversalEngineMode.UniversalMatch;

        // Midgame activation: if autoplay was toggled on midgame, snapshot current board
        if (_lastCardsPlacedCount < 0 && state.NumCardsPlaced > 0)
        {
            _lastCardsPlacedCount = state.NumCardsPlaced;
            for (var i = 0; i < 9; i++)
            {
                _prevBoardCards[i] = state.BoardCards[i];
                _prevBoardOwners[i] = state.BoardOwners[i];
            }
        }

        // Detect board restart (e.g. Sudden Death round 2)
        if (state.NumCardsPlaced == 0 && _lastCardsPlacedCount > 4)
        {
            ResetTurn();
            _initialRoundPlayerCards.Clear();
            if (_matchStarted)
            {
                UniversalMatchRecorder.EndMatch(5, 5, "SUDDEN DEATH DRAW (Restarting Round 2)");
                _matchStarted = false;
            }
        }

        // Detect opponent move when cards placed count increases
        if (_matchStarted && state.NumCardsPlaced > _lastCardsPlacedCount && _lastCardsPlacedCount >= 0)
        {
            var oppColor = state.OpponentColor;
            for (var i = 0; i < 9; i++)
            {
                if (_prevBoardCards[i] == null && state.BoardCards[i] != null && state.BoardOwners[i] == oppColor)
                {
                    _pendingOpponentSlot = i;
                    _pendingOpponentCard = state.BoardCards[i];
                    _pendingOpponentTime = DateTime.UtcNow;
                    break;
                }
            }
            _lastCardsPlacedCount = state.NumCardsPlaced;
        }

        // Resolve pending opponent move when board flip animation has finished
        if (_matchStarted && _pendingOpponentSlot >= 0)
        {
            var isSettled = state.IsPlayerTurn ||
                (state.NumCardsPlaced == 9 && (DateTime.UtcNow - _pendingOpponentTime).TotalMilliseconds >= 2200) ||
                (DateTime.UtcNow - _pendingOpponentTime).TotalMilliseconds >= 2500;

            if (isSettled)
            {
                var capturedSlots = new List<int>();
                var oppColor = state.OpponentColor;
                var localColor = state.LocalPlayerColor;
                for (var j = 0; j < 9; j++)
                {
                    if (j != _pendingOpponentSlot && _prevBoardOwners[j] == localColor && state.BoardOwners[j] == oppColor)
                    {
                        capturedSlots.Add(j);
                    }
                }

                UniversalMatchRecorder.RecordOpponentMove(state, _pendingOpponentSlot, _pendingOpponentCard, capturedSlots);
                _pendingOpponentSlot = -1;
                _pendingOpponentCard = null;

                for (var i = 0; i < 9; i++)
                {
                    _prevBoardCards[i] = state.BoardCards[i];
                    _prevBoardOwners[i] = state.BoardOwners[i];
                }
            }
        }

        // Snapshot initial 5 player cards for the round and initialize match recorder
        if (_initialRoundPlayerCards.Count != 5 && state.AvailableLocalCardCount == 5)
        {
            _initialRoundPlayerCards.Clear();
            var hand = state.LocalPlayerHand;
            for (var i = 0; i < 5; i++)
            {
                if (hand[i] != null)
                {
                    _initialRoundPlayerCards.Add(hand[i]!);
                }
            }

            if (!_matchStarted)
            {
                _matchStarted = true;
                UniversalMatchRecorder.StartMatch(state, mode);
            }
        }

        // Update previous board snapshot when no opponent move is pending
        if (_pendingOpponentSlot < 0)
        {
            for (var i = 0; i < 9; i++)
            {
                _prevBoardCards[i] = state.BoardCards[i];
                _prevBoardOwners[i] = state.BoardOwners[i];
            }
            _lastCardsPlacedCount = state.NumCardsPlaced;
        }

        // Finalize match if all 9 cards are placed on board and flip animation has settled
        if (state.NumCardsPlaced == 9 && _matchStarted)
        {
            if (!_waitingForMatchEnd)
            {
                _waitingForMatchEnd = true;
                _matchEndWaitTime = DateTime.UtcNow;
                return;
            }

            var resultAddonVisible = TriadLocalClientStructs.TryGetResult(out _, requireVisible: true);
            var isEndSettled = resultAddonVisible || (DateTime.UtcNow - _matchEndWaitTime).TotalMilliseconds >= 2200;

            if (!isEndSettled)
            {
                return;
            }

            // If there's still a pending 9th move (opponent), flush it now with settled board
            if (_pendingOpponentSlot >= 0)
            {
                var capturedSlots = new List<int>();
                var oppColor = state.OpponentColor;
                var localColor = state.LocalPlayerColor;
                for (var j = 0; j < 9; j++)
                {
                    if (j != _pendingOpponentSlot && _prevBoardOwners[j] == localColor && state.BoardOwners[j] == oppColor)
                    {
                        capturedSlots.Add(j);
                    }
                }
                UniversalMatchRecorder.RecordOpponentMove(state, _pendingOpponentSlot, _pendingOpponentCard, capturedSlots);
                _pendingOpponentSlot = -1;
                _pendingOpponentCard = null;
            }

            var localCount = 0;
            var oppCount = 0;
            for (var i = 0; i < 9; i++)
            {
                if (state.BoardOwners[i] == state.LocalPlayerColor) localCount++;
                else if (state.BoardOwners[i] == state.OpponentColor) oppCount++;
            }
            var localInHand = 5 - state.LocalCardPlaced.Count(p => p);
            var oppInHand = 5 - state.OpponentCardPlaced.Count(p => p);
            var localFinal = localCount + localInHand;
            var oppFinal = oppCount + oppInHand;
            var outcome = localFinal > oppFinal
                ? $"{state.LocalPlayerColor} WINS (VICTORY)"
                : (localFinal == oppFinal ? "DRAW" : $"{state.LocalPlayerColor} LOST (DEFEAT)");
            UniversalMatchRecorder.EndMatch(localFinal, oppFinal, outcome);
            _matchStarted = false;
            _waitingForMatchEnd = false;
        }

        if (_initialRoundPlayerCards.Count == 5)
        {
            state.InitialPlayerCards = [.. _initialRoundPlayerCards];
        }
        else
        {
            // Fallback: reconstruct from remaining local hand and player-owned board cards
            var reconstructed = new List<TriadCard>();
            var hand = state.LocalPlayerHand;
            for (var i = 0; i < 5; i++)
            {
                if (hand[i] != null)
                {
                    reconstructed.Add(hand[i]!);
                }
            }
            for (var i = 0; i < 9; i++)
            {
                if (state.BoardCards[i] != null && state.BoardOwners[i] == state.LocalPlayerColor)
                {
                    reconstructed.Add(state.BoardCards[i]!);
                }
            }
            state.InitialPlayerCards = reconstructed;

            if (!_matchStarted && state.AvailableLocalCardCount > 0)
            {
                _matchStarted = true;
                UniversalMatchRecorder.StartMatch(state, mode);
            }
        }

        if (_moveSubmittedThisTurn)
        {
            if (!state.IsPlayerTurn || state.NumCardsPlaced > _submittedCardsPlacedCount)
            {
                _moveSubmittedThisTurn = false;
            }
            else if ((DateTime.UtcNow - _moveSubmittedTime).TotalMilliseconds > 3500)
            {
                _moveSubmittedThisTurn = false;
            }
            else
            {
                Status = UniversalEngineStatus.WaitingTurn;
                StatusSummary = "Move placed. Waiting for turn end...";
                return;
            }
        }

        var canAct = TriadTurnState.CanAct(state.TurnState, state.IsPlayerTurn);
        if (!canAct)
        {
            if (Status != UniversalEngineStatus.WaitingTurn)
            {
                ResetTurn();
                Status = UniversalEngineStatus.WaitingTurn;
                StatusSummary = $"Waiting for opponent ({state.OpponentColor})...";
            }
            return;
        }

        // Player's turn!
        if (!_turnStartTracked)
        {
            _turnStartTime = DateTime.UtcNow;
            _turnStartTracked = true;
        }

        if (Status is UniversalEngineStatus.WaitingTurn or UniversalEngineStatus.Idle)
        {
            Status = UniversalEngineStatus.Evaluating;
            StatusSummary = $"Thinking ({state.LocalPlayerColor})...";

            var sampleCount = Math.Clamp(C.UniversalTriadSampleCount, 50, 500);

            _evalTask = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var solver = new UniversalTriadSolver(mode, sampleCount, SaucyParallelism.UniversalTriadThreads);
                var (cardIdx, boardPos, ev, candidates) = solver.Solve(state);
                sw.Stop();
                return (cardIdx, boardPos, ev, (int)sw.ElapsedMilliseconds, candidates);
            });
            return;
        }

        if (Status == UniversalEngineStatus.Evaluating)
        {
            if (_evalTask == null)
            {
                ResetTurn();
                return;
            }

            if (!_evalTask.IsCompleted)
            {
                return;
            }

            if (_evalTask.IsFaulted)
            {
                Svc.Log.Error(_evalTask.Exception, "[UniversalTriadManager] Solve task faulted");
                ResetTurn();
                Status = UniversalEngineStatus.Idle;
                StatusSummary = "Solve failed, retrying...";
                return;
            }

            var result = _evalTask.Result;
            _bestCardIdx = result.cardIdx;
            _bestBoardPos = result.boardPos;
            _bestEv = result.ev;
            _lastEvalMs = result.evalMs;
            _lastCandidates = result.candidates;
            _lastStrategy = state.NumCardsPlaced >= 4 ? "Endgame Exhaustive Alpha-Beta (Depth 10)" : (state.NumCardsPlaced <= 1 ? "Opening Adversarial Minimax (Depth 4)" : "Midgame Adversarial Minimax (Depth 6)");
            var matchingCandidate = _lastCandidates.FirstOrDefault(c => c.CardIdx == _bestCardIdx && c.BoardPos == _bestBoardPos);
            _bestPosScore = matchingCandidate.PositionalScore;
            _evalTask = null;

            if (_bestCardIdx < 0 || _bestBoardPos < 0)
            {
                StatusSummary = "No legal moves available";
                return;
            }

            Status = UniversalEngineStatus.MoveReady;
            StatusSummary = $"Ready [{state.LocalPlayerColor}]: Card #{_bestCardIdx + 1} -> ({_bestBoardPos % 3}, {_bestBoardPos / 3}), EV: {_bestEv:P0} ({_lastEvalMs}ms)";
        }

        if (Status == UniversalEngineStatus.MoveReady)
        {
            if (UniversalMoveExecutor.CanExecute(addon, state.IsPlayerTurn, _turnStartTime, C.UniversalTriadMoveDelayMs))
            {
                Status = UniversalEngineStatus.Executing;
                StatusSummary = $"Placing Card #{_bestCardIdx + 1} at ({_bestBoardPos % 3}, {_bestBoardPos / 3})...";

                var success = UniversalMoveExecutor.Execute(addon, _bestCardIdx, _bestBoardPos);
                if (success)
                {
                    UniversalMatchRecorder.RecordPlayerMove(
                        state,
                        _bestCardIdx,
                        _bestBoardPos,
                        _bestEv,
                        _bestPosScore,
                        _lastEvalMs,
                        _lastStrategy,
                        _lastCandidates);

                    ResetTurn();
                    _moveSubmittedThisTurn = true;
                    _submittedCardsPlacedCount = state.NumCardsPlaced;
                    _moveSubmittedTime = DateTime.UtcNow;
                    Status = UniversalEngineStatus.WaitingTurn;
                    StatusSummary = "Move placed. Waiting for turn end...";
                }
                else
                {
                    // Retry next tick if placement temporarily failed
                    Status = UniversalEngineStatus.MoveReady;
                }
            }
        }
    }

    public static void Reset()
    {
        if (_matchStarted)
        {
            UniversalMatchRecorder.AbortMatch("Match ended or board closed");
            _matchStarted = false;
        }
        ResetTurn();
        Status = UniversalEngineStatus.Idle;
        StatusSummary = "Idle";
        _lastCardsPlacedCount = -1;
        _moveSubmittedThisTurn = false;
        _submittedCardsPlacedCount = -1;
        _moveSubmittedTime = DateTime.MinValue;
        _pendingOpponentSlot = -1;
        _pendingOpponentCard = null;
        _waitingForMatchEnd = false;
        _initialRoundPlayerCards.Clear();
        Array.Clear(_prevBoardCards, 0, 9);
        Array.Clear(_prevBoardOwners, 0, 9);
    }

    private static void ResetTurn()
    {
        _evalTask = null;
        _turnStartTracked = false;
        _bestCardIdx = -1;
        _bestBoardPos = -1;
        _bestEv = 0.0f;
        _bestPosScore = 0.0f;
        _lastStrategy = "";
        _lastCandidates = [];
        if (Status == UniversalEngineStatus.Evaluating)
        {
            Status = UniversalEngineStatus.Idle;
            StatusSummary = "Idle";
        }
    }
}
