using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace RequiemAutoNPCPatcher3T;

/// <summary>
/// Races defined by the target mods.
///
/// A creature's difficulty often does not live on its NPC_ record at all. npcs.md §5.2 pattern B — bears,
/// trolls, sabre cats, spiders, chaurus, spriggans, most animals — puts one flat stat line on every actor
/// of the species and tiers by RACE instead, and §5.5 puts creature DAMAGE on the race
/// (<c>UnarmedDamage x Attacks[i].DamageMult</c>) rather than on any weapon. So a mod that ships a new
/// creature race needs the race brought onto the ladder, or nothing else will land.
///
/// The trait triple that gives a creature its armour rating, its resistances and its regeneration
/// (<c>REQ_Trait_Armor_*</c> / <c>_Resist_*</c> / <c>_Healing_*</c>) hangs off <c>RACE.ActorEffect</c>,
/// which is why copying the donor race's ability list matters more than any number here: without
/// <c>REQ_Trait_Armor_*</c> the creature has 0 armour rating, and because the weapon-type resistance
/// perks are gated on <c>GetActorValue(DamageResist) &gt; 0</c>, every resistance rank is inert too.
/// </summary>
public sealed class RacePatcher
{
    private readonly ILinkCache _cache;
    private readonly Settings _settings;
    private readonly PatchLog _log;

    public RacePatcher(ILinkCache cache, Settings settings, PatchLog log)
    {
        _cache = cache;
        _settings = settings;
        _log = log;
    }

    public bool Apply(IRaceGetter source, Race target, IRaceGetter donor)
    {
        // Pools, regeneration, carry weight and mass.
        target.Starting.Clear();
        foreach (var (stat, value) in donor.Starting) target.Starting[stat] = value;

        target.BaseCarryWeight = donor.BaseCarryWeight;
        target.Regen.Clear();
        foreach (var (stat, value) in donor.Regen) target.Regen[stat] = value;

        target.UnarmedDamage = donor.UnarmedDamage;
        target.UnarmedReach = donor.UnarmedReach;
        target.BaseMass = donor.BaseMass;

        // The trait triple, plus whatever else the donor race hands every actor of its species.
        var effects = new List<IFormLinkGetter<ISpellRecordGetter>>();
        var seen = new HashSet<FormKey>();
        foreach (var link in donor.ActorEffect ?? Enumerable.Empty<IFormLinkGetter<ISpellRecordGetter>>())
        {
            if (link.IsNull || StackData.ReqtificatorAssigned.Contains(link.FormKey)) continue;
            if (link.TryResolve(_cache) is { } spell && StackData.IsTombstone(spell.EditorID)) continue;
            if (seen.Add(link.FormKey)) effects.Add(link);
        }
        target.ActorEffect = effects.Count == 0 ? null : new(effects);

        // ActorType* is the classification the whole system reads — turn undead, silver bonuses, the
        // victim-side trait perks and half the perk system all key off it (races.md §3). Keywords are
        // ADDED, never replaced: a mod's own race keywords may be load-bearing for its own scripts.
        AddMissingKeywords(target, donor);

        _log.Note($"race {Describe(source)} <- {Describe(donor)}");
        return true;
    }

    private static void AddMissingKeywords(Race target, IRaceGetter donor)
    {
        if (donor.Keywords is null) return;
        target.Keywords ??= new();
        var present = target.Keywords.Select(k => k.FormKey).ToHashSet();
        foreach (var keyword in donor.Keywords)
            if (!keyword.IsNull && present.Add(keyword.FormKey))
                target.Keywords.Add(keyword);
    }

    /// <summary>
    /// crGiantStomp is a minor stagger in vanilla and an AoE knockdown under Requiem. Creature mods reuse
    /// it freely because it looks like flavour, which makes those creatures unfightable. Giants and
    /// Lurkers keep it.
    /// </summary>
    public bool StripGiantStomp(IRaceGetter source, Race target)
    {
        if (!_settings.RemoveGiantStomp) return false;
        if (source.FormKey == StackData.GiantRace || source.FormKey == StackData.LurkerRace) return false;
        if (source.ActorEffect is null) return false;
        if (source.ActorEffect.All(e => e.FormKey != StackData.GiantStompSpell)) return false;

        target.ActorEffect = new(source.ActorEffect.Where(e => e.FormKey != StackData.GiantStompSpell));
        _log.Warn($"{Describe(source)}: removed crGiantStomp (an AoE knockdown under Requiem, not vanilla's stagger).");
        return true;
    }

    private static string Describe(IRaceGetter race) =>
        $"{race.EditorID ?? race.Name?.String ?? "<unnamed>"} ({race.FormKey})";
}
