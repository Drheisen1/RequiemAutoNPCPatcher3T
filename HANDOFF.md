# Session prompt — rewrite RequiemAutoNPCPatcher for Requiem + 3BFTweaks

Paste everything below into a fresh session.

---

I want you to write a Synthesis patcher for Heretic that rebalances modded NPCs onto the
**Requiem + 3BFTweaks** ladders. It is a **ground-up rewrite** of
`https://github.com/tomnGithub/RequiemAutoNPCPatcher` — not a fork. A previous session did the
analysis below; it is verified against the live load order, so trust it, but re-check anything you
are about to depend on.

Load the **`requiem-3tweaks-patching`** skill before you read your first record. Do **not** use the
`authoria-requiem:*` skills — those carry stock-Requiem numbers and are wrong here. Do not treat any
patch I have been given before as authoritative; derive from the live records.

## The hard constraint that drives the whole design

**Synthesis runs BEFORE the Reqtificator. The Reqtificator is regenerated afterwards.**

Consequences, and they are not optional:

- **`Requiem for the Indifferent.esp` must never be read.** Not as a donor, not for a "winner"
  lookup, not to check anything. Every read must resolve the winner **excluding** that plugin (and
  any other generated output — `PGPatcher.esp`, DynDOLOD, this patcher's own previous output).
- **Do not hand-place any perk, spell or keyword the Reqtificator assigns.** The universal actor
  block, the racial/victim-side trait perks and the damage-type keywords all come from
  `ActorAssignmentRules_Requiem.esp.conf` and `WeaponKeywordAssignments_Requiem.esp.conf` on the
  *next* Reqtificator run. Writing them here double-stamps them (`npcs.md` §13.2 item 23, §4.1).
  Copy only what Requiem/3Tweaks **authored** on the donor record.
- The patcher's ESP therefore sits **above** `Requiem for the Indifferent.esp`, and the build order
  is: Synthesis → Reqtificator → play.

An earlier draft assumed the opposite order and planned to union the donor's full perk list into the
target. That is wrong. Discard it.

## What I want

- **Creatures — wholesale.** Copy a whole comparable actor of the matched race out of the live load
  order: stats, perks, abilities, class, combat style, level.
- **Humanoids — the rank grid.** 3BFTweaks' bandit templates.
- **No SPID, no companion ESP.** The upstream patcher pushes everything through
  `RequiemPatcherKeyword.esp` + a generated `_DISTR.ini`. All of that goes; Synthesis writes the
  records directly even if that is slower. This matches the stack, which ships one SPID line and
  zero KID lines.
- **Catch everything.** It has to work on mods that add 1000+ NPCs, and it should not silently skip
  actor kinds.

## What the upstream patcher actually does (verified, 3287 lines)

Its ESP writes are tiny — per NPC only: `Class` (on archetype mismatch), one marker keyword,
`Configuration.Level` / `CalcMinLevel`, and the three stat offsets. On RACE records: `Starting`
H/M/S, `UnarmedDamage`, `BaseMass`, `Regen`, a race-marker keyword, and a `crGiantStomp` strip.
**Every perk, spell, per-level stat scaling and creature armor trait is SPID.** So removing SPID
means writing the half of the patcher that does not currently exist.

Three findings worth keeping:

1. **Its hardcoded creature literals are Requiem's own exemplar-NPC offsets, copied by hand.**
   `GiantRace: health 1000, stamina 1400` is exactly `EncGiant02` `030437:Skyrim.esm`.
   `HagravenRace: health 100, magicka 650` is exactly `EncHagraven` `023AB0:Skyrim.esm`.
   → The fix is not to re-derive ~60 tables for 3Tweaks. It is to **stop hardcoding and read the
   donor live**, which is correct by construction and survives 3Tweaks updates.
2. **`racePerksToDistribute` and `raceSpellsToDistributNEw` are dead code.** They are built up across
   ~40 switch cases and never read anywhere in the file — every occurrence is a declaration, an
   `Array.Resize`, or an `[i] =` assignment. Creature natural armor is computed and thrown away
   upstream. The rewrite fixes this incidentally.
3. **Its RACE half already reads the LinkCache winner**, so that part is already 3Tweaks-aware.
   Its `WarriorHealthPerLevel 5.5 / Stamina 8.5 / Magicka 8.5 / PureMageHealth 3` settings are
   stock-Requiem **player** scaling pushed onto NPCs, and do not fit a fixed-level game.

## The 3Tweaks data already extracted

**The humanoid rank grid** — `FZR_Bandit_Template_<Archetype>_0<N>_Forn`,
`025897`–`0258CC:FTweaks.esp`, 9 archetypes × 6 ranks. Read them with `plugin="FTweaks.esp"`, never
at the Reqtificator winner.

| Archetype | Class (`:Requiem.esp`) | Base template (`:Requiem.esp`) | CombatStyle |
|---|---|---|---|
| AxeShield | `85BCE2` | `868382` | `csHumanMeleeLvl1` `03BE1B:Skyrim.esm` |
| BattleAxe | `86D2E6` | `8749C0` | `03BE1B` |
| Bow | `879915` | `879913` | `csHumanMissile` `03BE1D:Skyrim.esm` |
| Crossbow | `879916` | `881001` | `03BE1D` |
| GreatSword | `86D2E8` | `86D2E5` | `03BE1B` |
| MaceShield | `85BCE1` | `84F6B5` | `03BE1B` |
| SwordShield | `85BCE3` | `86837D` | `03BE1B` |
| Trickster | `8A8BE2` | `8A8BE0` | `03BE1D` |
| Warhammer | `86D2E7` | `8749BC` | `03BE1B` |

Rank → level: **3 · 7 · 10 · 12 · 19 · 24**. All 54 have all three `*Offset` fields at **0**,
`PlayerSkills.Magicka = 100`, `Configuration.Flags = Respawn, AutoCalcStats, LoopedScript,
LoopedAudio`, and `TemplateFlags = Traits, Factions, AIData, AIPackages, Script, DefPackList,
AttackData, Keywords` — i.e. **inherit behaviour, own numbers and gear**.

`npcs.md` §5.1.1 gives the stat formula, verified against all 54 cells:

```
Health  = 100 + surplus × Class.StatWeights[Health]  / 10
Magicka = 100 + surplus × Class.StatWeights[Magicka] / 10
Stamina = 100 + surplus × Class.StatWeights[Stamina] / 10
```

Surplus per rank is 10 / 30 / 45 / 55 / 90 / 115, which is an **exact linear fit:
`surplus = 5 × (level − 1)`** (check it: 3→10, 7→30, 10→45, 12→55, 19→90, 24→115). That means the
grid extrapolates cleanly past level 24 instead of clamping at rank 06. The published rounding is
not a single rule — 5:0:5 classes floor Health and ceil Stamina, 3:0:7 classes do the opposite — so
for levels ≤ 24 just read the template's `PlayerSkills` live and only use the formula above 24.

**Creatures have three stat authorities** (`npcs.md` §5.2) and guessing wrong is the most common
failure:

- **A — `NPC_` ladder**: a numbered `Enc<Family>0N` chain. Draugr, Dremora, Falmer, Dwemer automata,
  Dragons, and **humanoid casters** (Warlocks/Cultists).
- **B — `RACE` ladder**: one flat `NPC_` stat line per species; tiers are *separate races*. Bears,
  trolls, sabre cats, frostbite spiders, chaurus, spriggans, mudcrabs, most animals. Every troll
  actor is level 14 / 280 H / 340 S; every bear actor is level 12.
- **C — trait-spell only**: flat `NPC_` and `RACE`; difficulty lives in `REQ_Trait_Armor_*` /
  `_Resist_*` / `_Healing_*`.

A donor picked as *"actor of the matched race, nearest by level, from the donor plugins"* is correct
for all three: A picks the right rung, B is flat so any pick is right, C is carried by the RACE copy.
Verify that reasoning rather than taking it on trust.

**Casters are not on the bandit grid** (`npcs.md` line 515 and 596) — they are pattern A. Classify
them by magic-dominant `Class.StatWeights` and pull from the `EncWarlock*` / cultist ladder rather
than hardcoding a list.

**The template trap (`npcs.md` §7.2) is mandatory, not optional.** A record with `Stats` in
`TemplateFlags` has no stats of its own; the same holds per block for `SpellList` (= `ActorEffect`
**and** `Perks`), `Inventory`, `Traits`, `Factions`, `Keywords`. Two live examples:
`EncGiant02` has `Stats, SpellList` templated from `EncGiant01`, so the `HealthOffset = 1000` sitting
in its own bytes is dead. `EncDraugr01Template2H` displays 50/80 and plays 300/80. So:

- **Reading a donor**: walk the `Template` chain until you reach the record that actually owns the
  block you want.
- **Writing a target**: never write a block the target has templated. If the template root is inside
  the mods being patched it gets fixed on its own turn; if it is outside, the actor already inherits
  stack values.

## Rules that are not negotiable

- **Never touch `NPC_.Factions[]`** — read it as evidence, never write it.
- **Never read `Requiem for the Indifferent.esp`** or any other generated output.
- **`MoreNastyCritters.esp` is out of scope** — exclude it by default.
- Hostiles get a fixed `NpcLevel`; PC-level scaling survives only on named friendly humanoids
  (`npcs.md` §6). A mod's shipped `PcLevelMult` is the absence of a decision.
- Classify combatant-or-not from the **strong** evidence — skills, class, gear, spells, factions —
  never from `Essential` / `Invulnerable` / `IsGhost` / `Aggression`. When they disagree, it fights
  (`npcs.md` §11.3).

## Environment

- Instance `D:\Wabbajack\Heretic`, profile `BottleRim`, 896 plugins. houseCARL is already pointed at it.
- Load order positions: `Requiem.esp` 682 · `FTweaks.esp` 700 · `Requiem for the Indifferent.esp`
  884 of 887.
- Synthesis is at `D:\Wabbajack\Heretic\tools\Synthesis`, with an existing group
  **"Heretic - Synthesis Output"** in `PipelineSettings.json`.
- The profile pins **Mutagen 0.54.4 / Synthesis 0.36.6** (`LastSuccessfulRun` on the HPH patcher).
  Match those or Synthesis will rewrite the csproj at build time. `net8.0`. SDKs 8/9/10 installed.
- A scaffold exists at `D:\Mods\Code\RequiemAutoNPCPatcher3T\` — `.csproj` and `Settings.cs` only.
  **Re-check `Settings.cs` against the corrected run order before reusing it**; it was written under
  the wrong assumption and at minimum its donor-plugin defaults need the Reqtificator exclusion made
  explicit.

## Open questions — ask me, do not guess

1. Should the patcher touch `DefaultOutfit` / `Items` at all? The previous draft deliberately left
   gear alone so mods keep their visual identity, and relied on the donor's
   `REQ_Trait_Tempering_*` ability instead. Confirm that is what I want.
2. How far should "catch everything" go — every plugin that is not vanilla/stack, or an explicit
   target list?

## When the code is done

Tell me how to add and run it in Synthesis, and whether I have to publish it to GitHub first.
