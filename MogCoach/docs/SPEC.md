# MogCoach — Technical Specification

An FFXIV post-session coaching tool. It takes the "record → transcribe → analyze →
recommend" shape of a Screenpipe workflow and rebuilds it for raid/dungeon coaching, using
**structured combat telemetry as the spine and screen frames as visual evidence**, with all
inference running on a **local vision-capable LLM**.

> **v2 update — read [`SAMPLER.md`](SAMPLER.md) first.** The design now centers a **custom Dalamud
> sampler plugin** (`plugin/MogCoach.Recorder`) that reads the object table every frame for
> *continuous* positions, headings, cast telegraphs, status timers, and job gauge — the data IINACT
> can't provide. That makes positioning, mechanics, movement tells, and buff/gauge analysis derivable
> from data (see the signal-ceiling table in SAMPLER.md). IINACT is kept as the action/damage event
> stream (optional cross-check). Sections below describe the original IINACT-first pipeline, which the
> sampler capture plugs into unchanged (both are just capture sources).

> Status: scaffold. Interfaces and data flow are implemented; several integration points are
> marked **VERIFY** / **TODO** because they depend on a live system (exact Screenpipe DB
> schema, IINACT director opcodes, the FFLogs benchmark query). See §14.

---

## 1. Goals

- Ingest a play session and produce a **per-pull coaching report**: rotation quality, death
  causes, and positioning feedback, benchmarked against trusted references.
- Run **fully local** — telemetry parsing, frame extraction, and both the text and vision LLM
  passes stay on the user's own hardware / tailnet. No gameplay data leaves the network.
- Be **robust across game patches**: lean on IINACT's parsed combat log (patch-maintained)
  rather than the native duty-recorder `.dat` format (breaks every major patch).

## 2. Non-goals & scope decisions (locked with user)

- **Post-session only.** No live, in-combat advice. This is a deliberate design and ToS
  posture: the tool observes and reports after the fact, like FFLogs — it never reads memory
  for real-time overlays or acts on the player's behalf.
- **Observe-only.** MogCoach never sends input to the game or automates play.
- **Audio is out of scope** for v1 (Screenpipe audio is disabled in the capture config), so no
  voice/callout coaching yet.
- **One model.** A single Qwen2.5-VL-7B endpoint serves both the text and vision passes; we do
  not run a separate text model (see §4).

## 3. Architecture

```
GAMING PC (Windows, Tailscale)                         VM108 "shared-services" (RTX 3060 12GB)
┌───────────────────────────────────┐                 ┌─────────────────────────────────────┐
│ FFXIV + Dalamud                    │                 │ llama.cpp llama-server (OpenAI API)  │
│  ├─ IINACT ──► LogLine WS          │                 │   model: Qwen2.5-VL-7B-Instruct-Q4   │
│  │     (mogcoach record)           │                 │   serves BOTH text + vision          │
│  │        └► captures/*.log        │                 └──────────────────▲──────────────────┘
│  └─ Screenpipe (1 fps, OCR)        │                                    │ tailnet (OpenAI-compatible)
│         └► ~/.screenpipe/db.sqlite │                                    │
└───────────────────────────────────┘                                    │
                    │  both artifacts share one wall clock                │
                    ▼                                                     │
      ┌──────────────────────────────── MogCoach.Cli `analyze` ─────────────────────────────┐
      │  Ingest            Analysis (per pull, sequential)             Output                 │
      │  ┌───────────┐     ┌──────────────────────────────────────┐   ┌──────────────────┐   │
      │  │ Telemetry │──►  │ 1. TelemetryAnalyzer (text)          │   │ Markdown / HTML  │   │
      │  │ source    │     │ 2. KeyframeSelector (deaths/wipe)    │──►│ report + evidence│   │
      │  │ +Segmenter│     │ 3. VisionAnalyzer (VLM on frames)   │   │ frames           │   │
      │  │ FrameStore│     │ 4. FindingFuser (merge, dedupe)     │   └──────────────────┘   │
      │  └───────────┘     └──────────────────────────────────────┘                          │
      │        ▲ references: JsonRotationProvider · FfLogsClient                              │
      └─────────────────────────────────────────────────────────────────────────────────────┘
```

