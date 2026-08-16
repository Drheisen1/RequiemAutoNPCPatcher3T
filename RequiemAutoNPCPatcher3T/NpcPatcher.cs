using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace RequiemAutoNPCPatcher3T;

/// <summary>
/// Writes the balance onto one actor.
///
/// Two rules shape every method here.
///
/// 1. This patcher runs BEFORE the Reqtificator, which is regenerated afterwards. Nothing the
///    <c>ActorAssignmentRules_*.conf</c> files assign is ever written — only what Requiem and 3BFTweaks
///    AUTHORED on the donor record (npcs.md §4.1, §13.2 item 23).
/// 2. A block named in the target's <c>TemplateFlags</c> is not writable; the bytes are dead
///    (npcs.md §7.2). Those writes are skipped and reported, never issued and hoped for.
///
/// <c>Factions[]</c> is read as evidence and never written, on any actor, for any reason.
/// <c>DefaultOutfit</c> and <c>Items</c> are never written either: the mod keeps its visual identity and
/// the actor's effective armour comes from the donor's <c>REQ_Trait_Tempering_*</c> ability.
/// </summary>
public sealed class NpcPatcher
{
    private readonly ILinkCache _cache;
    private readonly ActorView _view;
    private readonly Settings _settings;
    private readonly PatchLog _log;
    private readonly IReadOnlySet<ModKey> _donorPlugins;

    public NpcPatcher(ILinkCache cache, ActorView view, Settings settings, PatchLog log, IReadOnlySet<ModKey> donorPlugins)
    {
        _cache = cache;
        _view = view;
        _settings = settings;
        _log = log;
        _donorPlugins = donorPlugins;
    }

    // ------------------------------------------------------------------ humanoid

    public bool ApplyBanditRank(INpcGetter source, Npc target, BanditArchetype archetype, int rank, Classification cls)
    {
        var templateKey = StackData.BanditTemplate(archetype, rank);
        if (!_cache.TryResolve<INpcGetter>(templateKey, out var template))
        {
            _log.Error($"bandit template {archetype} rank {rank} ({templateKey}) is missing from the load order — is FTweaks.esp active?");
            return false;
        }

        var level = StackData.RankLevels[rank];
        var wrote = CopyStats(source, target, template, level, cls);
        wrote |= CopyLoadout(source, target, template);
        wrote |= CopyTraits(source, target, template);

        if (!wrote) return false; // every block was templated: there is nothing on this record to write

        _log.Actor(source, $"{archetype} rank {rank:00} (level {level}) <- {template.EditorID} [{cls.ArchetypeReason}]");
        if (cls.ArchetypeIsGuess)
            _log.Warn($"{Name(source)}: archetype guessed as {archetype} — {cls.ArchetypeReason}");
        return true;
    }

    // ------------------------------------------------------------------ creature / caster

    public bool ApplyDonor(INpcGetter source, Npc target, Donor donor, Classification cls, string why)
    {
        var wrote = CopyStats(source, target, donor.Npc, donor.Level, cls);
        wrote |= CopyLoadout(source, target, donor.Npc);
        wrote |= CopyTraits(source, target, donor.Npc);

        if (!wrote) return false;

        _log.Actor(source, $"{cls.Kind} level {donor.Level} <- {Name(donor.Npc)} [{why}]");
        return true;
    }

    // ------------------------------------------------------------------ blocks

    /// <summary>Level, the three pools, the skill line, and the offsets.</summary>
    private bool CopyStats(INpcGetter source, Npc target, INpcGetter donor, int level, Classification cls)
    {
        if (_view.IsInherited(source, NpcConfiguration.TemplateFlag.Stats))
        {
            _log.Skip($"{Name(source)}: Stats are inherited from {Name(source.Template)} — no stat bytes of its own to write (npcs.md §7.2).");
            return false;
        }

        var donorStats = _view.Stats(donor);
        if (donorStats is null)
        {
            _log.Error($"{Name(source)}: donor {Name(donor)} has no resolvable stat block.");
            return false;
        }

        // --- level
        var keepScaling = _settings.KeepScalingOnNonCombatants && !cls.IsCombatant;
        var isScaled = source.Configuration.Level is IPcLevelMultGetter;

        if (isScaled && (!_settings.ConvertPcLevelMult || keepScaling))
        {
            _log.Note($"{Name(source)}: left on PcLevelMult ({(keepScaling ? "non-combatant" : "conversion disabled")}).");
        }
        else
        {
            // The arm IS the object: swapping in an NpcLevel is what converts a PC-scaled actor to a
            // fixed one. There is no separate flag to clear.
            target.Configuration.Level = new NpcLevel { Level = (short)level };
            target.Configuration.CalcMinLevel = (short)level;
            target.Configuration.CalcMaxLevel = (short)level;
        }

        // --- pools and skills
        target.PlayerSkills ??= new PlayerSkills();
        target.PlayerSkills.Health = donorStats.Health;
        target.PlayerSkills.Magicka = donorStats.Magicka;
        target.PlayerSkills.Stamina = donorStats.Stamina;

        target.PlayerSkills.SkillValues.Clear();
        foreach (var (skill, value) in donorStats.SkillValues)
            target.PlayerSkills.SkillValues[skill] = value;

        // A skill OFFSET on top of a copied skill line double-counts the same competence.
        target.PlayerSkills.SkillOffsets.Clear();
        foreach (var skill in donorStats.SkillValues.Keys)
            target.PlayerSkills.SkillOffsets[skill] = 0;

        // npcs.md §13.2 item 9 — 3Tweaks never uses the three stat offsets on a new actor, and all 54
        // bandit templates sit at 0. Leaving a mod's offsets in place double-counts the pools.
        target.Configuration.HealthOffset = 0;
        target.Configuration.MagickaOffset = 0;
        target.Configuration.StaminaOffset = 0;

        return true;
    }

