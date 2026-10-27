using FFXIVClientStructs.FFXIV.Client.UI;
using Saucy.TripleTriad.Data;
using Saucy.TripleTriad.GameLogic;
using Saucy.TripleTriad.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using XIVcardsharp.Core.State;
using CoreCard = XIVcardsharp.Core.Data.TriadCard;
using CoreCardDB = XIVcardsharp.Core.Data.TriadCardDB;
using CoreCardOwner = XIVcardsharp.Core.Data.ETriadCardOwner;
using CoreCardRarity = XIVcardsharp.Core.Data.ETriadCardRarity;
using CoreCardType = XIVcardsharp.Core.Data.ETriadCardType;
using CoreModifier = XIVcardsharp.Core.Engine.TriadGameModifier;
using CoreModifierDB = XIVcardsharp.Core.Engine.TriadGameModifierDB;

namespace Saucy.TripleTriad.UniversalEngine;

public static unsafe class UniversalStateReader
{
    public static bool TryReadState(AddonTripleTriad* addon, out UniversalTriadState? state)
    {
        state = null;
        if (addon is null)
        {
            return false;
        }

        uiReaderGame.SyncCurrentFromAddon((nint)addon);
        var uiState = uiReaderGame.currentState;
        if (uiState == null)
        {
            return false;
        }

        TriadGameAgentCardsharp.SyncCardDatabase();
        var parser = new GameUIParser();

        // 1. Detect Local Player Color (Blue vs Red)
        var hasHiddenBlue = false;
        var hasHiddenRed = false;
        for (var i = 0; i < 5; i++)
        {
            if (uiState.blueDeck[i].isPresent && uiState.blueDeck[i].IsHidden) hasHiddenBlue = true;
            if (uiState.redDeck[i].isPresent && uiState.redDeck[i].IsHidden) hasHiddenRed = true;
        }

        CoreCardOwner localColor;
        if (hasHiddenBlue && !hasHiddenRed)
        {
            localColor = CoreCardOwner.Red;
        }
        else if (hasHiddenRed && !hasHiddenBlue)
        {
            localColor = CoreCardOwner.Blue;
        }
        else
        {
            // Fallback (e.g. All Open rule or both fully known): check red player nameplate
            var localPlayerName = Svc.Objects.LocalPlayer?.Name.TextValue ?? "";
            var isRedPlayerLocal = !string.IsNullOrEmpty(localPlayerName) &&
                uiState.redPlayerDesc.Any(d => d.Contains(localPlayerName, StringComparison.OrdinalIgnoreCase));
            localColor = isRedPlayerLocal ? CoreCardOwner.Red : CoreCardOwner.Blue;
        }

        var ruleNames = uiState.rules
            .SelectMany(r => r.Split(['/', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct()
            .ToList();

        var isPlayerTurn = TriadTurnState.ReadIsPlayerTurn(&addon->AtkUnitBase, localColor == CoreCardOwner.Blue ? ETriadCardOwner.Blue : ETriadCardOwner.Red);

        var result = new UniversalTriadState
        {
            LocalPlayerColor = localColor,
            TurnState = (byte)addon->TurnState,
            IsPlayerTurn = isPlayerTurn,
            TurnBannerVisible = TriadTurnState.IsTurnBannerVisible(&addon->AtkUnitBase),
            RuleNames = ruleNames,
            Modifiers = ResolveModifiers(ruleNames)
        };

        // 2. Extract Board Cards (0..8)
        for (var i = 0; i < 9; i++)
        {
            var boardCard = uiState.board[i];
            if (boardCard.isPresent)
            {
                var card = ResolveCard(boardCard, parser);
                result.BoardCards[i] = card;
                result.BoardOwners[i] = boardCard.owner == 1 ? CoreCardOwner.Blue :
                                        boardCard.owner == 2 ? CoreCardOwner.Red :
                                        CoreCardOwner.Unknown;
                result.NumCardsPlaced++;

                if (card != null)
                {
                    if (result.BoardOwners[i] == CoreCardOwner.Red)
                    {
                        result.KnownRedCards.Add(card);
                    }
                    else if (result.BoardOwners[i] == CoreCardOwner.Blue)
                    {
                        result.KnownBlueCards.Add(card);
                    }
                }
            }
        }

        // 3. Extract Blue Hand (0..4)
        for (var i = 0; i < 5; i++)
        {
            var blueCard = uiState.blueDeck[i];
            if (!blueCard.isPresent)
            {
                result.BlueCardPlaced[i] = true;
                result.BlueHand[i] = null;
            }
            else
            {
                result.BlueCardPlaced[i] = false;
                if (blueCard.IsHidden)
                {
                    result.BlueCardRevealed[i] = false;
                    result.BlueHand[i] = null;
                    result.HiddenBlueCount++;
                }
                else
                {
                    result.BlueCardRevealed[i] = true;
                    var card = ResolveCard(blueCard, parser);
                    result.BlueHand[i] = card;
                    if (card != null)
                    {
                        result.KnownBlueCards.Add(card);
                    }
                }
            }
        }

        // 4. Extract Red Hand (0..4)
        for (var i = 0; i < 5; i++)
        {
            var redCard = uiState.redDeck[i];
            if (!redCard.isPresent)
            {
                result.RedCardPlaced[i] = true;
                result.RedHand[i] = null;
            }
            else
            {
                result.RedCardPlaced[i] = false;
                if (redCard.IsHidden)
                {
                    result.RedCardRevealed[i] = false;
                    result.RedHand[i] = null;
                    result.HiddenRedCount++;
                }
                else
                {
                    result.RedCardRevealed[i] = true;
                    var card = ResolveCard(redCard, parser);
                    result.RedHand[i] = card;
                    if (card != null)
                    {
                        result.KnownRedCards.Add(card);
                    }
                }
            }
        }

        // 5. Detect forced card pick under Chaos modifier (on active player's deck)
        if (TriadTurnState.IsForcedCardPickPhase(result.TurnState))
        {
            var activeDeck = (result.LocalPlayerColor == CoreCardOwner.Blue) ? uiState.blueDeck : uiState.redDeck;
            for (var i = 0; i < 5; i++)
            {
                if (activeDeck[i].isPresent && !activeDeck[i].isLocked)
                {
                    result.ForcedCardIdx = i;
                    break;
                }
            }
        }

        state = result;
        return true;
    }

    private static CoreCard? ResolveCard(UIStateTriadCard cardState, GameUIParser parser)
    {
        if (!cardState.isPresent)
        {
            return null;
        }

        var coreDb = CoreCardDB.Get();
        if (cardState.IsHidden)
        {
            return coreDb.hiddenCard;
        }

        // 1. Try parsing through standard GameUIParser and map to coreDb
        var card = cardState.ToTriadCard(parser, markFailed: false);
        if (card != null && card.IsValid() && card.Id >= 0 && card.Id < coreDb.cards.Count && coreDb.cards[card.Id] != null)
        {
            return coreDb.cards[card.Id];
        }

        // 2. Fallback: Lookup by sides directly in CoreCardDB
        var fallback = coreDb.Find(cardState.numU, cardState.numL, cardState.numD, cardState.numR);
        if (fallback != null && fallback.IsValid())
        {
            return fallback;
        }

        // 3. Fallback: Lookup by texture path directly in CoreCardDB
        if (!string.IsNullOrEmpty(cardState.texturePath))
        {
            var texFallback = coreDb.FindByTexture(cardState.texturePath);
            if (texFallback != null && texFallback.IsValid())
            {
                return texFallback;
            }
        }

        // 4. Bulletproof Fallback: Construct synthetic core card from memory stats so board tile is never null
        if (cardState.numU > 0 && cardState.numL > 0 && cardState.numD > 0 && cardState.numR > 0)
        {
            var cardType = GameDataLoader.ConvertToTriadType(cardState.type);
            var cardRarity = GameDataLoader.ConvertToTriadRarity(cardState.rarity);
            return new CoreCard(
                -1,
                (CoreCardRarity)Math.Clamp((int)cardRarity, 0, 4),
                (CoreCardType)Math.Clamp((int)cardType, 0, 4),
                cardState.numU,
                cardState.numD,
                cardState.numL,
                cardState.numR,
                0,
                0,
                $"Card [{cardState.numU}-{cardState.numL}-{cardState.numD}-{cardState.numR}]");
        }

        return null;
    }

    private static List<CoreModifier> ResolveModifiers(List<string> ruleNames)
    {
        var result = new List<CoreModifier>();
        var db = CoreModifierDB.Get();

        foreach (var rule in ruleNames)
        {
            foreach (var mod in db.mods)
            {
                if (mod.GetCodeName().Equals(rule, StringComparison.OrdinalIgnoreCase) ||
                    mod.GetLocalizedName().Equals(rule, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(CoreModifierDB.CreateFresh(mod));
                    break;
                }
            }
        }

        return result;
    }
}
