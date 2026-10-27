using System.Collections.Generic;
using Saucy.TripleTriad.GameLogic;

namespace Saucy.TripleTriad.UniversalEngine;

public class UniversalTriadState
{
    // Player perspective
    public ETriadCardOwner LocalPlayerColor { get; set; } = ETriadCardOwner.Blue;
    public ETriadCardOwner OpponentColor => LocalPlayerColor == ETriadCardOwner.Blue ? ETriadCardOwner.Red : ETriadCardOwner.Blue;

    // Blue hand (index 0..4)
    public TriadCard?[] BlueHand { get; set; } = new TriadCard?[5];
    public bool[] BlueCardPlaced { get; set; } = new bool[5];
    public bool[] BlueCardRevealed { get; set; } = new bool[5];
    public int HiddenBlueCount { get; set; }
    public List<TriadCard> KnownBlueCards { get; set; } = [];

    // Red hand (index 0..4)
    public TriadCard?[] RedHand { get; set; } = new TriadCard?[5];
    public bool[] RedCardPlaced { get; set; } = new bool[5];
    public bool[] RedCardRevealed { get; set; } = new bool[5];
    public int HiddenRedCount { get; set; }
    public List<TriadCard> KnownRedCards { get; set; } = [];

    // Forced pick (e.g. Chaos)
    public int ForcedCardIdx { get; set; } = -1;

    // Board (index 0..8)
    public TriadCard?[] BoardCards { get; set; } = new TriadCard?[9];
    public ETriadCardOwner[] BoardOwners { get; set; } = new ETriadCardOwner[9];

    // Match rules
    public List<TriadGameModifier> Modifiers { get; set; } = [];
    public List<string> RuleNames { get; set; } = [];

    // Turn info
    public bool IsPlayerTurn { get; set; }
    public byte TurnState { get; set; }
    public bool TurnBannerVisible { get; set; }
    public int NumCardsPlaced { get; set; }

    // Snapshot collections
    public List<TriadCard> InitialPlayerCards { get; set; } = [];
    public List<TriadCard> InitialBlueCards
    {
        get => LocalPlayerColor == ETriadCardOwner.Blue ? InitialPlayerCards : KnownBlueCards;
        set => InitialPlayerCards = value;
    }

    // Perspective-aware accessors
    public TriadCard?[] LocalPlayerHand => LocalPlayerColor == ETriadCardOwner.Blue ? BlueHand : RedHand;
    public TriadCard?[] OpponentHand => LocalPlayerColor == ETriadCardOwner.Blue ? RedHand : BlueHand;
    public bool[] LocalCardPlaced => LocalPlayerColor == ETriadCardOwner.Blue ? BlueCardPlaced : RedCardPlaced;
    public bool[] OpponentCardPlaced => LocalPlayerColor == ETriadCardOwner.Blue ? RedCardPlaced : BlueCardPlaced;
    public bool[] OpponentCardRevealed => LocalPlayerColor == ETriadCardOwner.Blue ? RedCardRevealed : BlueCardRevealed;
    public int HiddenOpponentCount => LocalPlayerColor == ETriadCardOwner.Blue ? HiddenRedCount : HiddenBlueCount;
    public List<TriadCard> KnownOpponentCards => LocalPlayerColor == ETriadCardOwner.Blue ? KnownRedCards : KnownBlueCards;

    public int AvailableLocalCardCount
    {
        get
        {
            var hand = LocalPlayerHand;
            var placed = LocalCardPlaced;
            var count = 0;
            for (var i = 0; i < 5; i++)
            {
                if (hand[i] != null && !placed[i])
                {
                    count++;
                }
            }
            return count;
        }
    }

    public int AvailableOpponentCardCount
    {
        get
        {
            var placed = OpponentCardPlaced;
            var count = 0;
            for (var i = 0; i < 5; i++)
            {
                if (!placed[i])
                {
                    count++;
                }
            }
            return count;
        }
    }

    public int AvailableBlueCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < 5; i++)
            {
                if (BlueHand[i] != null && !BlueCardPlaced[i])
                {
                    count++;
                }
            }
            return count;
        }
    }

    public UniversalTriadState Clone()
    {
        return new UniversalTriadState
        {
            LocalPlayerColor = LocalPlayerColor,
            HiddenBlueCount = HiddenBlueCount,
            HiddenRedCount = HiddenRedCount,
            ForcedCardIdx = ForcedCardIdx,
            IsPlayerTurn = IsPlayerTurn,
            TurnState = TurnState,
            TurnBannerVisible = TurnBannerVisible,
            NumCardsPlaced = NumCardsPlaced,
            BlueHand = (TriadCard?[])BlueHand.Clone(),
            BlueCardPlaced = (bool[])BlueCardPlaced.Clone(),
            BlueCardRevealed = (bool[])BlueCardRevealed.Clone(),
            KnownBlueCards = [.. KnownBlueCards],
            RedHand = (TriadCard?[])RedHand.Clone(),
            RedCardPlaced = (bool[])RedCardPlaced.Clone(),
            RedCardRevealed = (bool[])RedCardRevealed.Clone(),
            KnownRedCards = [.. KnownRedCards],
            BoardCards = (TriadCard?[])BoardCards.Clone(),
            BoardOwners = (ETriadCardOwner[])BoardOwners.Clone(),
            Modifiers = [.. Modifiers],
            RuleNames = [.. RuleNames],
            InitialPlayerCards = [.. InitialPlayerCards]
        };
    }
}