    /// <summary>Perks and abilities — the half of an actor a patcher usually skips.</summary>
    private bool CopyLoadout(INpcGetter source, Npc target, INpcGetter donor)
    {
        if (_view.IsInherited(source, NpcConfiguration.TemplateFlag.SpellList))
        {
            _log.Skip($"{Name(source)}: Perks and ActorEffect are inherited from {Name(source.Template)} — not writable here (npcs.md §7.2).");
            return false;
        }

        // --- perks: the donor's whole authored loadout, plus any perk the MOD itself defines.
        //
        // The bandit templates carry 4 perks at rank 01 and up to 21 at rank 06 — a real archetype's
        // worth, not three plausible picks. Replacing the list wholesale is what brings the actor onto
        // the ladder; keeping the mod's own perks is what keeps its identity.
        var perks = new List<PerkPlacement>();
        var seen = new HashSet<FormKey>();

        foreach (var placement in _view.Perks(donor))
        {
            if (!Admissible(placement.Perk)) continue;
            if (!seen.Add(placement.Perk.FormKey)) continue;
            perks.Add(new PerkPlacement { Perk = new FormLink<IPerkGetter>(placement.Perk.FormKey), Rank = placement.Rank });
        }

        foreach (var placement in _view.Perks(source))
        {
            if (IsStackOwned(placement.Perk.FormKey.ModKey)) continue; // the donor's list is the authority
            if (!Admissible(placement.Perk)) continue;
            if (!seen.Add(placement.Perk.FormKey)) continue;
            perks.Add(new PerkPlacement { Perk = new FormLink<IPerkGetter>(placement.Perk.FormKey), Rank = placement.Rank });
        }

        target.Perks = perks.Count == 0 ? null : new(perks);

        // --- abilities: strip the tombstones and the stale tempering rank, keep everything the mod
        // brought, then add the donor's traits. On a bandit template that is exactly one ability —
        // REQ_Trait_Tempering_Bandit_{Heavy,Light}_RankN. An armoured actor without one is wearing
        // untempered gear.
        var effects = new List<IFormLinkGetter<ISpellRecordGetter>>();
        var seenSpells = new HashSet<FormKey>();

        foreach (var link in _view.ActorEffects(source))
        {
            if (!Admissible(link)) continue;
            if (Resolve(link) is { } spell && StackData.IsTemperingTrait(spell.EditorID)) continue;
            if (!seenSpells.Add(link.FormKey)) continue;
            effects.Add(link);
        }

        foreach (var link in _view.ActorEffects(donor))
        {
            if (!Admissible(link)) continue;
            if (!seenSpells.Add(link.FormKey)) continue;
            effects.Add(link);
        }

        target.ActorEffect = effects.Count == 0 ? null : new(effects);
        return true;
    }

    /// <summary>Class and combat style. A null combat style on a non-templated actor is a live defect —
    /// the actor stands still in the fight it was built for.</summary>
    private bool CopyTraits(INpcGetter source, Npc target, INpcGetter donor)
    {
        if (_view.IsInherited(source, NpcConfiguration.TemplateFlag.Traits))
        {
            _log.Skip($"{Name(source)}: Class and CombatStyle are inherited from {Name(source.Template)} — not writable here (npcs.md §7.2).");
            return false;
        }

        var donorTraits = _view.Owner(donor, NpcConfiguration.TemplateFlag.Traits);
        if (donorTraits is null) return false;

        if (!donorTraits.Class.IsNull) target.Class.SetTo(donorTraits.Class.FormKey);
        if (!donorTraits.CombatStyle.IsNull) target.CombatStyle.SetTo(donorTraits.CombatStyle.FormKey);
        return true;
    }

    // ------------------------------------------------------------------ filters

    /// <summary>
    /// A form is admissible if it resolves, is not one of Requiem's tombstones, and is not something the
    /// next Reqtificator run assigns by itself.
    /// </summary>
    private bool Admissible<T>(IFormLinkGetter<T> link) where T : class, ISkyrimMajorRecordGetter
    {
        if (link.IsNull) return false;
        if (StackData.ReqtificatorAssigned.Contains(link.FormKey)) return false;
        if (link.TryResolve(_cache) is not { } record) return false;
        return !StackData.IsTombstone(record.EditorID);
    }

    private ISkyrimMajorRecordGetter? Resolve<T>(IFormLinkGetter<T> link) where T : class, ISkyrimMajorRecordGetter =>
        link.TryResolve(_cache);

    private bool IsStackOwned(ModKey key) =>
        _donorPlugins.Contains(key) || key.FileName.String.StartsWith("cc", StringComparison.OrdinalIgnoreCase);

    private static string Name(INpcGetter npc) => $"{npc.EditorID ?? npc.Name?.String ?? "<unnamed>"} ({npc.FormKey})";

    private static string Name(IFormLinkNullableGetter<INpcSpawnGetter> link) => link.FormKey.ToString();
}
