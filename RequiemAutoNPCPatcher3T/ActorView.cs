using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace RequiemAutoNPCPatcher3T;

/// <summary>
/// Reads an actor the way the game does, not the way xEdit prints it.
///
/// npcs.md §7.2: a record with a block named in <c>Configuration.TemplateFlags</c> has no values of its
/// own for that block — the bytes sitting in it are dead and the template supplies the real values.
/// <c>EncGiant02</c> carries <c>HealthOffset = 1000</c> that never applies; <c>EncDraugr01Template2H</c>
/// displays 50/80 and plays 300/80. Every read here walks the chain to the record that actually OWNS the
/// block, and every write asks first whether the target owns it at all.
/// </summary>
public sealed class ActorView
{
    private const int MaxChainDepth = 16;

    private readonly ILinkCache _cache;

    public ActorView(ILinkCache cache) => _cache = cache;

    /// <summary>The record that owns <paramref name="block"/> for this actor, or null if the chain
    /// is broken (a template that does not resolve, an LVLN root, or a loop).</summary>
    public INpcGetter? Owner(INpcGetter npc, NpcConfiguration.TemplateFlag block)
    {
        var current = npc;
        for (var depth = 0; depth < MaxChainDepth; depth++)
        {
            if (!current.Configuration.TemplateFlags.HasFlag(block)) return current;
            if (current.Template.IsNull) return current;

            // An LVLN root has no single owner: which actor supplies the block is a run-time roll.
            if (current.Template.TryResolve(_cache) is not INpcGetter next) return null;
            if (next.FormKey == current.FormKey) return null;
            current = next;
        }
        return null;
    }

    /// <summary>
    /// True when the actor's own bytes for this block are dead — a write to it would be a no-op.
    ///
    /// Deliberately does NOT test <c>Configuration.Flags.UseTemplate</c>. The live winner of
    /// <c>EncDraugr01Template2H</c> `05B752:Skyrim.esm` carries
    /// <c>TemplateFlags = Stats, SpellList, ...</c> and <c>Template = EncDraugr01Template</c> while its
    /// <c>Flags</c> are only <c>Respawn</c> — and it still plays its template's 300/80 rather than the
    /// 50/80 in its own bytes. Gating on that flag reads the record as self-owned and writes stats that
    /// never apply. The template subrecords are the authority; the ACBS bit is not.
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

    // Traits carries Race, Class, CombatStyle, Voice, Height, Weight, Skin ...
    public IFormLinkGetter<IRaceGetter>? RaceLink(INpcGetter npc) =>
        Owner(npc, NpcConfiguration.TemplateFlag.Traits)?.Race;

    public IRaceGetter? Race(INpcGetter npc) => RaceLink(npc)?.TryResolve(_cache);

    public IClassGetter? Class(INpcGetter npc) =>
        Owner(npc, NpcConfiguration.TemplateFlag.Traits)?.Class.TryResolve(_cache);

    public IFormLinkNullableGetter<ICombatStyleGetter>? CombatStyle(INpcGetter npc) =>
        Owner(npc, NpcConfiguration.TemplateFlag.Traits)?.CombatStyle;
}
