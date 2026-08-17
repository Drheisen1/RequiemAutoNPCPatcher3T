using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace RequiemAutoNPCPatcher3T;

/// <summary>
/// Reads an actor the way the game does, not the way xEdit prints it.
///
/// npcs.md §7.2: a record with a block named in <c>Configuration.TemplateFlags</c> has no values of its
/// own for that block — the bytes sitting in it are dead and the template supplies the real values.
/// <c>EncDraugr01Template2H</c> displays 50/80 and plays 300/80. Every read here walks the chain to the
/// record that actually OWNS the block, and every write asks first whether the target owns it at all.
///
/// A template can also be a <c>LeveledNpc</c> rather than an <c>NPC_</c>, which is how a huge number of
/// mod and vanilla actors are built: the block is rolled from a list at spawn time. Such a block has no
/// single owning record — but it is NOT unreadable, and it is certainly not an error. For the one field
/// classification actually needs, the race, the list's entries answer it.
/// </summary>
public sealed class ActorView
{
    private const int MaxChainDepth = 16;

    private readonly ILinkCache _cache;
    private readonly Dictionary<FormKey, FormKey?> _leveledRaceCache = new();

    public ActorView(ILinkCache cache) => _cache = cache;

    /// <summary>Where a block's real values come from: an owning record, a leveled list, or neither.</summary>
    public readonly record struct BlockSource(INpcGetter? Owner, ILeveledNpcGetter? Leveled)
    {
        public bool IsLeveled => Leveled is not null;
    }

    public BlockSource Source(INpcGetter npc, NpcConfiguration.TemplateFlag block)
    {
        var current = npc;
        for (var depth = 0; depth < MaxChainDepth; depth++)
        {
            if (!current.Configuration.TemplateFlags.HasFlag(block)) return new(current, null);
            if (current.Template.IsNull) return new(current, null);

            switch (current.Template.TryResolve(_cache))
            {
                case INpcGetter next when next.FormKey != current.FormKey:
                    current = next;
                    continue;
                case ILeveledNpcGetter leveled:
                    return new(null, leveled);
                default:
                    return default; // unresolvable, or a self-referencing loop
            }
        }
        return default;
    }

    /// <summary>The record that owns <paramref name="block"/>, or null when a leveled list supplies it.</summary>
    public INpcGetter? Owner(INpcGetter npc, NpcConfiguration.TemplateFlag block) => Source(npc, block).Owner;

    /// <summary>
    /// True when the actor's own bytes for this block are dead — a write to it would be a no-op.
    ///
    /// Deliberately does NOT test <c>Configuration.Flags.UseTemplate</c>. The live winner of
    /// <c>EncDraugr01Template2H</c> `05B752:Skyrim.esm` carries <c>TemplateFlags = Stats, SpellList, …</c>
    /// and a <c>Template</c> pointer while its <c>Flags</c> are only <c>Respawn</c> — and it still plays
    /// its template's 300/80 rather than the 50/80 in its own bytes. Gating on that flag reads the record
    /// as self-owned and writes stats that never apply. The template subrecords are the authority; the
    /// ACBS bit is not.
    /// </summary>
    public bool IsInherited(INpcGetter npc, NpcConfiguration.TemplateFlag block) =>
        npc.Configuration.TemplateFlags.HasFlag(block) && !npc.Template.IsNull;

    // ------------------------------------------------------------------ block reads

    public IPlayerSkillsGetter? Stats(INpcGetter npc) =>
        Owner(npc, NpcConfiguration.TemplateFlag.Stats)?.PlayerSkills;

    /// <summary>The actor's effective level, or null when it is on the PC-level multiplier arm.</summary>
    public short? Level(INpcGetter npc)
    {
        var owner = Owner(npc, NpcConfiguration.TemplateFlag.Stats);
        return owner?.Configuration.Level is INpcLevelGetter fixedLevel ? fixedLevel.Level : null;
    }

    /// <summary>
    /// The level the actor was MEANT to sit at: its fixed level, or — when it is PC-scaled — the floor of
    /// its clamp band. npcs.md §15: a shipped PcLevelMult is the absence of a decision, so the band's
    /// floor is the only authored number in it.
    /// </summary>
    public int IntendedLevel(INpcGetter npc)
    {
        var owner = Owner(npc, NpcConfiguration.TemplateFlag.Stats) ?? npc;
        if (owner.Configuration.Level is INpcLevelGetter fixedLevel && fixedLevel.Level > 0)
            return fixedLevel.Level;

        var min = owner.Configuration.CalcMinLevel;
        var max = owner.Configuration.CalcMaxLevel;
        if (min > 0) return min;
        if (max > 0) return max;
        return 1;
    }

