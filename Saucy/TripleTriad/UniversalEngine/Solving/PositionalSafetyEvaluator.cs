using Saucy.TripleTriad.GameLogic;
using System;

namespace Saucy.TripleTriad.UniversalEngine;

public static class PositionalSafetyEvaluator
{
    private static readonly bool[] IsCorner = [true, false, true, false, false, false, true, false, true];
    private static readonly bool[] IsEdge = [false, true, false, true, false, true, false, true, false];

    public static float Evaluate(UniversalTriadState state, TriadCard? card, int boardPos)
    {
        if (card == null || boardPos < 0 || boardPos >= 9)
        {
            return 0.0f;
        }

        var score = 0.0f;

        // 1. Positional corner & edge preference
        var isOpening = state.NumCardsPlaced <= 1;
        if (IsCorner[boardPos])
        {
            score += isOpening ? 0.55f : 0.40f;
        }
        else if (IsEdge[boardPos])
        {
            score += 0.20f;
        }
        else if (boardPos == 4 && isOpening)
        {
            // Center opening penalty: exposing all 4 sides early without walls is high-risk
            score -= 0.30f;
        }

        // 2. Neighbor analysis across 4 cardinal directions (Up, Left, Down, Right)
        var isReverse = state.Modifiers.Exists(m => m is TriadGameModifierReverse || (m is TriadGameModifierRoulette r && r.GetResolvedRule() is TriadGameModifierReverse)) ||
            state.RuleNames.Exists(r =>
                r.Contains("Reverse", StringComparison.OrdinalIgnoreCase) ||
                r.Contains("Inversion", StringComparison.OrdinalIgnoreCase) ||
                r.Contains("リバース", StringComparison.OrdinalIgnoreCase) ||
                r.Contains("逆转", StringComparison.OrdinalIgnoreCase));

        var isFallenAce = state.Modifiers.Exists(m => m is TriadGameModifierFallenAce || (m is TriadGameModifierRoulette r && r.GetResolvedRule() is TriadGameModifierFallenAce)) ||
            state.RuleNames.Exists(r =>
                r.Contains("Fallen Ace", StringComparison.OrdinalIgnoreCase) ||
                r.Contains("Ascendant", StringComparison.OrdinalIgnoreCase) ||
                r.Contains("エースキラー", StringComparison.OrdinalIgnoreCase) ||
                r.Contains("王牌杀手", StringComparison.OrdinalIgnoreCase));

        var neis = TriadGameSimulation.GetNeighbors(boardPos);
        var exposedSideCount = 0;
        var exposedSidePowerSum = 0;

        for (var side = 0; side < 4; side++)
        {
            var neiPos = neis[side];
            var sideNumber = card.Sides[side];

            if (neiPos < 0)
            {
                // Perimeter wall: invulnerable side
                if (isReverse)
                {
                    // In Reverse, large numbers are vulnerable; placing them against walls shields them
                    if (sideNumber >= 8)
                    {
                        score += 0.10f;
                    }
                }
                else
                {
                    // In Normal, small numbers placed against perimeter walls are safely shielded
                    if (sideNumber <= 4)
                    {
                        score += 0.10f;
                    }
                }
            }
            else if (state.BoardCards[neiPos] == null)
            {
                // Exposed side facing an open board slot that opponent can attack
                exposedSideCount++;

                if (isReverse)
                {
                    // In Reverse: smaller numbers are stronger!
                    // Invert effective power: 1 -> 10, 2 -> 9, ..., 10 -> 1
                    exposedSidePowerSum += (11 - sideNumber);

                    if (sideNumber <= 2)
                    {
                        score += 0.18f;
                    }
                    else if (sideNumber <= 4)
                    {
                        score += 0.08f;
                    }
                    else if (sideNumber >= 9)
                    {
                        score -= (isFallenAce && sideNumber == 10) ? 0.04f : 0.25f;
                    }
                    else if (sideNumber >= 7)
                    {
                        score -= 0.10f;
                    }
                }
                else
                {
                    // Standard rules: high numbers resist captures
                    exposedSidePowerSum += sideNumber;

                    if (sideNumber >= 9)
                    {
                        score += 0.18f;
                    }
                    else if (sideNumber >= 7)
                    {
                        score += 0.08f;
                    }
                    else if (sideNumber <= 3)
                    {
                        // Heavily penalize exposing weak side to open board
                        score -= 0.25f;
                    }
                    else if (sideNumber <= 5)
                    {
                        score -= 0.10f;
                    }
                }
            }
        }

        if (exposedSideCount > 0)
        {
            var avgExposedDefense = exposedSidePowerSum / (exposedSideCount * 10.0f);
            score += avgExposedDefense * 0.30f;
        }

        // Rarity Pacing for Open Tournament Draft:
        // On opening plies (<= 1 card placed), penalize prematurely burning ★5 trumps
        // unless they are completely uncapturable (e.g. corner with both exposed sides >= 9)
        if (state.NumCardsPlaced <= 1)
        {
            var allExposedFortified = true;
            for (var s = 0; s < 4; s++)
            {
                if (neis[s] >= 0 && state.BoardCards[neis[s]] == null)
                {
                    var val = card.Sides[s];
                    if (isReverse)
                    {
                        if (val > 2) allExposedFortified = false;
                    }
                    else
                    {
                        if (val < 9) allExposedFortified = false;
                    }
                }
            }

            if (card.Rarity == ETriadCardRarity.Legendary && !allExposedFortified)
            {
                score -= 0.35f;
            }
            else if (card.Rarity == ETriadCardRarity.Common)
            {
                score += 0.18f;
            }
            else if (card.Rarity == ETriadCardRarity.Uncommon)
            {
                score += 0.10f;
            }
        }

        // 3. Combo vulnerability penalty under Same / Plus rules
        var hasSameOrPlus = state.Modifiers.Exists(m => m.AllowsCombo()) ||
            state.RuleNames.Exists(r =>
                r.Contains("Same", StringComparison.OrdinalIgnoreCase) ||
                r.Contains("Plus", StringComparison.OrdinalIgnoreCase) ||
                r.Contains("Identique", StringComparison.OrdinalIgnoreCase));

        if (hasSameOrPlus)
        {
            for (var s1 = 0; s1 < 4; s1++)
            {
                if (neis[s1] >= 0 && state.BoardCards[neis[s1]] == null)
                {
                    for (var s2 = s1 + 1; s2 < 4; s2++)
                    {
                        if (neis[s2] >= 0 && state.BoardCards[neis[s2]] == null)
                        {
                            if (card.Sides[s1] == card.Sides[s2])
                            {
                                score -= 0.08f;
                            }
                        }
                    }
                }
            }
        }

        return Math.Max(0.0f, score);
    }
}
