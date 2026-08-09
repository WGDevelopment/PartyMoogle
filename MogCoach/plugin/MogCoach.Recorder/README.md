# MogCoach.Recorder (Dalamud plugin)

The **spatial/state spine** for MogCoach. Samples the FFXIV object table on the framework thread
(throttled to `SampleHz`, default 10 Hz) and writes a `.mogcap` capture: continuous positions,
headings, cast telegraphs, status timers, job gauge, HP, deaths, and pull start/end — the data
IINACT's action-sampled log can't provide.

**Observe-only.** It reads game state and writes a file. It never sends input to the game and never
automates play. Same class of access as any combat logger.

## Use

- `/mogrec start` — begin recording to a `.mogcap` (also auto-starts on combat if enabled)
- `/mogrec stop` — stop and flush
- `/mogrec status` — show state

Captures land in the plugin config directory (or `OutputDirectory` if set). Feed a `.mogcap` to
`mogcoach analyze --capture <file>.mogcap`.

## Build

Requires the Dalamud dev environment (`DALAMUD_HOME` or `~/.xlcore/dalamud/Hooks/dev`), exactly like
PartyMoogle — it is not buildable in a bare checkout. Uses `Dalamud.NET.Sdk` / `net10.0-windows`.

## Verify against your SDK

`Sampler.cs` is the only file that touches game state. Dalamud property names (`GameObjectId`,
`CastActionId`, `StatusList`, gauge types, …) are pinned to the SDK it builds against; if a name has
drifted on your SDK, fix it there. The heading→vector convention lives in
`MogCoach.Core.Geometry.Geometry.ForwardVector` — verify cones/lines aren't mirrored on real data.

See [`../../docs/SAMPLER.md`](../../docs/SAMPLER.md) for the capture format and the full v2 design.
