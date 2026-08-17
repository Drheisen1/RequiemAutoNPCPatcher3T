using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace RequiemAutoNPCPatcher3T;

/// <summary>
/// The only FormIDs this patcher hardcodes: identities (keywords, races) and the addresses of the
/// 54 bandit rank templates. Every NUMBER is read out of those records at run time, so the patcher
/// follows a 3BFTweaks update instead of freezing a copy of it.
/// </summary>
public static class StackData
{
    public static readonly ModKey Skyrim = ModKey.FromNameAndExtension("Skyrim.esm");
    public static readonly ModKey Dawnguard = ModKey.FromNameAndExtension("Dawnguard.esm");
    public static readonly ModKey Dragonborn = ModKey.FromNameAndExtension("Dragonborn.esm");
    public static readonly ModKey Requiem = ModKey.FromNameAndExtension("Requiem.esp");
    public static readonly ModKey FTweaks = ModKey.FromNameAndExtension("FTweaks.esp");

    private static FormKey Sky(uint id) => new(Skyrim, id);
    private static FormKey Req(uint id) => new(Requiem, id);
    private static FormKey Ftw(uint id) => new(FTweaks, id);

    // ------------------------------------------------------------ classification keywords

    public static readonly FormKey ActorTypeNPC = Sky(0x013794);
    public static readonly FormKey ActorTypeCreature = Sky(0x013795);
    public static readonly FormKey ArmorShield = Sky(0x0965B2);

    public static readonly FormKey WeapTypeSword = Sky(0x01E711);
    public static readonly FormKey WeapTypeWarAxe = Sky(0x01E712);
    public static readonly FormKey WeapTypeDagger = Sky(0x01E713);
    public static readonly FormKey WeapTypeMace = Sky(0x01E714);
    public static readonly FormKey WeapTypeBow = Sky(0x01E715);
    public static readonly FormKey WeapTypeWarhammer = Sky(0x06D930);
    public static readonly FormKey WeapTypeGreatsword = Sky(0x06D931);
    public static readonly FormKey WeapTypeBattleaxe = Sky(0x06D932);
    public static readonly FormKey WeapTypeQuarterstaff = Req(0xADDF81);

    /// <summary>FoxRace is used across the stack as an invisible marker/spawner race — a 1683-health
    /// "fox" (npcs.md §14.1) — and is also what the CK leaves in the Race field of any actor whose
    /// Traits are templated. Never a stat comparable.</summary>
    public static readonly FormKey FoxRace = Sky(0x109C7C);

    /// <summary>The CK's other placeholder. It sits in the Race field of every one of the 54 bandit
    /// templates, and on leftovers like `AADeleteWhenDoneTestJeremyBig`. Reading it as a real race makes
    /// a skeever list resolve to "Default Race".</summary>
    public static readonly FormKey DefaultRace = Sky(0x000019);

    /// <summary>A race that names nothing about the actor — the CK's placeholders.</summary>
    public static bool IsPlaceholderRace(FormKey race) => race == FoxRace || race == DefaultRace;

    public static readonly FormKey GiantRace = Sky(0x0131F9);
    public static readonly FormKey LurkerRace = new(Dragonborn, 0x014495);

    /// <summary>crGiantStomp — a minor stagger in vanilla, an AoE knockdown under Requiem.</summary>
    public static readonly FormKey GiantStompSpell = Sky(0x02FFD2);

    // ------------------------------------------------------------ the bandit rank grid

    /// <summary>Rank index 1..6 -> the level FTweaks gives that rank. Verified against all 54 cells.</summary>
    public static readonly int[] RankLevels = { 0, 3, 7, 10, 12, 19, 24 };

