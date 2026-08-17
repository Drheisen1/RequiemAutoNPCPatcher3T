using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.WPF.Reflection.Attributes;

namespace RequiemAutoNPCPatcher3T;

public class Settings
{
    [SettingName("Mods to patch")]
    [Tooltip("The plugins whose NPCs get rebalanced onto the Requiem + 3BFTweaks ladders.\n\n" +
             "Nothing is patched until you list a plugin here. Only records DEFINED in these plugins are " +
             "touched; records they merely override are left to the stack unless you tick the option below.")]
    public List<ModKey> TargetMods { get; set; } = new();

    [SettingName("Also patch records these mods only override")]
    [Tooltip("Off (default): a target mod's override of a vanilla or Requiem actor is left alone, because the " +
             "3BFTweaks stack already owns that record and re-deriving it would fight the stack.\n\n" +
             "On: those overrides get rebalanced too. Turn this on only for a mod that deliberately re-levels " +
             "existing actors.")]
    public bool PatchOverriddenRecords { get; set; } = false;

    [SettingName("Never patch these plugins")]
    [Tooltip("Applied on top of the target list.")]
    public List<ModKey> ExcludedMods { get; set; } = new()
    {
        ModKey.FromNameAndExtension("MoreNastyCritters.esp"),
    };

    [SettingName("Never patch these NPCs")]
    public List<IFormLinkGetter<INpcGetter>> ExcludedNpcs { get; set; } = new();

    // ---------------------------------------------------------------- run order

    [SettingName("Plugins that are generated output (never read)")]
    [Tooltip("This patcher runs BEFORE the Reqtificator, and the Reqtificator is regenerated afterwards. " +
             "Every record it reads therefore resolves the load-order winner with these plugins REMOVED, so it " +
             "never copies a Reqtificator-computed value and never double-stamps a perk the next Reqtificator " +
             "run will assign itself.\n\n" +
             "Build order is: Synthesis -> Reqtificator -> play. This patcher's own ESP must sit ABOVE " +
             "'Requiem for the Indifferent.esp'.\n\n" +
             "Leave this list alone unless your stack adds another generated plugin.")]
    public List<ModKey> GeneratedOutputPlugins { get; set; } = new()
    {
        ModKey.FromNameAndExtension("Requiem for the Indifferent.esp"),
        ModKey.FromNameAndExtension("PGPatcher.esp"),
        ModKey.FromNameAndExtension("PG_1.esp"),
        ModKey.FromNameAndExtension("DynDOLOD.esm"),
        ModKey.FromNameAndExtension("DynDOLOD.esp"),
        ModKey.FromNameAndExtension("Occlusion.esp"),
    };

    [SettingName("Plugins the balance is read FROM")]
    [Tooltip("The donor pool. Every number this patcher writes is read live out of these plugins' records at " +
             "their load-order winner (with the generated plugins above removed), so it follows 3BFTweaks " +
             "updates instead of freezing a copy of them.\n\n" +
             "Creation Club plugins (cc*) are always included as donors.")]
    public List<ModKey> DonorPlugins { get; set; } = new()
    {
        ModKey.FromNameAndExtension("Skyrim.esm"),
        ModKey.FromNameAndExtension("Update.esm"),
        ModKey.FromNameAndExtension("Dawnguard.esm"),
        ModKey.FromNameAndExtension("HearthFires.esm"),
        ModKey.FromNameAndExtension("Dragonborn.esm"),
        ModKey.FromNameAndExtension("Requiem.esp"),
        ModKey.FromNameAndExtension("3Tweaks.esp"),
        ModKey.FromNameAndExtension("FTweaks.esp"),
        ModKey.FromNameAndExtension("3Tweaks - Small Tweaks Patch.esp"),
        ModKey.FromNameAndExtension("Requiem-general_NPC_tweaks.esp"),
    };

    // ---------------------------------------------------------------- what to patch

    [SettingName("Patch humanoids")]
    [Tooltip("Weapon-using humanoids are rebalanced onto 3BFTweaks' bandit rank grid " +
             "(FZR_Bandit_Template_*, 9 archetypes x 6 ranks, read live out of FTweaks.esp). " +
             "Casters take the caster path instead.")]
    public bool PatchHumanoids { get; set; } = true;

    [SettingName("Patch casters")]
    [Tooltip("Magic-dominant humanoids are not on the bandit grid. They are matched against the nearest " +
             "caster actor in the donor plugins by level, the same way creatures are.")]
    public bool PatchCasters { get; set; } = true;

    [SettingName("Patch creatures")]
    [Tooltip("Creatures are rebalanced by copying a whole comparable actor of the SAME RACE out of the donor " +
             "plugins - stats, skills, perks, abilities, class, combat style and level - with the template " +
             "chain walked so a donor whose stats are inherited contributes its template's numbers, not its " +
             "own dead bytes.")]
    public bool PatchCreatures { get; set; } = true;

    [SettingName("Patch ghosts and spirits")]
    [Tooltip("Ghost and spirit are race-independent STATE traits in this stack, so a ghost's comparable " +
             "is another ghost rather than another actor of its race. Detected from the IsGhost flag, " +
             "the Keyword_Ghost / Keyword_Spirit keywords, or a ghost race.")]
    public bool PatchGhosts { get; set; } = true;

