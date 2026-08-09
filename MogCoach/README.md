# MogCoach

An FFXIV **post-session coaching** tool: record a session, turn it into a structured combat
timeline, analyze it with a local vision-capable LLM, and get a per-pull report of what to fix —
rotation, deaths, and positioning.

It reuses the "record → transcribe → analyze → recommend" pattern of a Screenpipe workflow. A
**custom Dalamud sampler plugin** reads the object table every frame (positions, headings, cast
telegraphs, status timers, gauge) as the spine; **IINACT** supplies the action/damage event stream;
**screen frames** are visual evidence. All inference runs **locally** on a Qwen2.5-VL model.

> **Just want it running?** → **[`docs/GETTING_STARTED.md`](docs/GETTING_STARTED.md)** is the
> copy-paste, zero-to-report quickstart.
>
> **Architecture:** read **[`docs/SAMPLER.md`](docs/SAMPLER.md)** — it explains the sampler spine and,
> importantly, **how much of coaching is derivable from data alone** (a per-dimension table).
> **[`docs/SETUP.md`](docs/SETUP.md)** is the detailed setup; [`docs/SPEC.md`](docs/SPEC.md) is the full design.

## How it works

```
IINACT combat log  ─┐
                    ├─►  segment into pulls ─►  text analysis (rotation, deaths, DPS)
Screenpipe frames  ─┘                           keyframe select (deaths/wipe) ─► vision analysis
                                                fuse ─► Markdown / HTML report
```

Telemetry decides *when* something went wrong; the VLM looks only at those few frames to explain
the *visual* cause. That keeps vision cost bounded (see SPEC §8).

## Prerequisites

- **.NET 9 SDK**
- **IINACT** (Dalamud plugin) running in FFXIV — the telemetry source.
- **Screenpipe** capturing the game (1 fps is fine) — the frame source. **ffmpeg** on PATH for
  frame extraction.
- **Qwen2.5-VL-7B** served at an OpenAI-compatible endpoint (e.g. llama.cpp on VM108). Note: the
  vision *(VL)* variant, not the text-only `Instruct`.
- *(optional)* An **FFLogs v2** API client for DPS benchmarks.

## Quick start

```bash
cd MogCoach
dotnet build

# 0) Check what's wired up (run after each setup step; see docs/SETUP.md).
dotnet run --project src/MogCoach.Cli -- doctor

# 1) Capture a session on the gaming PC. Two capture sources:
#    - Sampler (spatial/state spine): the MogCoach.Recorder Dalamud plugin — /mogrec in-game → .mogcap
#    - IINACT (action/damage stream, optional cross-check): the CLI recorder → .log
dotnet run --project src/MogCoach.Cli -- record --out captures/tonight.log   # IINACT WS

# 2) Analyze a capture (source auto-detected by extension: .mogcap or .log). Telemetry + positional + vision.
dotnet run --project src/MogCoach.Cli -- analyze \
  --capture captures/tonight.mogcap --job Warrior --mode mechanics --format html \
  --out reports/tonight.html

# Smoke tests against the bundled samples (no LLM/frames needed with --no-vision):
dotnet run --project src/MogCoach.Cli -- analyze --capture samples/sample-session.mogcap --no-vision
dotnet run --project src/MogCoach.Cli -- analyze --capture samples/sample-capture.log --no-vision
```

The Dalamud sampler plugin lives in [`plugin/MogCoach.Recorder`](plugin/MogCoach.Recorder) and builds
against the Dalamud SDK (like PartyMoogle), separately from this .NET 9 solution.

Configure endpoints in `src/MogCoach.Cli/appsettings.json`; put secrets (`Llm:ApiKey`,
`FfLogs:ClientSecret`) in `appsettings.Local.json` or env vars (`FfLogs__ClientSecret`).

## Layout

`Core` (models/interfaces) · `Ingest` (IINACT + Screenpipe) · `Llm` (VLM client) · `References`
(rotations + FFLogs) · `Analysis` (pipeline + reports) · `Cli`. Details in `docs/SPEC.md §17`.

## Status

Scaffold — the pipeline is wired end to end; a few integration points depend on the live system
and are marked **VERIFY/TODO** in the code and in `docs/SPEC.md §14`.
