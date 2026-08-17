using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace RequiemAutoNPCPatcher3T;

public sealed record Donor(INpcGetter Npc, int Level, bool FromStack);

/// <summary>
/// The donor pool: every actor in the donor plugins, indexed by the race it actually resolves to.
///
/// The upstream patcher hardcoded ~60 tables of creature stats — and those literals turned out to be
/// Requiem's own exemplar NPCs copied by hand (GiantRace 1000/1400 is EncGiant02; HagravenRace 100/650
/// is EncHagraven). Reading the exemplar live instead is correct by construction and survives a
/// 3BFTweaks update, so this patcher hardcodes no creature numbers at all.
/// </summary>
public sealed class DonorIndex
{
    private readonly ActorView _view;
    private readonly Dictionary<FormKey, List<Donor>> _byRace = new();
    private readonly List<Donor> _casters = new();
    private readonly List<Donor> _ghosts = new();

    public int ActorCount { get; private set; }
    public int RaceCount => _byRace.Count;
    public int CasterCount => _casters.Count;
    public int GhostCount => _ghosts.Count;
    public int RejectedCount { get; private set; }

    public DonorIndex(
        IEnumerable<INpcGetter> winningNpcs,
        IReadOnlySet<ModKey> donorPlugins,
        ILinkCache cache,
        ActorView view,
        Classifier classifier)
    {
        _view = view;

        foreach (var npc in winningNpcs)
        {
            if (!IsDonorPlugin(npc.FormKey.ModKey, donorPlugins)) continue;

            // Requiem retires records by hollowing them out. spells.md §14.7: never use one as a comparable.
            if (StackData.IsTombstone(npc.EditorID)) continue;

            // Two whole families are off-pattern by construction and must never be a generic comparable
            // (the "don't copy an off-ladder value because it sits next door" rule):
            //
            //   Unique     — npcs.md §12.1 makes this the closest thing the stack has to a boss flag:
            //                a hand-set stat line far off any ladder. dunHunterSabreCat is level 32 with
            //                427 health where the ordinary EncSabreCatSnow is level 11 with 275, so
            //                letting it into the pool turns any high-level modded cat into a boss.
            //   Summonable — npcs.md §5.4: summon stats are per-record and arbitrary, with no curve at
            //                all. REQ_Actor_Illusion_Dremora is level 46 with 40 health (§14.2).
            var flags = npc.Configuration.Flags;
            if (flags.HasFlag(NpcConfiguration.Flag.Unique) || flags.HasFlag(NpcConfiguration.Flag.Summonable))
            {
                RejectedCount++;
                continue;
            }

            // npcs.md §14.1: FoxRace is the stack's invisible marker/spawner race — a 1683-health
            // "fox", and also the placeholder the CK leaves on any actor whose Traits are templated.
            // Never a stat comparable.
            if (view.RaceKey(npc) is not { } raceKey) continue;
            if (StackData.IsPlaceholderRace(raceKey)) continue;

            // A donor whose stat block resolves to nothing (a broken template chain, or an LVLN root)
            // has no numbers to give.
            var stats = view.Stats(npc);
            if (stats is null) continue;
            if (view.Level(npc) is not { } level || level <= 0) continue;

            var fromStack = npc.FormKey.ModKey == StackData.Requiem
                            || npc.FormKey.ModKey == StackData.FTweaks
                            || npc.FormKey.ModKey.FileName.String.StartsWith("3Tweaks", StringComparison.OrdinalIgnoreCase);

            var donor = new Donor(npc, level, fromStack);

            if (!_byRace.TryGetValue(raceKey, out var list))
                _byRace[raceKey] = list = new List<Donor>();
            list.Add(donor);
            ActorCount++;

            switch (classifier.Classify(npc, BanditArchetype.SwordShield).Kind)
            {
                case ActorKind.Caster: _casters.Add(donor); break;
                case ActorKind.Ghost: _ghosts.Add(donor); break;
            }
        }
    }

    private static bool IsDonorPlugin(ModKey key, IReadOnlySet<ModKey> donorPlugins) =>
        donorPlugins.Contains(key) ||
        key.FileName.String.StartsWith("cc", StringComparison.OrdinalIgnoreCase);

    public bool HasRace(FormKey race) => _byRace.ContainsKey(race);

    /// <summary>
    /// npcs.md §5.2 names three creature stat authorities, and this one selection rule is correct for
    /// all three. Pattern A (draugr, dremora, falmer, dwemer, dragons) is a numbered NPC_ ladder, so
    /// "nearest by level" picks the right rung. Pattern B (bears, trolls, spiders, most animals) puts
    /// one flat stat line on every actor of the species — every troll is level 14 / 280 / 340 — so any
    /// pick is the same pick. Pattern C carries its difficulty in RACE.ActorEffect trait spells, which
    /// the target already inherits from the race it shares with the donor.
    /// </summary>
    public Donor? ForRace(FormKey race, int targetLevel)
    {
        if (!_byRace.TryGetValue(race, out var list) || list.Count == 0) return null;
        return Nearest(list, targetLevel);
    }

    public Donor? ForCaster(int targetLevel) =>
        _casters.Count == 0 ? null : Nearest(_casters, targetLevel);

    /// <summary>Ghost and spirit are race-independent state traits (npcs.md §3.1), so a ghost's
    /// comparable is another ghost of any race rather than another Altmer.</summary>
    public Donor? ForGhost(int targetLevel) =>
        _ghosts.Count == 0 ? null : Nearest(_ghosts, targetLevel);

    private static Donor Nearest(List<Donor> list, int targetLevel) =>
        list.OrderBy(d => Math.Abs(d.Level - targetLevel))
            .ThenByDescending(d => d.FromStack) // a stack-authored comparable beats an untouched vanilla one
            .ThenBy(d => d.Npc.FormKey.ToString(), StringComparer.Ordinal) // deterministic across runs
            .First();
}
