# MogCoach — Setup (from scratch)

You're building all of this new. Do the steps in order; each ends with a `mogcoach doctor`
check that should flip from FAIL/WARN to OK. `doctor` is your progress meter — run it as often as
you like.

Topology (from your spec sheet): the **gaming PC** (Windows) runs FFXIV + Dalamud/IINACT +
Screenpipe; **VM108** (`shared-services`, RTX 3060) runs the LLM; everything talks over the
**Tailscale** tailnet. The MogCoach `analyze` app can run on either box.

---

## 0. Build MogCoach first (so `doctor` exists)

On whichever machine will run analysis (needs the **.NET 9 SDK**):

```bash
cd MogCoach
dotnet build
dotnet run --project src/MogCoach.Cli -- doctor
```

Everything will FAIL at first — expected. Now knock them down one by one.

Put machine-specific settings in `src/MogCoach.Cli/appsettings.Local.json` (git-ignored); it
overrides `appsettings.json`. Create it as you go.

---

## 1. LLM backend on VM108 — swap in the **vision** model

Your current container serves `Qwen2.5-7B-Instruct`, which is **text-only**. The vision pass needs
the **VL** variant. Stop the old container (frees the port + VRAM) and run the VL model under
llama.cpp with its vision projector (`--mmproj`).

```bash
docker stop qwen-llm            # free port 11434 + VRAM

# Download the GGUF + mmproj (verify current filenames on the model repo, e.g.
# ggml-org/Qwen2.5-VL-7B-Instruct-GGUF) into /opt/models on VM108, then:
docker run -d --name qwen-vl --restart unless-stopped --runtime=nvidia --gpus all \
  -p 11434:8080 -v /opt/models:/models \
  ghcr.io/ggml-org/llama.cpp:server-cuda \
  -m /models/Qwen2.5-VL-7B-Instruct-Q4_K_M.gguf \
  --mmproj /models/mmproj-Qwen2.5-VL-7B-Instruct-f16.gguf \
  --host 0.0.0.0 --port 8080 -ngl 99 --ctx-size 8192
```

VRAM: 7B Q4 (~5 GB) + vision projector + KV fits the 12 GB 3060 alongside tdarr's ~1.5 GB floor.
Keep `--ctx-size` modest (8192) to leave headroom.

Set in `appsettings.Local.json`:
```json
{ "Llm": { "BaseUrl": "http://shared-services:11434", "Model": "qwen2.5-vl-7b-instruct" } }
```
If you bearer-gate the endpoint, also set `"ApiKey"` (or env `Llm__ApiKey`).

**Check:** `mogcoach doctor` → *LLM /models*, *LLM text*, and *LLM vision* should all be OK. If
*LLM vision* fails but text passes, the mmproj didn't load (text-only model still running).

---

## 2. IINACT in FFXIV (telemetry source)

1. In-game, open Dalamud (`/xlplugins`) and install **IINACT**.
2. In IINACT's settings, enable the **WebSocket / OverlayPlugin server**. Note the port
   (default `10501`).
3. If MogCoach runs on a *different* machine than the game, make sure the port is reachable over
   the tailnet and set the host accordingly.

Set (only if not default):
```json
{ "Iinact": { "WebSocketUrl": "ws://127.0.0.1:10501/ws" } }
```

**Check:** launch a duty (or hit a striking dummy), then `mogcoach doctor --seconds 10` → *IINACT
WebSocket* OK with a sample line. "Connected but no LogLine events" just means nothing was
happening — retry during combat.

---

## 3. Screenpipe (frame source) + ffmpeg

You already run Screenpipe (1 fps, OCR, audio off) — that's the intended config. MogCoach reads its
DB directly and extracts frames with **ffmpeg**, so:

1. Install **ffmpeg** and ensure it's on PATH (or set `Screenpipe:FfmpegPath` to the full path).
2. Point MogCoach at the DB (default `~/.screenpipe/db.sqlite`):

```json
{ "Screenpipe": { "DbPath": "C:\\Users\\<you>\\.screenpipe\\db.sqlite", "FfmpegPath": "ffmpeg" } }
```

**Check:** `mogcoach doctor` → *Screenpipe DB* OK (shows frame count + latest timestamp) and
*ffmpeg* OK. If the schema check fails, your Screenpipe version renamed columns — fix the SQL in
`ScreenpipeSqliteFrameStore.cs` (doctor tells you which column).

**Optional (mouse-pointer hint):** the screenshot review can note when the cursor is sitting on your
hotbars (a weak hint you might be mouse-clicking skills). This only works if the screen capture
includes the cursor — make sure cursor capture is enabled in Screenpipe. It's an unreliable,
one-frame-a-second signal by nature; treat it as something to tune, not trust.

---

## 4. First run

```bash
# Record a session on the gaming PC while you play (Ctrl+C to stop). Screenpipe runs on its own.
mogcoach record --out captures/tonight.log

# Analyze it.
mogcoach analyze --capture captures/tonight.log --job Warrior --mode mechanics \
  --format html --out reports/tonight.html
```

Open the HTML report. Deaths and rotation come from telemetry; positioning findings come from the
VLM on the death/wipe keyframes.

---

## 5. Optional — better recommendations

- **AoE shape DB** (unlocks "stood in the AoE" findings): draft it from a local BossMod checkout with
  `mogcoach import-aoe --bossmod <path-to-ffxiv_bossmod>`, then review `reference-data/aoe/shapes.json`
  and the generated `shapes.unbound.json`. Or hand-author entries for the fight you're progging.
- **Rotation references**: drop curated JSON per job in `reference-data/rotations/` (see
  `warrior.json` for the schema; replace the placeholder with a real reference for your patch).
- **FFLogs benchmarks**: create an API client at <https://www.fflogs.com/api/clients/>, set
  `FfLogs:ClientId`/`ClientSecret` (env `FfLogs__ClientSecret`), and fill `FfLogs:EncounterIds`.
  See `docs/SPEC.md §10/§14` — the ranking query still needs finalizing.

---

## Setup checklist

- [ ] `dotnet build` succeeds
- [ ] VL model serving on VM108 → *LLM /models / text / vision* OK
- [ ] IINACT WS reachable, events flowing in combat → *IINACT WebSocket* OK
- [ ] Screenpipe DB + ffmpeg → *Screenpipe DB / ffmpeg* OK
- [ ] `record` produces a non-empty `.log`
- [ ] `analyze` produces a report with findings
