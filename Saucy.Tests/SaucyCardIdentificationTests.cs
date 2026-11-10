using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using Saucy.TripleTriad.Data;
using Saucy.TripleTriad.GameLogic;
using Saucy.TripleTriad.TournamentDraft;
using Saucy.TripleTriad.UI;
using Saucy.TripleTriad.UniversalEngine;
using Xunit;
using Xunit.Abstractions;
using CoreCard = XIVcardsharp.Core.Data.TriadCard;
using CoreCardDB = XIVcardsharp.Core.Data.TriadCardDB;
using CoreCardOwner = XIVcardsharp.Core.Data.ETriadCardOwner;

namespace Saucy.Tests;

public class SaucyCardIdentificationTests
{
    private readonly ITestOutputHelper _output;

    public SaucyCardIdentificationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static void SeedWeaponCards(TriadCardDB cardDb)
    {
        while (cardDb.cards.Count <= 312)
        {
            cardDb.cards.Add(null);
        }

        // Ultima Weapon: Card 51, ★4 (Epic), None, Top: 7, Bottom: 9, Left: 1, Right: 8
        cardDb.cards[51] = new TriadCard(
            51,
            ETriadCardRarity.Epic,
            ETriadCardType.None,
            numUp: 7,
            numDown: 9,
            numLeft: 1,
            numRight: 8,
            sortOrder: 51,
            group: 0)
        {
            Name = "Ultima Weapon"
        };

        // Emerald Weapon: Card 312, ★4 (Epic), None, Top: 7, Bottom: 9, Left: 8, Right: 1
        cardDb.cards[312] = new TriadCard(
            312,
            ETriadCardRarity.Epic,
            ETriadCardType.None,
            numUp: 7,
            numDown: 9,
            numLeft: 8,
            numRight: 1,
            sortOrder: 312,
            group: 0)
        {
            Name = "Emerald Weapon"
        };
    }

    [Fact]
    public void Saucy_TriadCard_Sides_HaveCorrectCanonicalIndices()
    {
        var cardDb = TriadCardDB.Get();
        SeedWeaponCards(cardDb);

        var emerald = cardDb.FindById(312);
        var ultima = cardDb.FindById(51);

        Assert.NotNull(emerald);
        Assert.NotNull(ultima);

        // Sides array: [0]=Up, [1]=Left, [2]=Down, [3]=Right
        Assert.Equal(7, emerald.Sides[(int)ETriadGameSide.Up]);
        Assert.Equal(8, emerald.Sides[(int)ETriadGameSide.Left]);
        Assert.Equal(9, emerald.Sides[(int)ETriadGameSide.Down]);
        Assert.Equal(1, emerald.Sides[(int)ETriadGameSide.Right]);

        Assert.Equal(7, ultima.Sides[(int)ETriadGameSide.Up]);
        Assert.Equal(1, ultima.Sides[(int)ETriadGameSide.Left]);
        Assert.Equal(9, ultima.Sides[(int)ETriadGameSide.Down]);
        Assert.Equal(8, ultima.Sides[(int)ETriadGameSide.Right]);
    }

    [Fact]
    public void Saucy_TriadCardDB_Find_CorrectlyDistinguishes_Emerald_From_Ultima()
    {
        var cardDb = TriadCardDB.Get();
        SeedWeaponCards(cardDb);

        // Emerald Weapon: U=7, L=8, D=9, R=1
        var matchedEmerald = cardDb.Find(7, 8, 9, 1);
        Assert.NotNull(matchedEmerald);
        Assert.Equal(312, matchedEmerald.Id);
        Assert.Equal("Emerald Weapon", matchedEmerald.Name);

        // Ultima Weapon: U=7, L=1, D=9, R=8
        var matchedUltima = cardDb.Find(7, 1, 9, 8);
        Assert.NotNull(matchedUltima);
        Assert.Equal(51, matchedUltima.Id);
        Assert.Equal("Ultima Weapon", matchedUltima.Name);
    }

