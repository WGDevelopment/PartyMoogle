# MogCoach — Get It Running (from zero)

This is the copy-paste path from "nothing installed" to "reading a coaching report." No build
knowledge assumed. Commands are for **Windows PowerShell** on your gaming PC, plus one Linux block
for the AI box (VM108).

**What you'll end up with:** an in-game add-on that records your fights, and an app that turns each
recording into a report of what to fix.

---

## Where each piece runs

- **Gaming PC (Windows):** the game, Screenpipe, the recorder add-on, the analyzer app, and the
  reports — everything except the AI model.
- **VM108 (`shared-services`):** the local AI model.
- They talk over your Tailscale network (already set up).

You'll do almost everything on the **gaming PC**. VM108 is just one command (Part B).

---

## Part A — Gaming PC: one-time install

**1. Install the tools.** Open PowerShell and run:

```powershell
winget install Git.Git
winget install Microsoft.DotNet.SDK.9
winget install Gyan.FFmpeg
```

Then **close and reopen PowerShell** (so the new tools are on your PATH). Sanity check:

```powershell
git --version ; dotnet --version ; ffmpeg -version
```

Each should print a version. (You already have the Dalamud build setup from PartyMoogle, which the
add-on reuses — nothing extra to install for that.)

**2. Get the code.**

```powershell
cd $HOME
git clone https://github.com/WGDevelopment/PartyMoogle.git
cd PartyMoogle
git checkout claude/ff14-coaching-screenpipe-5b0b9e
cd MogCoach
```

Everything below is run from this `MogCoach` folder.

**3. Build the analyzer app.**

```powershell
dotnet build MogCoach.sln -c Release
```

First build downloads dependencies and takes a minute. If it complains about a missing .NET
version, install the exact one it names and re-run.

**4. Build and install the in-game recorder add-on.**

```powershell
dotnet build plugin\MogCoach.Recorder\MogCoach.Recorder.csproj -c Release

$dst = "$env:AppData\XIVLauncher\devPlugins\MogCoachRecorder"
New-Item -ItemType Directory -Force -Path $dst | Out-Null
Copy-Item plugin\MogCoach.Recorder\bin\Release\* $dst -Recurse -Force
```

Then **restart the game** (or reload Dalamud). Open the plugin list with `/xlplugins` → **Dev
Tools / Installed** and confirm **MogCoach Recorder** is enabled.

---

## Part B — VM108: start the AI model (one-time)

On VM108 (Linux). This swaps in the vision-capable model. Download the model files first (see
`docs/SETUP.md §1` for the exact filenames), then:

```bash
docker stop qwen-llm   # free the port + VRAM from the old text-only model

docker run -d --name qwen-vl --restart unless-stopped --runtime=nvidia --gpus all \
  -p 11434:8080 -v /opt/models:/models \
  ghcr.io/ggml-org/llama.cpp:server-cuda \
  -m /models/Qwen2.5-VL-7B-Instruct-Q4_K_M.gguf \
  --mmproj /models/mmproj-Qwen2.5-VL-7B-Instruct-f16.gguf \
  --host 0.0.0.0 --port 8080 -ngl 99 --ctx-size 8192
```

---

## Part C — Configure (gaming PC)

Open `src\MogCoach.Cli\appsettings.json` in Notepad and set:

- `Llm.BaseUrl` → `http://shared-services:11434` (your model box)
- `Llm.Model` → the VL model name you loaded, e.g. `qwen2.5-vl-7b-instruct`
- `Screenpipe.DbPath` → `C:\Users\<YOU>\.screenpipe\db.sqlite` (your Windows username)

Optional (for the "your FFLogs numbers" part): fill `FfLogs.ClientId/ClientSecret`,
`CharacterName/Server/Region`, `ZoneId`, and `EncounterIds`.

```powershell
notepad src\MogCoach.Cli\appsettings.json
```

---

## Part D — Check everything is connected

```powershell
dotnet run --project src/MogCoach.Cli -- doctor
```

This prints a checklist — Screenpipe, ffmpeg, the in-game recorder connection, and the AI model.
Anything red tells you exactly what's not wired yet. Fix those, re-run until it's green. (The
in-game check needs you to be **in a duty/combat** to see events.)

---

## Part E — Every session after that

**1. Play.** The add-on auto-records when you're in combat. (Or control it manually in-game:
`/mogrec start`, `/mogrec stop`, `/mogrec status`.)

**2. Turn the latest recording into a report:**

```powershell
$cap = Get-ChildItem "$env:AppData\XIVLauncher\pluginConfigs\MogCoachRecorder\*.mogcap" |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1

dotnet run --project src/MogCoach.Cli -- analyze `
  --capture $cap.FullName --job Warrior --mode mechanics --format html --out reports\latest.html
```

Change `--job` to your job and `--mode` to `rotation`, `mechanics`, or `awareness`.
(If that folder name differs, look under `%AppData%\XIVLauncher\pluginConfigs\` for the `.mogcap`.)

**3. Read it:**

```powershell
start reports\latest.html
```

---

## Kick the tires without any of the above

Want to see a report render before you've set anything up? Run the bundled sample with `--no-llm`
(deterministic only — no game, model, or recording needed):

```powershell
dotnet run --project src/MogCoach.Cli -- analyze --capture samples\sample-session.mogcap --no-llm --out reports\sample.html
start reports\sample.html
```

You should get a **"Stood in AoE: Ground Circle"** finding (the geometry check) and a **"Low uptime:
Surging Tempest"** finding — both computed with no AI. (`--no-vision` skips only the screenshot pass;
`--no-llm` skips all AI, which is what makes this run need nothing.)

---

## If something breaks

- `doctor` is your first stop — it names what's missing.
- If a build fails, or the report looks wrong, copy the error text and send it over — most
  first-run issues are a config path or a game-version name that needs a one-line fix.