**Data flow (analyze):**
1. `ITelemetrySource` streams normalised `CombatEvent`s from the capture `.log`.
2. `IEncounterSegmenter` splits them into `Pull`s (attempts).
3. For each pull, sequentially:
   - `TelemetryAnalyzer` computes objective `PullMetrics` and asks the LLM (text) for findings
     against the trusted rotation reference + FFLogs benchmark.
   - `IKeyframeSelector` picks the visual moments (deaths, wipe) and pulls the frame(s) around
     each from `IFrameStore`.
   - `VisionAnalyzer` runs the VLM on those frames only.
   - `FindingFuser` merges + dedupes into one `CoachingReport`.
4. `IReportRenderer` writes Markdown or HTML.

## 4. Deployment & model serving

- **Capture** runs on the gaming PC: Screenpipe (already deployed, 1 fps, OCR, audio off) plus
  `mogcoach record` tapping IINACT's WebSocket. Both write to disk on the same machine, so they
  share one wall clock — the join key between telemetry and frames (see §5).
- **Inference** runs on VM108 over the tailnet at an OpenAI-compatible endpoint.
- **Analysis** (`mogcoach analyze`) can run anywhere with tailnet reach to both the capture
  files and VM108 — on the gaming PC or on VM108 itself.

**Single-VLM decision.** The 3060 has 12 GB and shares ~1.5 GB with tdarr. Running two 7B
models (text + vision) does not fit comfortably and would force model swaps. Qwen2.5-VL-7B is a
capable text model *and* a vision model, so MogCoach uses it for both passes — one container,
one endpoint. The current `Qwen2.5-7B-Instruct` in the spec sheet is **text-only** and must be
replaced with the **VL** variant for the vision pass to work.

## 5. Capture format & time-sync

- **Telemetry capture** = an FFXIV network log: pipe-delimited lines, one event per line
  (`type|timestamp|...`). This is the format IINACT emits as `rawLine` and that ACT writes to
  its `Network*.log` files, so captures are interoperable with existing tooling. `mogcoach
  record` appends each `rawLine`; you can also point `analyze` at an ACT-exported log.
- **Frame capture** = Screenpipe's SQLite DB + mp4 chunks, untouched by MogCoach at capture
  time.
- **Time-sync**: every `CombatEvent` and every `Frame` carries an absolute `DateTimeOffset`.
  Because both recorders run on the same PC, no correlation handshake is needed. A residual skew
  correction is available via `Screenpipe:ClockOffsetSeconds` if ever required.

## 6. Telemetry ingest

- **`IinactLogLineParser`** lifts the coaching-relevant log types into `CombatEvent`:
  casts (20), abilities (21/22), deaths (25), status apply/remove (26/30), zone (01), director
  (33), chat (00). Unmodelled types are skipped, not errored.
- **Damage amounts are intentionally not decoded** from ability flags — that encoding is fiddly
  and error-prone. **Metrics source:** DPS/throughput numbers are meant to come from IINACT's
  aggregated encounter data (which computes per-actor rDPS directly), not from re-deriving them
  off log lines. The log gives the reliable timeline: casts, deaths, statuses. `ActualDps` is
  left null in the scaffold until the aggregated-data feed is wired (see §14).
- **`EncounterSegmenter`** prefers explicit director markers (start/end/wipe) and falls back to
  combat-activity + idle-gap detection, always breaking on zone changes.
- **Approximate metrics** computed locally today: GCD uptime and drift (from self-cast cadence,
  assuming a 2.5 s GCD) and death count. These are labelled "approx" everywhere and are handed
  to the LLM as authoritative so it never invents numbers.

## 7. Frame store