    [SettingName("Let clones of vanilla NPCs inherit instead of being patched")]
    [Tooltip("An actor that templates its Traits from an NPC outside the patched mods - a clone of a " +
             "vanilla or Requiem actor, like BaboEventBrunwulf templating Brunwulf - gets 'Use Stats' " +
             "ticked, so its numbers come from the original that Requiem, 3BFTweaks and the Reqtificator " +
             "have already balanced.\n\n" +
             "'Use Spell List' is deliberately NOT ticked. That flag replaces the clone's perks and " +
             "abilities rather than merging them, which would silently delete mod-defined perks the mod's " +
             "own quests and events depend on. Instead the clone keeps its own loadout and the original's " +
             "perks and abilities are merged in on top.")]
    public bool InheritFromStackTemplates { get; set; } = true;

    [SettingName("De-level children")]
    [Tooltip("A child gets a fixed level and NOTHING else - no pools, no skill line, no perks, no " +
             "abilities, no class, no combat style. Detected from the RACE's own Child flag, not from " +
             "the EditorID.\n\nTurn this off to leave child actors completely untouched.")]
    public bool DeLevelChildren { get; set; } = true;

    [SettingName("Level to give children")]
    public int ChildLevel { get; set; } = 1;

    [SettingName("Patch non-combatants")]
    [Tooltip("Off (default): an actor whose STRONG evidence is entirely civilian - a merchant class, no " +
             "weapon, a flat skill line, no combat style, no perks - is left alone and logged.\n\n" +
             "Ambiguous actors are always treated as combatants regardless of this setting: over-statting " +
             "someone who never fights costs nothing, under-statting someone who turns hostile in act three " +
             "hands the player a free boss kill.")]
    public bool PatchNonCombatants { get; set; } = false;

    // ---------------------------------------------------------------- humanoid rank grid

    [SettingName("Lowest bandit rank a humanoid may be given")]
    [Tooltip("1..6. Rank levels are 3 / 7 / 10 / 12 / 19 / 24.")]
    public int MinimumRank { get; set; } = 1;

    [SettingName("Highest bandit rank a humanoid may be given")]
    [Tooltip("1..6. Rank levels are 3 / 7 / 10 / 12 / 19 / 24.")]
    public int MaximumRank { get; set; } = 6;

    [SettingName("Default archetype when none can be determined")]
    [Tooltip("Used when an actor carries no weapon and its skill line gives no answer. Every use is logged.")]
    public BanditArchetype FallbackArchetype { get; set; } = BanditArchetype.SwordShield;

    // ---------------------------------------------------------------- levels

    [SettingName("Convert PC-level-scaled actors to fixed levels")]
    [Tooltip("3BFTweaks is a fixed-level game: 1,599 of its 1,624 actors use a fixed NpcLevel and every " +
             "exception is a named, friendly humanoid. A mod shipping PcLevelMult on a hostile is the absence " +
             "of a decision, not a decision.")]
    public bool ConvertPcLevelMult { get; set; } = true;

    [SettingName("Keep PC-level scaling on non-combatants")]
    [Tooltip("Hirelings, followers and merchants are the one actor kind the stack does leave PC-scaled.")]
    public bool KeepScalingOnNonCombatants { get; set; } = true;

    // ---------------------------------------------------------------- creatures

    [SettingName("Creature race donor overrides")]
    [Tooltip("For a creature on a race the donor plugins contain no actor of - a brand new custom monster race " +
             "- name the vanilla or stack race its balance should be copied from.\n\n" +
             "Without an entry here such an actor is SKIPPED and reported in the log as needing a decision. " +
             "It is never silently guessed at.")]
    public List<RaceDonorOverride> RaceDonorOverrides { get; set; } = new();

    [SettingName("Strip Giant Stomp from non-giant races")]
    [Tooltip("crGiantStomp is a minor stagger in vanilla and an AoE knockdown in Requiem. Creature mods reuse " +
             "it freely, which makes those creatures unfightable. Giants and Lurkers keep it.\n\n" +
             "Only races DEFINED in the target mods are touched.")]
    public bool RemoveGiantStomp { get; set; } = true;

    // ---------------------------------------------------------------- logging

    [SettingName("Log every patched actor")]
    [Tooltip("Off: counts per plugin, plus every warning and every skip. On: a line per actor naming the donor " +
             "it was balanced against.")]
    public bool VerboseLog { get; set; } = false;
}

public class RaceDonorOverride
{
    [SettingName("Custom race")]
    public IFormLinkGetter<IRaceGetter> TargetRace { get; set; } = FormLink<IRaceGetter>.Null;

    [SettingName("Copy balance from this race's actors")]
    public IFormLinkGetter<IRaceGetter> DonorRace { get; set; } = FormLink<IRaceGetter>.Null;
}

public enum BanditArchetype
{
    AxeShield,
    BattleAxe,
    Bow,
    Crossbow,
    GreatSword,
    MaceShield,
    SwordShield,
    Trickster,
    Warhammer,
}