    /// <summary>
    /// FZR_Bandit_Template_&lt;Archetype&gt;_0&lt;N&gt;_Forn, 025897–0258CC:FTweaks.esp, laid out
    /// archetype-major / rank-minor. Every one of the 54 is uncontested in the load order — FTweaks.esp
    /// is both the definer and the winner — so these addresses ARE the live records.
    /// </summary>
    public static FormKey BanditTemplate(BanditArchetype archetype, int rank)
    {
        if (rank is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(rank));
        return Ftw((uint)(0x025897 + (int)archetype * 6 + (rank - 1)));
    }

    // ------------------------------------------------------------ the Reqtificator's own assignments

    /// <summary>
    /// Perks and spells that <c>ActorAssignmentRules_*.conf</c> hands every actor on the NEXT Reqtificator
    /// run (npcs.md §4.1). Because this patcher runs first, writing any of them here would double-stamp
    /// (npcs.md §13.2 item 23). They are filtered out of everything copied from a donor.
    /// </summary>
    public static readonly IReadOnlySet<FormKey> ReqtificatorAssigned = new HashSet<FormKey>
    {
        Req(0xAD394D), // RFTI_All_ArmorPenetration_Resistances_Slash
        Req(0xAD3948), // ..._Ranged
        Req(0xAD394C), // ..._Pierce
        Req(0xAD394E), // ..._Blunt
        Req(0xAD394B), // RFTI_All_ArmorPenetration_PowerAttacks
        Req(0xAD394A), // RFTI_All_ArmorPenetration_StandardAttacks
        Sky(0x0CF788), // REQ_Reqtificator_ActorValue_Modifier
        Sky(0x0A725C), // RFTI_All_ActorValuePowerModifier
        Req(0xAD3A34), // RFTI_All_ArmorWeight
        Req(0xAD3A35), // RFTI_All_ArrowRecovery
        Req(0x962799), // RFTI_All_AbsorbRescaling
        Req(0x682FB5), // REQ_Reqtificator_Ward_PhysicalReduction
        Req(0x962798), // RFTI_All_PoisonRescaling
        Req(0x703B25), // RFTI_All_Stress_ArmoredCasting
        Req(0x6B9709), // RFTI_All_Stress_AttackStaminaCost
        Req(0x95FFFB), // RFTI_All_Stress_ExhaustionPenalties
        Req(0x755649), // RFTI_All_Stress_MassEffect
        Req(0xAD3977), // RFTI_NPC_PersistentSpellRescaling  (spell)
        new(ModKey.FromNameAndExtension("3Tweaks.esp"), 0x15DE7C), // Yeomen_Perk_Skyproc_General
        Ftw(0x024C9E), // Forn_ShockCastDebuff
    };

    /// <summary>Requiem retires records by hollowing them out rather than deleting them; a mod's Perks[]
    /// and ActorEffect[] are full of the corpses. Detected by EditorID prefix.</summary>
    public static bool IsTombstone(string? editorId) =>
        editorId is not null &&
        (editorId.StartsWith("REQ_NULL_", StringComparison.OrdinalIgnoreCase) ||
         editorId.StartsWith("REQ_LEGACY_", StringComparison.OrdinalIgnoreCase) ||
         editorId.StartsWith("NULL_", StringComparison.OrdinalIgnoreCase));

    /// <summary>The one ability the bandit templates carry: REQ_Trait_Tempering_Bandit_{Heavy,Light}_RankN.</summary>
    public static bool IsTemperingTrait(string? editorId) =>
        editorId is not null &&
        editorId.StartsWith("REQ_Trait_Tempering_", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------ misc

    public static readonly Skill[] CombatSkills =
    {
        Skill.OneHanded, Skill.TwoHanded, Skill.Archery, Skill.Block,
        Skill.HeavyArmor, Skill.LightArmor, Skill.Sneak,
        Skill.Destruction, Skill.Conjuration, Skill.Alteration, Skill.Illusion, Skill.Restoration,
    };

    public static readonly Skill[] MagicSkills =
    {
        Skill.Destruction, Skill.Conjuration, Skill.Alteration, Skill.Illusion, Skill.Restoration,
    };
}