`ScreenpipeSqliteFrameStore` reads frame metadata + OCR text from `db.sqlite` and extracts the
frame image on demand from the recorded mp4 chunk via **ffmpeg**, caching PNGs.

- **VERIFY**: the SQL targets Screenpipe's `frames` / `video_chunks` / `ocr_text` schema; column
  names drift between versions. Confirm against your install and adjust the queries if needed.
- **Graceful degradation**: if ffmpeg is missing or extraction fails, the frame is returned with
  OCR text but no image, so the telemetry pass and OCR context still work — only that frame's
  vision analysis is skipped.

## 8. Keyframe strategy (telemetry drives vision)

The VLM never scans the whole recording. `KeyframeSelector` derives a small set of moments from
telemetry — every self death, party deaths (mechanics mode), and the wipe — capped at
`Analysis:MaxKeyframes` (default 6). For each, it pulls a short window of frames
(`KeyframeWindowBefore/AfterSeconds`) and keeps the `FramesPerKeyframe` nearest the moment. This
is the core cost control: vision work scales with *interesting moments*, not video length.

**Throughput budget.** VM108 does ~12–16 s per segment (~4 seg/min). At 6 keyframes × 2 frames,
a wipe-heavy pull is a few minutes of VLM time; a whole prog session is tens of minutes of batch
— fine for post-session review, unusable for anything live (another reason v1 is post-session).
The laptop fallback (~5 tok/s) is too slow for the vision pass; use it only for the text pass.

## 9. Analysis passes

- **Text (`TelemetryAnalyzer`)** — a compact pull digest (metrics, death timeline, self-cast
  timeline, rotation reference, benchmark) → LLM → findings JSON. Mode selects the system prompt
  (rotation / mechanics / awareness).
- **Vision (`VisionAnalyzer`)** — per keyframe: downscale frames (ImageSharp, longest edge
  `MaxImageEdge`), attach OCR text as context, prompt the VLM to read the *picture* for the
  visual cause telemetry can't see (position vs AoE/telegraph/markers) → findings JSON, each with
  a saved evidence image.
- **Fusion (`FindingFuser`)** — deterministic merge/dedupe (by category+title, keep highest
  severity), ordered by severity then time. No LLM call, so reports are stable and cheap.
- **Output contract**: both passes return the strict JSON in `Prompts.JsonContract`; the client
  also requests JSON mode. Parsing tolerates prose/code-fence wrapping and never fabricates
  numbers.

## 10. References

- **Rotations** (`JsonRotationProvider`): curated JSON per job (optionally per encounter) under
  `reference-data/rotations`. Lookup order: `{job}.{encounter}.json` → `{job}.json`. Populate
  these from a trusted source (e.g. The Balance) for the current patch. The bundled
  `warrior.json` is a **placeholder** schema demo, not authoritative rotation data.
- **Benchmarks** (`FfLogsClient`): FFLogs v2 GraphQL with OAuth2 client-credentials (token
  cached). **TODO**: FFLogs keys rankings by numeric encounter id (`FfLogs:EncounterIds` map)
  and the percentile→rDPS extraction depends on the chosen metric — finalise
  `BuildRankingsQuery`/`ParseBenchmark` against the current schema. Returns null (skips) when
  unconfigured or unmapped, so analysis still runs.

## 11. Report output

Markdown (default) or self-contained HTML, one document per session. Each pull shows: result,
job, duration, summary, a metrics table, and findings ordered by severity with a concrete "Fix"
line and any evidence frame inline.

## 12. Configuration reference

All in `appsettings.json`; secrets belong in `appsettings.Local.json` (git-ignored) or env vars
(`Section__Key`, e.g. `FfLogs__ClientSecret`, `Llm__ApiKey`).