    [Fact]
    public void Saucy_TriadCardDB_FindByTexture_SupportsBackslashes_And_ShortFileNames()
    {
        var cardDb = TriadCardDB.Get();
        SeedWeaponCards(cardDb);

        // Standard slash path
        var texEmeraldSlash = cardDb.FindByTexture("ui/icon/087000/087312.tex");
        Assert.NotNull(texEmeraldSlash);
        Assert.Equal(312, texEmeraldSlash.Id);

        // Windows backslash path
        var texEmeraldBackslash = cardDb.FindByTexture(@"ui\icon\087000\087312.tex");
        Assert.NotNull(texEmeraldBackslash);
        Assert.Equal(312, texEmeraldBackslash.Id);

        // Direct short file name
        var texEmeraldShort = cardDb.FindByTexture("087312.tex");
        Assert.NotNull(texEmeraldShort);
        Assert.Equal(312, texEmeraldShort.Id);

        // 088000 small icon format
        var texUltimaSmall = cardDb.FindByTexture("ui/icon/088000/088051.tex");
        Assert.NotNull(texUltimaSmall);
        Assert.Equal(51, texUltimaSmall.Id);
    }

    [Fact]
    public void Saucy_TriadCardDB_FindById_HandlesOutOfBoundsAndSparseLists()
    {
        var cardDb = TriadCardDB.Get();
        cardDb.cards.Clear();

        // Sparse list with only card 312 added
        var emerald = new TriadCard(312, ETriadCardRarity.Epic, ETriadCardType.None, 7, 9, 8, 1, 312, 0)
        {
            Name = "Emerald Weapon"
        };
        cardDb.cards.Add(emerald);

        // cards.Count is 1, but emerald.Id is 312
        var found = cardDb.FindById(312);
        Assert.NotNull(found);
        Assert.Equal(312, found.Id);

        // Negative id returns null safely
        Assert.Null(cardDb.FindById(-1));
        // Nonexistent id returns null safely
        Assert.Null(cardDb.FindById(9999));
    }

