using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace RequiemAutoNPCPatcher3T;

public enum ActorKind { Humanoid, Caster, Creature }

public sealed record Classification(
    ActorKind Kind,
    bool IsCombatant,
    string CombatantReason,
    BanditArchetype Archetype,
    bool ArchetypeIsGuess,
    string ArchetypeReason,
    bool IsChild);

/// <summary>
/// npcs.md §11.3. Flags and AI dispositions are one Papyrus call from being cleared, so they are never
/// evidence of what an actor IS. Classification reads only what a quest script cannot practically rewrite:
/// skills, class, combat style, perks, spells, and the gear the actor carries.
///
/// The tie-break is asymmetric on purpose. Over-statting someone who never draws a weapon costs the player
/// nothing. Under-statting someone who turns hostile in act three hands them a free boss kill.
/// </summary>
public sealed class Classifier
{
    private readonly ILinkCache _cache;
    private readonly ActorView _view;

    public Classifier(ILinkCache cache, ActorView view)
    {
        _cache = cache;
        _view = view;
    }

    public Classification Classify(INpcGetter npc, BanditArchetype fallback)
    {
        var race = _view.Race(npc);
        var isHumanoid = race is not null && HasKeyword(race, StackData.ActorTypeNPC);

        // The RACE's own Child flag, not a name guess. A child is never a combatant in this stack and
        // never belongs on the bandit grid: it gets a level and nothing else.
        var isChild = race is not null && race.Flags.HasFlag(Race.Flag.Child);

        var cls = _view.Class(npc);
        var stats = _view.Stats(npc);
        var perks = _view.Perks(npc);
        var effects = _view.ActorEffects(npc);
        var gear = ReadGear(npc);

        var (combatant, reason) = JudgeCombatant(cls, stats, npc, perks, effects, gear);

        if (isChild)
            return new Classification(ActorKind.Humanoid, false, "child race", fallback, false, "child", true);

        if (!isHumanoid)
            return new Classification(ActorKind.Creature, combatant, reason, fallback, false, "creature", false);

        if (IsCaster(cls, stats, effects))
            return new Classification(ActorKind.Caster, combatant, reason, fallback, false, "magic-dominant class / skill line", false);

        var (archetype, guessed, why) = PickArchetype(gear, stats, cls, fallback);
        return new Classification(ActorKind.Humanoid, combatant, reason, archetype, guessed, why, false);
    }

    // ------------------------------------------------------------------ combatant

    private (bool, string) JudgeCombatant(
        IClassGetter? cls,
        IPlayerSkillsGetter? stats,
        INpcGetter npc,
        IReadOnlyList<IPerkPlacementGetter> perks,
        IReadOnlyList<IFormLinkGetter<ISpellRecordGetter>> effects,
        Gear gear)
    {
        // Strong signal 1 — the actor carries a weapon or a shield.
        if (gear.Weapons.Count > 0) return (true, "carries a weapon");
        if (gear.HasShield) return (true, "carries a shield");

        // Strong signal 2 — a class whose skill weights are combat skills.
        if (cls is not null && ClassCombatWeight(cls) > 0) return (true, $"combat class {cls.EditorID}");

        // Strong signal 3 — a skill line that was deliberately raised in a combat skill.
        if (stats is not null)
        {
            var floor = FlatSkillFloor(stats);
            foreach (var skill in StackData.CombatSkills)
            {
                if (stats.SkillValues.TryGetValue(skill, out var v) && v >= floor + 15)
                    return (true, $"skill line ({skill} {v})");
            }
        }

        // Strong signal 4 — a live combat style. On a non-templated actor a NULL one is a defect, not
        // evidence of a civilian (npcs.md §11.3 worked read 1).
        var style = _view.CombatStyle(npc);
        if (style is not null && !style.IsNull) return (true, "has a combat style");

        // Strong signal 5 — perks or abilities that are not tombstones.
        if (perks.Any(p => !IsTombstoneLink(p.Perk))) return (true, "carries perks");
        if (effects.Any(e => !IsTombstoneLink(e))) return (true, "carries abilities");

        return (false, "no weapon, no combat class, flat skill line, no combat style, no perks or abilities");
    }

    private static int FlatSkillFloor(IPlayerSkillsGetter stats)
    {
        var min = 100;
        foreach (var v in stats.SkillValues.Values) min = Math.Min(min, v);
        return min;
    }