| Section | Key | Meaning |
|---|---|---|
| `Llm` | `BaseUrl`, `Model`, `ApiKey`, `TimeoutSeconds`, `MaxTokens` | VLM endpoint (VM108) |
| `Screenpipe` | `DbPath`, `FfmpegPath`, `FrameCacheDir`, `ClockOffsetSeconds` | frame store |
| `Iinact` | `WebSocketUrl` | live capture recorder |
| `Segmenter` | `IdleGapSeconds`, `MinPullSeconds` | pull splitting |
| `References` | `RotationsDirectory` | rotation JSON location |
| `FfLogs` | `ClientId`, `ClientSecret`, `EncounterIds`, `Percentiles` | benchmarks |
| `Analysis` | `MaxKeyframes`, `FramesPerKeyframe`, window, `EvidenceDir`, `MaxImageEdge` | vision bounds |

## 13. CLI

```
mogcoach doctor  [--seconds 8]     check setup: Screenpipe DB+schema, ffmpeg, IINACT WS, LLM (text+vision)
mogcoach record  [--out captures/session.log]
mogcoach analyze --capture <path> [--mode rotation|mechanics|awareness]
                 [--job Warrior] [--player "Name"] [--pull pull-03]
                 [--no-vision] [--format md|html] [--out report.md]
```

`doctor` is the practical way to work through §14 items 1–2 on the live system — it introspects the
Screenpipe schema and probes the IINACT WebSocket for real events. See `docs/SETUP.md`.

## 14. Open items to verify against the live system

1. **Screenpipe schema** (§7) — confirm `frames`/`video_chunks`/`ocr_text` columns; adjust SQL.
2. **IINACT WebSocket** — confirm the OverlayPlugin WS URL/port and the `LogLine` subscription
   handshake for your IINACT build.
3. **Director opcodes** (`IinactLogLineParser.DirectorCommand`) — the type-33 start/wipe/complete
   command codes vary by content; validate on a real capture.
4. **FFLogs benchmark query** (§10) — finalise the GraphQL + percentile extraction and fill the
   `EncounterIds` map.
5. **Aggregated DPS feed** — decide how to capture IINACT's per-actor rDPS (CombatData snapshot)
   alongside the log to populate `PullMetrics.ActualDps` and real percentile estimates.
6. **Model swap** — deploy `Qwen2.5-VL-7B-Instruct-Q4` on VM108 and point `Llm:Model` at it.

## 15. Roadmap

- **Phase 1 (this scaffold)** — capture recorder, telemetry ingest + segmentation, text
  analysis, keyframe-driven vision, fusion, reports, CLI.
- **Phase 2** — aggregated DPS ingest + real FFLogs percentiles; curated rotation library;
  per-encounter references.
- **Phase 3** — session-level trends across pulls; encounter-timeline alignment (mechanic
  windows); optional audio/callout coaching if capture audio is enabled.

## 16. Build & test

Requires the .NET 9 SDK.

```
cd MogCoach
dotnet restore
dotnet build
dotnet test
dotnet run --project src/MogCoach.Cli -- analyze --capture samples/sample-capture.log --no-vision
```

## 17. Project layout

```
MogCoach/
  src/
    MogCoach.Core        models, geometry (AoE shapes), abstractions (no deps)
    MogCoach.Ingest      capture sources (sampler .mogcap + IINACT .log), segmenter,
                         Screenpipe frame store, diagnostics
    MogCoach.Llm         OpenAI-compatible text+vision client
    MogCoach.References  rotation JSON, AoE-shape DB, FFLogs client
    MogCoach.Analysis    pipeline (telemetry, positional, keyframe, vision, fusion) + renderers
    MogCoach.Cli         host, DI, config, `doctor` / `record` / `analyze`
  plugin/MogCoach.Recorder   Dalamud sampler plugin (the spatial/state spine) — builds vs Dalamud SDK
  tests/MogCoach.Tests   segmenter + log-parser unit tests
  reference-data/rotations   curated rotation references (placeholder included)
  reference-data/aoe         ability-id → AoE geometry DB (placeholder included)
  samples/               tiny sample captures (.mogcap and .log) for smoke runs
  docs/  SPEC.md · SAMPLER.md (v2 architecture) · SETUP.md
```