    [Fact]
    public unsafe void UIReaderTriadGame_ReadsEmeraldWeapon_WithCorrectLeftRight()
    {
        var cardDb = TriadCardDB.Get();
        SeedWeaponCards(cardDb);

        // Allocate memory for AddonTripleTriad.TripleTriadCard struct (size >= 0xC0 bytes)
        var size = Math.Max(Marshal.SizeOf<AddonTripleTriad.TripleTriadCard>(), 0xC0);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            new Span<byte>((void*)ptr, size).Clear();

            // Native TripleTriadCard memory offsets:
            // 0x80: CardRarity = 3 (Epic, 0-indexed)
            // 0x81: CardType = 0 (None)
            // 0x82: CardOwner = 1 (Blue)
            // 0x83: NumSideU = 7
            // 0x84: NumSideD = 9
            // 0x85: NumSideR (native Left) = 8
            // 0x86: NumSideL (native Right) = 1
            // 0xA4: HasCard = 1
            var pByte = (byte*)ptr;
            pByte[0x80] = 3;
            pByte[0x81] = 0;
            pByte[0x82] = 1;
            pByte[0x83] = 7;
            pByte[0x84] = 9;
            pByte[0x85] = 8; // 0x85 is named NumSideR in ClientStructs, but represents game Left!
            pByte[0x86] = 1; // 0x86 is named NumSideL in ClientStructs, but represents game Right!
            pByte[0xA4] = 1;

            var reader = new UIReaderTriadGame();
            var getCardDataMethod = typeof(UIReaderTriadGame).GetMethod("GetCardData", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(getCardDataMethod);

            var addonCard = Marshal.PtrToStructure<AddonTripleTriad.TripleTriadCard>(ptr);
            var cardState = (UIStateTriadCard)getCardDataMethod.Invoke(reader, [addonCard])!;

            Assert.NotNull(cardState);
            Assert.True(cardState.isPresent);
            Assert.Equal(7, cardState.numU);
            Assert.Equal(8, cardState.numL); // Left MUST be 8
            Assert.Equal(9, cardState.numD);
            Assert.Equal(1, cardState.numR); // Right MUST be 1

            // Resolve to TriadCard via GameUIParser
            var parser = new GameUIParser();
            var resolvedCard = cardState.ToTriadCard(parser);
            Assert.NotNull(resolvedCard);
            Assert.Equal(312, resolvedCard.Id);
            Assert.Equal("Emerald Weapon", resolvedCard.Name);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public unsafe void TriadCardList_AddonGSInfoCardList_OffsetsAndStats_ResolveCorrectly()
    {
        var cardDb = TriadCardDB.Get();
        SeedWeaponCards(cardDb);

        // Verify FieldOffsets in AddonGSInfoCardList
        var gsCardListType = typeof(AddonGSInfoCardList);
        var gsFields = gsCardListType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var gsOffsetMap = new Dictionary<string, int>();
        foreach (var field in gsFields)
        {
            var attr = (FieldOffsetAttribute?)Attribute.GetCustomAttribute(field, typeof(FieldOffsetAttribute));
            if (attr != null)
            {
                gsOffsetMap[field.Name] = attr.Value;
            }
        }

        Assert.Equal(0x2A3, gsOffsetMap["NumSideU"]);
        Assert.Equal(0x2A4, gsOffsetMap["NumSideD"]);
        Assert.Equal(0x2A5, gsOffsetMap["NumSideR"]); // Native Left
        Assert.Equal(0x2A6, gsOffsetMap["NumSideL"]); // Native Right

        // Simulate AddonGSInfoCardList memory
        var size = Math.Max(Marshal.SizeOf<AddonGSInfoCardList>(), 0x300);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            new Span<byte>((void*)ptr, size).Clear();
            var pByte = (byte*)ptr;
            pByte[0x2A0] = 3; // CardRarity (Epic)
            pByte[0x2A1] = 0; // CardType (None)
            pByte[0x2A3] = 7; // U
            pByte[0x2A4] = 9; // D
            pByte[0x2A5] = 8; // Native Left (NumSideR)
            pByte[0x2A6] = 1; // Native Right (NumSideL)

            var selectionReaderType = typeof(UIReaderTriadCardList).Assembly.GetType("Saucy.TripleTriad.UI.TriadCardListSelectionReader");
            Assert.NotNull(selectionReaderType);

            var method = selectionReaderType.GetMethod(
                "TryReadCardIdFromStats",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var resolvedId = (int)method.Invoke(null, [Pointer.Box((void*)ptr, typeof(AddonGSInfoCardList*))])!;
            Assert.Equal(312, resolvedId); // Must resolve Emerald Weapon, not Ultima!

            // Test AddonStatsMatchCard in UIReaderTriadCardList
            var matchesMethod = typeof(UIReaderTriadCardList).GetMethod(
                "AddonStatsMatchCard",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(matchesMethod);

            var matchesEmerald = (bool)matchesMethod.Invoke(null, [Pointer.Box((void*)ptr, typeof(AddonGSInfoCardList*)), 312])!;
            Assert.True(matchesEmerald, "AddonStatsMatchCard must return true for Emerald Weapon (312)");

            var matchesUltima = (bool)matchesMethod.Invoke(null, [Pointer.Box((void*)ptr, typeof(AddonGSInfoCardList*)), 51])!;
            Assert.False(matchesUltima, "AddonStatsMatchCard must return false for Ultima Weapon (51)");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public void SyncCardDatabase_PopulatesCoreWithCanonicalSides()
    {
        var saucyDb = TriadCardDB.Get();
        SeedWeaponCards(saucyDb);

        TriadGameAgentCardsharp.SyncCardDatabase();

        var coreDb = CoreCardDB.Get();
        var coreEmerald = coreDb.FindById(312);
        var coreUltima = coreDb.FindById(51);

        Assert.NotNull(coreEmerald);
        Assert.NotNull(coreUltima);

        // Verify Core card canonical sides: [0]=Up, [1]=Left, [2]=Down, [3]=Right
        Assert.Equal(7, coreEmerald.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Up]);
        Assert.Equal(8, coreEmerald.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Left]);
        Assert.Equal(9, coreEmerald.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Down]);
        Assert.Equal(1, coreEmerald.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Right]);

        Assert.Equal(7, coreUltima.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Up]);
        Assert.Equal(1, coreUltima.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Left]);
        Assert.Equal(9, coreUltima.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Down]);
        Assert.Equal(8, coreUltima.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Right]);

        // Core find by sides
        var matchedCoreEmerald = coreDb.Find(7, 8, 9, 1);
        Assert.NotNull(matchedCoreEmerald);
        Assert.Equal(312, matchedCoreEmerald.Id);
    }

    [Fact]
    public void UniversalStateReader_ResolveCard_ResolvesEmeraldWeaponCorrectly()
    {
        var saucyDb = TriadCardDB.Get();
        SeedWeaponCards(saucyDb);
        TriadGameAgentCardsharp.SyncCardDatabase();

        var parser = new GameUIParser();
        var cardState = new UIStateTriadCard
        {
            isPresent = true,
            numU = 7,
            numL = 8,
            numD = 9,
            numR = 1,
            rarity = 3,
            type = 0,
            texturePath = "ui/icon/087000/087312.tex"
        };

        var resolveMethod = typeof(UniversalStateReader).GetMethod(
            "ResolveCard",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(resolveMethod);

        var coreCard = (CoreCard?)resolveMethod.Invoke(null, [cardState, parser]);
        Assert.NotNull(coreCard);
        Assert.Equal(312, coreCard.Id);
        Assert.Equal(8, coreCard.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Left]);
        Assert.Equal(1, coreCard.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Right]);
    }

    [Fact]
    public void Verify_Lumina_CardResident_Matches_CanonicalSides()
    {
        var sqpackPath = @"C:\Games\Steam\steamapps\common\FINAL FANTASY XIV Online\game\sqpack";
        if (!System.IO.Directory.Exists(sqpackPath)) return;

        var lumina = new Lumina.GameData(sqpackPath);
        var cardResidentSheet = lumina.GetExcelSheet<Lumina.Excel.Sheets.TripleTriadCardResident>();
        var cardSheet = lumina.GetExcelSheet<Lumina.Excel.Sheets.TripleTriadCard>();
        if (cardResidentSheet == null || cardSheet == null) return;

        var emeraldResident = cardResidentSheet.GetRowOrDefault(312);
        var emeraldName = cardSheet.GetRowOrDefault(312);
        var ultimaResident = cardResidentSheet.GetRowOrDefault(51);
        var ultimaName = cardSheet.GetRowOrDefault(51);

        Assert.True(emeraldResident.HasValue);
        Assert.True(emeraldName.HasValue);
        Assert.True(ultimaResident.HasValue);
        Assert.True(ultimaName.HasValue);

        Assert.Equal("Emerald Weapon", emeraldName.Value.Name.ToString());
        Assert.Equal(7, emeraldResident.Value.Top);
        Assert.Equal(9, emeraldResident.Value.Bottom);
        Assert.Equal(8, emeraldResident.Value.Left);
        Assert.Equal(1, emeraldResident.Value.Right);

        Assert.Equal("Ultima Weapon", ultimaName.Value.Name.ToString());
        Assert.Equal(7, ultimaResident.Value.Top);
        Assert.Equal(9, ultimaResident.Value.Bottom);
        Assert.Equal(1, ultimaResident.Value.Left);
        Assert.Equal(8, ultimaResident.Value.Right);

        // Verify constructor call matches Lumina order (Left as arg 6, Right as arg 7)
        var emeraldCard = new TriadCard(
            312,
            ETriadCardRarity.Epic,
            ETriadCardType.None,
            emeraldResident.Value.Top,
            emeraldResident.Value.Bottom,
            emeraldResident.Value.Left,
            emeraldResident.Value.Right,
            emeraldResident.Value.Order,
            emeraldResident.Value.UIPriority);

        Assert.Equal(8, emeraldCard.Sides[(int)ETriadGameSide.Left]);
        Assert.Equal(1, emeraldCard.Sides[(int)ETriadGameSide.Right]);
    }

    [Fact]
    public void ToTriadCard_WhenTypeRarityMismatchesButStatMatches_DoesNotSetParseError()
    {
        var cardDb = TriadCardDB.Get();
        SeedWeaponCards(cardDb);

        var parser = new GameUIParser();
        // Emerald Weapon stats [7-8-9-1], but type is Garlean and rarity is Common (mismatching DB None/Epic)
        var cardState = new UIStateTriadCard
        {
            isPresent = true,
            numU = 7,
            numL = 8,
            numD = 9,
            numR = 1,
            rarity = 0, // Common
            type = 4,   // Garlean
            texturePath = null
        };

        var resolved = cardState.ToTriadCard(parser, markFailed: true);

        Assert.NotNull(resolved);
        Assert.Equal(312, resolved.Id);
        Assert.Equal("Emerald Weapon", resolved.Name);
        // CRITICAL: parser must NOT have errors flagged, as the card was successfully resolved by stat fallback
        Assert.False(parser.hasFailedCard, "Parser must not flag hasFailedCard when fallback successfully matched card");
        Assert.False(parser.HasErrors, "Parser must not have errors when fallback successfully matched card");
    }

    [Fact]
    public void SyncCardDatabase_WhenCardsAddedOrReloaded_ResyncsCoreDatabaseCorrectly()
    {
        var saucyDb = TriadCardDB.Get();
        saucyDb.cards.Clear();
        TriadGameAgentCardsharp.ResetCardDatabaseSync();

        // 1. Initial seed with 1 card
        var card1 = new TriadCard(1, ETriadCardRarity.Common, ETriadCardType.None, 1, 1, 1, 1, 1, 0)
        {
            Name = "Card One"
        };
        saucyDb.cards.Add(card1);

        TriadGameAgentCardsharp.SyncCardDatabase();
        var coreDb = CoreCardDB.Get();
        Assert.Single(coreDb.cards);
        Assert.NotNull(coreDb.FindById(1));

        // 2. Add Emerald Weapon (312) to Saucy DB
        SeedWeaponCards(saucyDb);
        Assert.True(saucyDb.cards.Count >= 313);

        // Call SyncCardDatabase without manually calling reset - must automatically detect count changed!
        TriadGameAgentCardsharp.SyncCardDatabase();

        Assert.True(coreDb.cards.Count >= 313);
        var coreEmerald = coreDb.FindById(312);
        Assert.NotNull(coreEmerald);
        Assert.Equal(312, coreEmerald.Id);
        Assert.Equal("Emerald Weapon", coreEmerald.Name);
        Assert.Equal(8, coreEmerald.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Left]);
        Assert.Equal(1, coreEmerald.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Right]);
    }

    [Fact]
    public void TriadCardDB_FindByTexture_HandlesWhitespace_UppercaseExtension_And_ShortLegacy()
    {
        var cardDb = TriadCardDB.Get();
        SeedWeaponCards(cardDb);

        // Leading / trailing whitespace
        var texWhitespace = cardDb.FindByTexture("   ui/icon/087000/087312.tex   ");
        Assert.NotNull(texWhitespace);
        Assert.Equal(312, texWhitespace.Id);

        // Uppercase extension
        var texUpper = cardDb.FindByTexture("087312.TEX");
        Assert.NotNull(texUpper);
        Assert.Equal(312, texUpper.Id);

        // Dawntrail _hr1 texture format
        var texHr1 = cardDb.FindByTexture("ui/icon/087000/087312_hr1.tex");
        Assert.NotNull(texHr1);
        Assert.Equal(312, texHr1.Id);

        // Direct legacy short format 082xxx: card 312 in legacy format is 082412 (412 - 100 = 312)
        var texLegacyShort = cardDb.FindByTexture("082412.tex");
        Assert.NotNull(texLegacyShort);
        Assert.Equal(312, texLegacyShort.Id);
    }

    [Fact]
    public void ToTriadCard_WhenDuplicateStatsExist_DisambiguatesByTextureOrTypeRarity()
    {
        var cardDb = TriadCardDB.Get();
        cardDb.cards.Clear();

        // Sabotender: Id 42, Common, Beastman, [2-3-3-2]
        var sabotender = new TriadCard(42, ETriadCardRarity.Common, ETriadCardType.Beastman, 2, 3, 3, 2, 42, 0)
        {
            Name = "Sabotender"
        };
        // Asura: Id 324, Common, None, [2-3-3-2]
        var asura = new TriadCard(324, ETriadCardRarity.Common, ETriadCardType.None, 2, 3, 3, 2, 324, 0)
        {
            Name = "Asura"
        };

        while (cardDb.cards.Count < 325) cardDb.cards.Add(null);
        cardDb.cards[42] = sabotender;
        cardDb.cards[324] = asura;
        cardDb.ProcessSameSideLists();

        Assert.True(sabotender.SameNumberId >= 0);
        Assert.True(asura.SameNumberId >= 0);
        Assert.Equal(sabotender.SameNumberId, asura.SameNumberId);

        var parser = new GameUIParser();

        // 1. Disambiguate by Texture
        var stateAsuraByTex = new UIStateTriadCard
        {
            isPresent = true,
            numU = 2,
            numL = 3,
            numD = 3,
            numR = 2,
            texturePath = "087324.tex"
        };
        var matchedAsura = stateAsuraByTex.ToTriadCard(parser);
        Assert.NotNull(matchedAsura);
        Assert.Equal(324, matchedAsura.Id);
        Assert.Equal("Asura", matchedAsura.Name);

        // 2. Disambiguate by Type (Beastman -> Sabotender) when texture is null
        var stateSabotenderByType = new UIStateTriadCard
        {
            isPresent = true,
            numU = 2,
            numL = 3,
            numD = 3,
            numR = 2,
            type = (byte)ETriadCardType.Beastman,
            rarity = (byte)ETriadCardRarity.Common,
            texturePath = null
        };
        var matchedSabotender = stateSabotenderByType.ToTriadCard(parser);
        Assert.NotNull(matchedSabotender);
        Assert.Equal(42, matchedSabotender.Id);
        Assert.Equal("Sabotender", matchedSabotender.Name);

        // 3. Fallback when texture is null and type is unknown: must never return null
        var stateFallback = new UIStateTriadCard
        {
            isPresent = true,
            numU = 2,
            numL = 3,
            numD = 3,
            numR = 2,
            type = 255,
            rarity = 255,
            texturePath = null
        };
        var matchedFallback = stateFallback.ToTriadCard(parser);
        Assert.NotNull(matchedFallback);
        Assert.True(matchedFallback.Id == 42 || matchedFallback.Id == 324);
    }

    [Fact]
    public void TriadGameAgentCardsharp_ConvertToUniversalState_ResolvesCardsViaFindById_WhenDBSparse()
    {
        var saucyDb = TriadCardDB.Get();
        saucyDb.cards.Clear();
        SeedWeaponCards(saucyDb);
        TriadGameAgentCardsharp.SyncCardDatabase(force: true);

        var agent = new TriadGameAgentCardsharp();
        var solver = new TriadGameSolver(agent);

        var simState = new TriadGameSimulationState();
        var deckBlue = new TriadDeck([saucyDb.FindById(312)!, saucyDb.FindById(51)!, saucyDb.FindById(312)!, null, null]);
        simState.deckBlue = new TriadDeckInstanceManual(deckBlue);
        var deckRed = new TriadDeck([saucyDb.FindById(51)!, null, null, null, null]);
        simState.deckRed = new TriadDeckInstanceManual(deckRed);

        // Put Emerald Weapon on the board at position 4
        var cardInst = new TriadCardInstance(saucyDb.FindById(312)!, ETriadCardOwner.Blue);
        simState.board[4] = cardInst;

        var convertMethod = typeof(TriadGameAgentCardsharp).GetMethod(
            "ConvertToUniversalState",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(convertMethod);

        var uniState = (XIVcardsharp.Core.State.UniversalTriadState?)convertMethod.Invoke(null, [solver, simState]);
        Assert.NotNull(uniState);

        // Blue Hand slot 0 should be Emerald Weapon
        Assert.NotNull(uniState.BlueHand[0]);
        Assert.Equal(312, uniState.BlueHand[0]!.Id);
        Assert.Equal(8, uniState.BlueHand[0]!.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Left]);
        Assert.Equal(1, uniState.BlueHand[0]!.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Right]);

        // Board slot 4 should be Emerald Weapon
        Assert.NotNull(uniState.BoardCards[4]);
        Assert.Equal(312, uniState.BoardCards[4]!.Id);
        Assert.Equal(CoreCardOwner.Blue, uniState.BoardOwners[4]);
        Assert.Equal(8, uniState.BoardCards[4]!.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Left]);
        Assert.Equal(1, uniState.BoardCards[4]!.Sides[(int)XIVcardsharp.Core.Data.ETriadGameSide.Right]);
    }
}
