# MogCoach — v2 architecture: the Dalamud sampler spine

This supersedes the "IINACT is the whole spine" framing in the original SPEC. The insight: IINACT's
combat log is an **action-sampled event stream** — positions only exist at the moment you press a
button. A purpose-built **Dalamud plugin reading the object table every frame** gives *continuous*
positions, headings, cast bars, status timers, and job gauge. That converts positioning, mechanics,
movement, and buff/gauge analysis from vision-guesswork into geometry on data.

## Signal ceiling — how much comes from data alone

| Coaching dimension | Data-only? | Primary source |
|---|---|---|
| Timing (everything ms-stamped) | ~100% | either |
| Skill rotation (sequence vs reference) | ~100% | action stream |
| Buff timing / uptime / burst alignment | ~100% | status stream |
| Rotational failures (drift, drops, procs, overcap) | ~95% | action stream + **gauge (sampler)** |
| Positions (continuous XYZ + heading) | ~95% | **sampler** |
| Mechanics (who got hit, tethers, markers) | ~80% | sampler + encounter model |
| Visual signal vs placement (in the danger zone?) | ~70% | **sampler + AoE-shape DB**; vision confirms telegraph visibility |
| Mouse vs keyboard | ~30% | movement tells (backpedal/keyboard-turn) from **sampler**; ability-click ≈ unobservable |

The only near-unobservable item is *how* a skill was activated (mouse-click vs keybind). Everything
else is largely-to-fully derivable — once the sampler exists.

## The hybrid (recommended)

```
┌─ Sampler plugin (NEW, spatial/state spine) ─┐   ┌─ IINACT (action/damage event stream) ─┐
│ object table @ ~10 Hz:                      │   │ casts, damage, deaths, status applies │
│  positions, heading, velocity              │   │ — the patch-maintained hard part;     │
│  cast state (telegraphs), status timers,   │   │   reinventing it is wasteful, so we   │
│  job gauge, HP, combat flag, deaths        │   │   keep it as the rotation source      │
└──────────────────────┬──────────────────────┘   └──────────────────┬────────────────────┘
                       │  one wall clock (both on the gaming PC)      │
                       ▼                                              ▼
             capture: sampler .mogcap (JSONL)          capture: IINACT network .log (optional)
                       │                                              │
                       └──────────────► MogCoach analyze ◄───────────┘
                          segment pulls · rotation (actions) · positional/mechanics (snapshots
                          + AoE geometry) · movement tells · buff/gauge · keyframe→VLM vision
```

Why keep IINACT rather than fully replace it: the reliable action/damage event is the **ActionEffect
network packet**, which IINACT/ACT already hook and maintain across patches. The sampler *can* capture
actions too (optional `UseAction` hook, §"Action capture"), making IINACT fully optional — but that
duplicates maintained work. Default: sampler for space/state, IINACT for actions. Both fuse on one clock.

## Sampler design

- **Rate:** driven by `IFramework.Update`, throttled to `SampleHz` (default 10 Hz). Per-frame is
  available but 10 Hz is ample for movement/positioning and keeps captures small.
- **Snapshot (periodic):** for every relevant actor (self, party, nearby enemies) — id, name, kind,
  x/z, heading, hp, current cast (action id + elapsed/total + target), current target. Plus, for
  self: job gauge and full status list (id, remaining, stacks, source).
- **Events (edge-triggered):** combat enter/leave (pull start/end), death, zone change. Cast
  start/end and status gain/lose are *derivable* from consecutive snapshots, so they aren't
  separately emitted (keeps the writer simple).
- **Not captured in v1:** head markers and tethers (they arrive as network packets, not object-table
  state) — get these from IINACT for now. Marked TODO.

## Capture format (`.mogcap`, JSONL)

One JSON object per line. Every record has `k` (kind) and `ts` (ISO-8601 UTC — the join key to
Screenpipe frames). Line 1 is the header.

```jsonc
{"k":"hdr","ver":1,"ts":"…","player":"Hero Adventurer","playerId":"10001234","job":"Warrior","sampleHz":10}
{"k":"snap","ts":"…","actors":[
   {"id":"10001234","name":"Hero Adventurer","kind":"self","x":100.2,"z":95.1,"h":1.57,"hp":74000,"hpm":74000,
    "cast":null,"tgt":"400009C0",
    "st":[{"id":"2F5","name":"Surging Tempest","rem":48.2,"stk":0,"src":"10001234"}],
    "gauge":{"beastGauge":80}},
   {"id":"400009C0","name":"Some Boss","kind":"enemy","x":100.0,"z":100.0,"h":3.14,"hp":5.0e6,"hpm":9.9e6,
    "cast":{"id":"2A3F","name":"Exaflare","el":2.1,"tot":5.0,"tgt":"0"},"tgt":"10001234"}
]}
{"k":"evt","ts":"…","type":"Death","id":"10001234","name":"Hero Adventurer"}
{"k":"evt","ts":"…","type":"PullEnd","cleared":false}
```

`analyze` reads this via `SamplerCaptureSource`: `evt` records map to `CombatEvent`s (so the existing
segmenter/rotation path works unchanged) and `snap` records become `WorldSnapshot`s for the positional
passes.

## Action capture (optional — makes IINACT unnecessary)

To drop IINACT entirely, the sampler needs the instant/oGCD actions that don't show as cast bars. That
means hooking the game's action path (`ActionManager.UseAction` or the ActionEffect handler) via
`IGameInteropProvider`. This is powerful but **signature-fragile** (must be re-verified each patch),
which is exactly why we default to IINACT. A hook is scaffolded but off by default; enable it only if
you accept maintaining the signature.

## Seeding the AoE shape DB

`shapes.json` (ability-id → geometry) is what turns "were you in the AoE" into a computation, and it's
the main authoring cost. Two ways to fill it:

- **`mogcoach import-aoe --bossmod <path>`** — best-effort scrape of a local [BossMod](https://github.com/awgil/ffxiv_bossmod)
  source tree. BossMod defines shapes in component code (not a table), so only shapes with a nearby
  `AID.Name` are auto-bound; the rest land in `shapes.unbound.json` with file/line/context for manual
  curation. Treat the output as a **reviewable draft** — hand-edits are preserved on re-import.
- **By hand**, per fight you're progging — a handful of entries covers the mechanics that actually kill
  you. See `reference-data/aoe/shapes.json` for the schema.

Shape mapping from BossMod: Circle→Circle, Donut→Donut, Cone→Cone (half-angle°), Rect(front,halfW,back)
→Line (length=front; back dropped), Cross→Cross.

## Job gauge coverage

The sampler records job gauge per snapshot (overcap/resource analysis). Gauges are implemented for most
jobs; the reworked/newest ones (AST, SMN, VPR, PCT) are TODO and recorded as null until their gauge
members are confirmed against the SDK. Gauge member names are the most SDK-version-sensitive part of the
plugin — a wrong name is a compile error; fix it in `Sampler.BuildGauge`.

## ToS / altitude

Read-only, post-session, no overlay, no automation, never sends input to the game. This is the same
class of access as any combat logger (ACT/IINACT) and the read-only side of plugins like BossMod. The
line we don't cross — real-time automated advice or acting for the player — stays uncrossed.
```
