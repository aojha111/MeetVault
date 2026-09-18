# MeetVault — Local Meeting Knowledge Vault

MeetVault turns meeting recordings into a searchable, date-organized knowledge base — **entirely on your machine**. It extracts audio, transcribes it locally (whisper.cpp), analyzes it with a local LLM (llama.cpp + GGUF), generates a concise spoken meeting brief with offline TTS, and lets you play that brief inside the app.

No cloud services. No external AI APIs. No telemetry with meeting content.

## Features

- **Import** MP4/MKV/MOV/AVI/WebM recordings and common audio formats via drag-and-drop friendly *Add Meeting* dialog or the CLI.
- **Local pipeline**: FFmpeg audio extraction → whisper.cpp transcription → llama.cpp structured analysis → offline TTS audio brief.
- **Structured meeting intelligence**: summary, agenda, key discussion points, decisions, action items with owners/deadlines, risks, open questions, topics, participants, entities.
- **Concise spoken brief** (30–120 s, configurable) generated from the analysis and playable in-app (play/pause/seek/speed/volume/restart).
- **Date-wise organization** (All / Today / Yesterday / This Week / This Month) and **global search** across titles, transcripts, decisions, actions, questions, risks and people (SQLite FTS5).
- **Cross-meeting links**: related meetings surfaced via shared topics/entities.
- **Model Manager**: download just the packs you need, SHA-256-verified. The app itself never bundles model weights.
- **Stage-cached processing**: a failed TTS stage retries without re-transcribing.
- **Export**: Markdown minutes, JSON analysis, plain-text transcript.
- **Offline Mode**: one checkbox disables all non-essential network access.

## Project layout

```
MeetVault.slnx
├── src/
│   ├── MeetVault.Core/            domain logic: pipeline, prompts, merger, chunker, search, export
│   ├── MeetVault.Infrastructure/  SQLite, FFmpeg, whisper.cpp, llama.cpp, TTS, model manager
│   ├── MeetVault.App/             WPF desktop UI (main window, player, model manager)
│   └── MeetVault.Cli/             headless CLI for automation and power users
└── tests/MeetVault.Tests/         unit + integration tests (SQLite, pipeline with fakes)
```

## Building

Prerequisites: **.NET 10 SDK** (Windows 10/11 x64).

```bash
dotnet build MeetVault.slnx
dotnet test MeetVault.slnx
```

Run the desktop app:

```bash
dotnet run --project src/MeetVault.App
```

## Data layout (portable)

All data lives under one root, resolved next to the exe when writable, else `%LOCALAPPDATA%\MeetVault`:

```
<root>/
├── runtime/    ffmpeg, whisper, llama, piper executables (installed via Model Manager)
├── models/     whisper/, llm/, tts/ weights (downloaded separately)
├── data/       meetings.db, meetings/YYYY/MM/DD/<id>/, cache/
├── config/     appsettings.json, model-registry.json
└── logs/       rolling local logs (never transcript content)
```

## Model Manager (CPU-only, works on 16 GB RAM)

Every pack in the catalog runs on **CPU only — no GPU is required at any point**. The Model Manager shows your detected hardware and a one-click **Install recommended set** button that downloads everything your machine needs.

1. Launch the app and click **Models**.
2. Click **Install recommended set** (or pick packs individually — see the table below).
3. Windows built-in voices (SAPI) are used for TTS by default — no download needed. Optionally install a Piper voice pack for neural TTS.
4. Select which model is active per category with **Use**.
5. **Offline Mode** blocks all downloads; installed packs keep working.

Downloads are resumable and SHA-256-verified before anything is installed. Only registry-declared executables are ever executed.

### CPU-friendly pack tiers

| Pack | RAM tier | Download | Notes |
| --- | --- | --- | --- |
| FFmpeg runtime | any | 106 MB | Required for every import |
| whisper.cpp runtime (CPU) | any | 8 MB | CPU-only build; no CUDA/Vulkan needed |
| llama.cpp runtime (CPU) | any | 14 MB | CPU-only llama-server |
| Piper TTS runtime | any | 21 MB | Optional; SAPI voices work without it |
| Whisper **Tiny** (q5_1) | 4 GB | 31 MB | Fastest transcription |
| Whisper **Base** (q5_1) | 4 GB | 57 MB | **Recommended default** |
| Whisper **Small** (q5_1) | 8 GB | 181 MB | Best accuracy of the lightweight tiers |
| Silero VAD | 1 GB | 0.9 MB | Skips silence; recommended |
| Qwen2.5 **0.5B** Instruct | 4 GB | 469 MB | Runs on almost any CPU |
| Qwen2.5 **1.5B** Instruct | 8 GB | 1.1 GB | Balanced summaries/actions |
| Qwen2.5 **3B** Instruct | 16 GB | 2.0 GB | **Recommended default**; best quality that stays CPU-comfortable |
| Piper voice: en_US lessac | 2 GB | 60 MB | Optional neural TTS voice |

First-run defaults and the recommendation engine pick tiers automatically from detected RAM and CPU cores (see `HardwareInfo` in `MeetVault.Core`).

## Standalone builds and installer

```bash
publish.bat              # one-shot: test + build + push + GitHub release with EXEs
scripts\publish.cmd      # builds dist\portable\ + zip + dist\single-file\ EXEs
scripts\installer.cmd    # wraps the portable layout into dist\MeetVault-<v>-setup-win-x64.exe
```

`publish.bat [version]` runs the test suite, builds every artifact, commits/pushes the source to `github.com/aojha111/MeetVault`, and publishes a `v<version>` release with the portable zip, setup EXE and single-file EXEs attached. On first run it auto-installs the `gh` CLI per-user and asks you to sign in via browser; later runs skip straight through.

Produces in `dist\`:

- `portable\MeetVault\` — self-contained folder (no .NET install needed); run `MeetVault.exe`.
- `MeetVault-<version>-portable-win-x64.zip` — the same folder, zipped.
- `single-file\MeetVault.exe` / `single-file\mvault.exe` — single-file app/CLI EXEs.
- `MeetVault-<version>-setup-win-x64.exe` — per-user installer (no admin required), installs to `%LOCALAPPDATA%\Programs\MeetVault`.

No AI model weights are ever bundled: every artifact downloads and SHA-256-verifies its packs from inside the app on first run, so a fresh install stays small (~50–140 MB) and models stay verifiable.

## CLI

```text
mvault status                             show tool/model availability and meeting count
MeetVault.Cli packs                       list available model packs
mvault install <packId>                   download + verify + install a pack
mvault remove <packId>
mvault import <file> [--title T] [--date yyyy-MM-dd] [--time HH:mm] [--no-process]
mvault process <meetingId> [--force]
mvault list
mvault search <query>
```

All commands accept `--root <path>` to operate on a portable vault directory.

## Privacy

- Every processing step runs locally; no meeting content ever leaves the machine.
- Network access happens only when you explicitly download/update packs in Model Manager.
- **Offline Mode** disables even that.
- Logs contain pipeline status only, never transcript content.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| "FFmpeg runtime is not installed" | Install the FFmpeg pack in Model Manager → Runtimes. |
| "No transcription model is installed" | Install a Whisper pack in Model Manager. |
| "No analysis LLM is installed" | Install an LLM pack — 3B instruct fits 16 GB RAM; 0.5B/1.5B for less. |
| Processing failed at audio generation | Press **Retry** — earlier stages are cached and will not repeat. |
| Search returns nothing for known words | Ensure the meeting's analysis/transcription stage completed. |
| App data location | Run `mvault status` — it prints the active root. |
