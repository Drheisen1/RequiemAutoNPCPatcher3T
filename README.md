# RequiemAutoNPCPatcher3T

A Synthesis patcher that rebalances modded NPCs onto Heretic's **Requiem + 3BFTweaks** ladders.

A ground-up rewrite of [RequiemAutoNPCPatcher](https://github.com/tomnGithub/RequiemAutoNPCPatcher),
not a fork. Nothing is shared but the idea.

---

## The constraint the whole design turns on

**This patcher runs BEFORE the Reqtificator, and the Reqtificator is regenerated afterwards.**

Build order: **Synthesis → Reqtificator → play**, with `RequiemAutoNPCPatcher3T.esp` sitting
**above** `Requiem for the Indifferent.esp`.

Two consequences are baked into the code:

- **`Requiem for the Indifferent.esp` is never read.** Every lookup resolves the load-order winner
  with the generated plugins removed, so no value the Reqtificator computed is ever copied. The list
  is editable in settings and also covers `PGPatcher.esp`, DynDOLOD and this patcher's own output.
- **Nothing the `ActorAssignmentRules_*.conf` files assign is ever written.** The universal actor
  block, the racial and state trait perks and the damage-type keywords all arrive on the *next*
  Reqtificator run; writing them here would double-stamp them. Only what Requiem and 3BFTweaks
  **authored on a record** is copied, and the 20 conf-assigned forms are filtered out of everything.

## No hardcoded numbers

The upstream patcher carried roughly 60 hand-written tables of creature stats. Those literals turn
out to be Requiem's own exemplar NPCs copied by hand — `GiantRace: health 1000, stamina 1400` is
`EncGiant02`, `HagravenRace: health 100, magicka 650` is `EncHagraven`. Re-deriving them for 3BFTweaks
would just move the staleness.

So this patcher stores **no stat, level, perk or ability values at all**. It hardcodes addresses and
identities only — the 54 bandit-template FormIDs, the weapon-type and `ActorType*` keywords, the
conf-assignment blocklist — and reads every number live. A 3BFTweaks update changes the output with
no code change.

There is also no SPID file and no companion ESP. The upstream patcher pushed every perk, spell and
creature trait through `RequiemPatcherKeyword.esp` plus a generated `_DISTR.ini`; the whole of that is
gone, because the stack itself ships one SPID line and zero KID lines. Synthesis writes the records.

## What it does to an actor

### Humanoids — the 3BFTweaks rank grid

`FZR_Bandit_Template_<Archetype>_0<N>_Forn`, `025897`–`0258CC:FTweaks.esp` — 9 archetypes × 6 ranks,
read live out of FTweaks.esp. All 54 are uncontested in the load order, so FTweaks' body *is* the
winner.

| Rank | 01 | 02 | 03 | 04 | 05 | 06 |
|---|---|---|---|---|---|---|
| Level | 3 | 7 | 10 | 12 | 19 | 24 |
| Health + Stamina | 210 | 230 | 245 | 255 | 290 | 315 |
| Perks | 4–5 | 8–9 | 10–12 | 11–12 | 15–19 | 17–21 |

The actor's intended level picks the nearest rank; that rank's whole record is then copied — level,
the three pools, the 18 skill values, the full perk loadout, the `REQ_Trait_Tempering_Bandit_*`
ability, the `REQ_Class_Bandit_*` class and the combat style.

The archetype comes from the **weapon** the actor carries, because the nine bandit classes cannot tell
Bow from Crossbow or Sword from Axe apart — their `SkillWeights` are identical within a shape. Only
when there is no weapon does it fall back to the skill line, and every such guess is logged.

### Casters

Magic-dominant humanoids are not on the bandit grid. They are matched against the nearest caster in
the donor plugins by level. Casters are recognised from a magic-dominant `Class.StatWeights` or skill
line, not from a hardcoded list of EditorIDs, so FTweaks' own `EncWarlock*Template*_Forn` stratum is
found automatically alongside vanilla's.

### Creatures

A whole comparable actor of the **same race**, nearest by level, is copied out of the donor plugins.

That one rule is correct for all three of the stack's creature stat authorities. A numbered
`Enc<Family>0N` ladder (draugr, dremora, falmer, dwemer, dragons) needs the right rung and gets it. A
`RACE` ladder (bears, trolls, spiders, most animals) puts one flat stat line on every actor of the
species — every troll is level 14 / 280 / 340 — so any pick is the same pick. A trait-spell-only
family carries its difficulty on `RACE.ActorEffect`, which the target already inherits from the race
it shares with the donor.

A creature on a **brand-new custom race** has no comparable. It is never guessed at: it is reported in
the summary as needing a decision, and you point that race at a donor race in the settings.

### Template inheritance is checked before every write

A block named in `Configuration.TemplateFlags` has no values of its own — the bytes are dead and the
template supplies the real ones. Reads walk the chain to the record that owns the block; writes are
skipped and reported when the target does not own it.

This is **not** gated on the `UseTemplate` ACBS flag, and that matters. The live winner of
`EncDraugr01Template2H` `05B752:Skyrim.esm` carries `TemplateFlags = Stats, SpellList, …` and
`Template = EncDraugr01Template` while its `Flags` are only `Respawn` — and it still plays its
template's **300/80** rather than the **50/80** in its own bytes. Gating on the flag reads the record
as self-owned and writes stats that never apply.

## What it never touches

- **`Factions[]`** — read as evidence, never written, on any actor, for any reason.
- **`DefaultOutfit` and `Items`** — mods keep their visual identity. The actor's effective armour
  comes from the copied `REQ_Trait_Tempering_*` ability instead.
- **Anything inside the Requiem / 3BFTweaks stack.** Off-ladder values there are deliberate.
- **`MoreNastyCritters.esp`** — excluded by default.

## Combatant classification

Flags and AI dispositions are one Papyrus call from being cleared, so `Essential`, `Invulnerable`,
`IsGhost` and `Aggression` are never read as evidence. The decision is made from what a quest script
cannot practically rewrite: skills, class, combat style, perks, abilities, and carried gear.

The tie-break is asymmetric on purpose. Over-statting someone who never draws a weapon costs the
player nothing; under-statting someone who turns hostile in act three hands them a free boss kill. So
an ambiguous actor is treated as a combatant, always.

---

## Adding it to Synthesis

**You do not need to publish this to GitHub.** Synthesis runs local projects directly.

1. Open `D:\Wabbajack\Heretic\tools\Synthesis\Synthesis.exe`.
2. In the **Heretic - Synthesis Output** group, press **+** and choose **Solution** (not "Git
   Repository").
3. Point it at `D:\Mods\Code\RequiemAutoNPCPatcher3T\RequiemAutoNPCPatcher3T.sln` and select the
   `RequiemAutoNPCPatcher3T` project inside it.
4. Open the patcher's **Settings** tab and add the plugins you want rebalanced under
   **Mods to patch**. Nothing is patched until you do — there is no auto-detect mode.
5. Run the group. `RequiemAutoNPCPatcher3T.esp` lands in the Synthesis output.
6. **Then run the Reqtificator**, and make sure the patcher's ESP sits above
   `Requiem for the Indifferent.esp` in the load order.

Two environment notes:

- The profile pins **Mutagen 0.54.4 / Synthesis 0.36.6**, and the csproj matches. Those versions ship
  `net9.0` and `net10.0` only — **`net8.0` does not resolve**, so the project targets `net9.0`.
- `PipelineSettings.json` has `"BlockBuildingWithinMo2": true`. If Synthesis is launched through MO2 it
  will refuse to compile the patcher. Build it once outside MO2 (or run `dotnet build` on the solution
  yourself), or flip that setting.

## Settings worth knowing

| Setting | Default | Why |
|---|---|---|
| **Mods to patch** | *(empty)* | Explicit list. Nothing runs until it is filled. |
| **Also patch records these mods only override** | off | A target mod's override of a vanilla or Requiem actor is left to the stack. Turn on only for a mod that deliberately re-levels existing actors. |
| **Patch non-combatants** | off | Merchants and civilians are left alone. Ambiguous actors are patched regardless. |
| **Lowest / highest bandit rank** | 1 / 6 | Clamps the grid. |
| **Creature race donor overrides** | *(empty)* | The lever for custom monster races. Anything that needs one says so in the summary. |
| **Strip Giant Stomp from non-giant races** | on | `crGiantStomp` `02FFD2:Skyrim.esm` is a minor stagger in vanilla and an AoE knockdown under Requiem. Only races defined by the target mods are touched; Giants and Lurkers keep it. |
| **Log every patched actor** | off | On: one line per actor naming the donor. Skips, guesses and errors are always reported either way. |

## Verified against the live load order

Instance `D:\Wabbajack\Heretic`, profile `BottleRim`, 2026-08-17:

- all 54 bandit-template addresses resolve to the expected EditorIDs, and all 54 are uncontested
  (`winner = FTweaks.esp`, override depth 1);
- the rank grid's levels, pools, offsets, `TemplateFlags` and `Flags` match FTweaks' body exactly, and
  the nine classes carry the 5:0:5 / 4:0:6 / 3:0:7 / 6:0:4 stat weights the pool split needs;
- the template walker returns 300/80 for `EncDraugr01Template2H` and walks `EncGiant02` to
  `EncGiant01`, which are the two cases `npcs.md` §7.2 names as the trap.

Not verified in game — no Synthesis run has been made against the full VFS load order yet.