    private static int ClassCombatWeight(IClassGetter cls)
    {
        var total = 0;
        foreach (var skill in StackData.CombatSkills)
            if (cls.SkillWeights.TryGetValue(skill, out var w)) total += w;
        return total;
    }

    // ------------------------------------------------------------------ caster

    private static bool IsCaster(IClassGetter? cls, IPlayerSkillsGetter? stats, IReadOnlyList<IFormLinkGetter<ISpellRecordGetter>> effects)
    {
        // npcs.md line 515: humanoid casters are NOT on the bandit grid. Classify them by a
        // magic-dominant class rather than by a hardcoded list of caster EditorIDs.
        if (cls is not null)
        {
            cls.StatWeights.TryGetValue(BasicStat.Magicka, out var magicka);
            cls.StatWeights.TryGetValue(BasicStat.Health, out var health);
            cls.StatWeights.TryGetValue(BasicStat.Stamina, out var stamina);
            if (magicka > health && magicka > stamina) return true;

            var magicWeight = StackData.MagicSkills.Sum(s => cls.SkillWeights.TryGetValue(s, out var w) ? w : 0);
            var martialWeight = new[] { Skill.OneHanded, Skill.TwoHanded, Skill.Archery, Skill.Block }
                .Sum(s => cls.SkillWeights.TryGetValue(s, out var w) ? w : 0);
            if (magicWeight > martialWeight && magicWeight > 0) return true;
        }

        if (stats is not null)
        {
            var magic = StackData.MagicSkills.Max(s => stats.SkillValues.TryGetValue(s, out var v) ? v : 0);
            var martial = new[] { Skill.OneHanded, Skill.TwoHanded, Skill.Archery }
                .Max(s => stats.SkillValues.TryGetValue(s, out var v) ? v : 0);
            if (magic > martial + 10) return true;
        }

        return false;
    }

    // ------------------------------------------------------------------ archetype

    /// <summary>
    /// The grid's nine archetypes are three shapes: one-handed + shield (Axe/Mace/Sword), two-handed
    /// (BattleAxe/GreatSword/Warhammer), and light (Bow/Crossbow/Trickster). The weapon is the primary
    /// evidence because the classes cannot tell Bow from Crossbow or Sword from Axe apart at all — their
    /// SkillWeights are identical within a shape.
    /// </summary>
    private (BanditArchetype, bool, string) PickArchetype(
        Gear gear, IPlayerSkillsGetter? stats, IClassGetter? cls, BanditArchetype fallback)
    {
        foreach (var weapon in gear.Weapons)
        {
            var keywords = weapon.Keywords;
            if (keywords is null) continue;

            if (Has(keywords, StackData.WeapTypeBow))
            {
                // Vanilla crossbows carry WeapTypeBow too; the animation type is what splits them.
                var crossbow = weapon.Data?.AnimationType == WeaponAnimationType.Crossbow;
                return (crossbow ? BanditArchetype.Crossbow : BanditArchetype.Bow, false,
                        $"weapon {weapon.EditorID}");
            }
            if (Has(keywords, StackData.WeapTypeBattleaxe)) return (BanditArchetype.BattleAxe, false, $"weapon {weapon.EditorID}");
            if (Has(keywords, StackData.WeapTypeGreatsword)) return (BanditArchetype.GreatSword, false, $"weapon {weapon.EditorID}");
            if (Has(keywords, StackData.WeapTypeWarhammer)) return (BanditArchetype.Warhammer, false, $"weapon {weapon.EditorID}");
            if (Has(keywords, StackData.WeapTypeQuarterstaff)) return (BanditArchetype.Warhammer, false, $"quarterstaff {weapon.EditorID} (blunt two-hander)");

            if (Has(keywords, StackData.WeapTypeWarAxe))
                return (gear.HasShield ? BanditArchetype.AxeShield : BanditArchetype.Trickster, false, $"weapon {weapon.EditorID}");
            if (Has(keywords, StackData.WeapTypeMace))
                return (gear.HasShield ? BanditArchetype.MaceShield : BanditArchetype.Trickster, false, $"weapon {weapon.EditorID}");
            if (Has(keywords, StackData.WeapTypeSword) || Has(keywords, StackData.WeapTypeDagger))
                return (gear.HasShield ? BanditArchetype.SwordShield : BanditArchetype.Trickster, false, $"weapon {weapon.EditorID}");
        }

        // No weapon-type keyword to read. Fall back to the skill line, which can name the SHAPE even
        // though it cannot name the weapon inside it.
        var byShape = ShapeFromSkills(stats, cls);
        if (byShape is not null)
            return (Representative(byShape.Value, gear.HasShield, stats), true, $"skill line ({byShape})");

        return (fallback, true, "no weapon and no usable skill line");
    }

