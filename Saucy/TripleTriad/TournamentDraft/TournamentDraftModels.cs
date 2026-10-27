using System.Collections.Generic;
using System.Linq;

namespace Saucy.TripleTriad.TournamentDraft;

public record TournamentDraftCard(
    string Name,
    int Up,
    int Left,
    int Down,
    int Right,
    int Rarity,
    string Tribe = "")
{
    public int[] Sides => [Up, Left, Down, Right];
    public int CountVal(int v) => Sides.Count(s => s == v);
}

public class TournamentDraftOption
{
    public int SetIndex { get; set; }
    public int OptionIndex { get; set; }
    public uint NodeId { get; set; }
    public List<TournamentDraftCard> Cards { get; set; } = [];
}

public class TournamentDraftState
{
    public List<string> Rules { get; set; } = [];
    public List<TournamentDraftOption> Set1Options { get; set; } = [];
    public List<TournamentDraftOption> Set2Options { get; set; } = [];
    public List<TournamentDraftOption> Set3Options { get; set; } = [];

    public bool IsValid =>
        Set1Options.Count == 3 &&
        Set2Options.Count == 3 &&
        Set3Options.Count == 3;
}

public class TournamentDraftResult
{
    public int Set1Option { get; set; }
    public int Set2Option { get; set; }
    public int Set3Option { get; set; }
    public float Score { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<TournamentDraftCard> SelectedCards { get; set; } = [];
    public List<TournamentDraftCard> Set1Cards { get; set; } = [];
    public List<TournamentDraftCard> Set2Cards { get; set; } = [];
    public List<TournamentDraftCard> Set3Cards { get; set; } = [];
}