    public IReadOnlyList<IPerkPlacementGetter> Perks(INpcGetter npc) =>
        Owner(npc, NpcConfiguration.TemplateFlag.SpellList)?.Perks
        ?? (IReadOnlyList<IPerkPlacementGetter>)Array.Empty<IPerkPlacementGetter>();

    public IReadOnlyList<IFormLinkGetter<ISpellRecordGetter>> ActorEffects(INpcGetter npc) =>
        Owner(npc, NpcConfiguration.TemplateFlag.SpellList)?.ActorEffect
        ?? (IReadOnlyList<IFormLinkGetter<ISpellRecordGetter>>)Array.Empty<IFormLinkGetter<ISpellRecordGetter>>();

    public IReadOnlyList<IContainerEntryGetter> Items(INpcGetter npc) =>
        Owner(npc, NpcConfiguration.TemplateFlag.Inventory)?.Items
        ?? (IReadOnlyList<IContainerEntryGetter>)Array.Empty<IContainerEntryGetter>();

    public IFormLinkNullableGetter<IOutfitGetter>? Outfit(INpcGetter npc) =>
        Owner(npc, NpcConfiguration.TemplateFlag.Inventory)?.DefaultOutfit;

    public IClassGetter? Class(INpcGetter npc) =>
        Owner(npc, NpcConfiguration.TemplateFlag.Traits)?.Class.TryResolve(_cache);

    public IFormLinkNullableGetter<ICombatStyleGetter>? CombatStyle(INpcGetter npc) =>
        Owner(npc, NpcConfiguration.TemplateFlag.Traits)?.CombatStyle;

    // ------------------------------------------------------------------ race

    /// <summary>
    /// The race the actor actually spawns as.
    ///
    /// Three cases, and only the first is the obvious one:
    /// 1. the Traits block is owned — read its <c>Race</c>;
    /// 2. Traits is templated from a <c>LeveledNpc</c> — the record's own <c>Race</c> field is the CK's
    ///    placeholder (invariably <c>FoxRace</c>, npcs.md §14.1) and lying. Resolve the race from the
    ///    list's entries instead, which is what the game will roll;
    /// 3. nothing resolves — return null and let the caller report it as undecidable, not as an error.
    /// </summary>
    public FormKey? RaceKey(INpcGetter npc)
    {
        var source = Source(npc, NpcConfiguration.TemplateFlag.Traits);

        if (source.Owner is { } owner)
            return owner.Race.IsNull ? null : owner.Race.FormKey;

        if (source.Leveled is { } leveled)
            return RaceOfLeveledList(leveled, 0);

        return null;
    }

    public IRaceGetter? Race(INpcGetter npc) =>
        RaceKey(npc) is { } key && _cache.TryResolve<IRaceGetter>(key, out var race) ? race : null;

    /// <summary>True when the actor's race comes from a leveled list rather than a single record.</summary>
    public bool RaceIsLeveled(INpcGetter npc) => Source(npc, NpcConfiguration.TemplateFlag.Traits).IsLeveled;

    /// <summary>
    /// The race a leveled actor list rolls. Entries may themselves be lists, so this recurses; where a
    /// list mixes races (a "forest predator" list of wolves and bears) the most common one wins, which is
    /// the right bias for choosing a stat comparable.
    /// </summary>
    private FormKey? RaceOfLeveledList(ILeveledNpcGetter leveled, int depth)
    {
        if (depth > 6) return null;
        if (_leveledRaceCache.TryGetValue(leveled.FormKey, out var cached)) return cached;
        _leveledRaceCache[leveled.FormKey] = null; // guard against a cyclic list

        var tally = new Dictionary<FormKey, int>();
        foreach (var entry in leveled.Entries ?? Enumerable.Empty<ILeveledNpcEntryGetter>())
        {
            if (entry.Data is not { } data || data.Reference.IsNull) continue;
            switch (data.Reference.TryResolve(_cache))
            {
                case INpcGetter npc when RaceKey(npc) is { } key && !StackData.IsPlaceholderRace(key):
                    tally[key] = tally.GetValueOrDefault(key) + 1;
                    break;
                case ILeveledNpcGetter nested when RaceOfLeveledList(nested, depth + 1) is { } key:
                    tally[key] = tally.GetValueOrDefault(key) + 1;
                    break;
            }
        }

        var winner = tally.Count == 0
            ? (FormKey?)null
            : tally.OrderByDescending(p => p.Value)
                   .ThenBy(p => p.Key.ToString(), StringComparer.Ordinal)
                   .First().Key;

        _leveledRaceCache[leveled.FormKey] = winner;
        return winner;
    }
}