    private enum Shape { OneHanded, TwoHanded, Ranged }

    private static Shape? ShapeFromSkills(IPlayerSkillsGetter? stats, IClassGetter? cls)
    {
        int one = 0, two = 0, bow = 0;

        if (stats is not null)
        {
            stats.SkillValues.TryGetValue(Skill.OneHanded, out var a); one = a;
            stats.SkillValues.TryGetValue(Skill.TwoHanded, out var b); two = b;
            stats.SkillValues.TryGetValue(Skill.Archery, out var c); bow = c;
        }
        if (one == two && two == bow && cls is not null)
        {
            cls.SkillWeights.TryGetValue(Skill.OneHanded, out var a); one = a;
            cls.SkillWeights.TryGetValue(Skill.TwoHanded, out var b); two = b;
            cls.SkillWeights.TryGetValue(Skill.Archery, out var c); bow = c;
        }

        if (one == two && two == bow) return null;
        if (bow >= one && bow >= two) return Shape.Ranged;
        return two > one ? Shape.TwoHanded : Shape.OneHanded;
    }

    private static BanditArchetype Representative(Shape shape, bool hasShield, IPlayerSkillsGetter? stats)
    {
        var heavy = 0; var light = 0;
        if (stats is not null)
        {
            stats.SkillValues.TryGetValue(Skill.HeavyArmor, out var h); heavy = h;
            stats.SkillValues.TryGetValue(Skill.LightArmor, out var l); light = l;
        }

        return shape switch
        {
            Shape.Ranged => BanditArchetype.Bow,
            Shape.TwoHanded => heavy >= light ? BanditArchetype.GreatSword : BanditArchetype.BattleAxe,
            _ => hasShield || heavy > light ? BanditArchetype.SwordShield : BanditArchetype.Trickster,
        };
    }

    // ------------------------------------------------------------------ gear

    public sealed record Gear(List<IWeaponGetter> Weapons, bool HasShield);

    /// <summary>
    /// Reads what the actor carries. Gear is never WRITTEN by this patcher — mods keep their visual
    /// identity — but it is the strongest evidence available about what the actor is for.
    /// </summary>
    public Gear ReadGear(INpcGetter npc)
    {
        var weapons = new List<IWeaponGetter>();
        var shield = false;

        var visited = new HashSet<FormKey>();

        void Consider(FormKey formKey, int depth)
        {
            if (depth > 4 || formKey.IsNull) return;
            if (!visited.Add(formKey)) return;
            if (!_cache.TryResolve<ISkyrimMajorRecordGetter>(formKey, out var item)) return;

            switch (item)
            {
                case IWeaponGetter weapon:
                    weapons.Add(weapon);
                    break;
                case IArmorGetter armor:
                    if (armor.Keywords is not null && Has(armor.Keywords, StackData.ArmorShield)) shield = true;
                    break;
                case ILeveledItemGetter leveled:
                    foreach (var entry in leveled.Entries ?? Enumerable.Empty<ILeveledItemEntryGetter>())
                        if (entry.Data is { } data) Consider(data.Reference.FormKey, depth + 1);
                    break;
            }
        }

        foreach (var entry in _view.Items(npc))
            if (entry.Item is { } contained) Consider(contained.Item.FormKey, 0);

        if (_view.Outfit(npc)?.TryResolve(_cache) is { } outfit)
            foreach (var item in outfit.Items ?? Enumerable.Empty<IFormLinkGetter<IOutfitTargetGetter>>())
                Consider(item.FormKey, 0);

        return new Gear(weapons, shield);
    }

    // ------------------------------------------------------------------ helpers

    internal static bool Has(IReadOnlyList<IFormLinkGetter<IKeywordGetter>> keywords, FormKey key) =>
        keywords.Any(k => k.FormKey == key);

    private static bool HasKeyword(IRaceGetter race, FormKey key) =>
        race.Keywords is not null && Has(race.Keywords, key);

    private bool IsTombstoneLink<T>(IFormLinkGetter<T> link) where T : class, ISkyrimMajorRecordGetter =>
        link.TryResolve(_cache) is { } record && StackData.IsTombstone(record.EditorID);
}
