# Meeting Knowledge Analyzer — Master Build Specification

## 1. Product Goal

Build a lightweight, Windows-first, privacy-first desktop application that takes meeting recordings (MP4/MKV/MOV/AVI/WebM and common audio formats), analyzes them completely locally, and maintains a date-wise searchable meeting knowledge base.

The app must:

1. Import recorded meetings.
2. Extract audio locally.
3. Transcribe speech locally with timestamps.
4. Optionally identify speakers when the required local speaker model is installed.
5. Analyze the transcript with a local LLM.
6. Extract concise, structured meeting intelligence:
   - agenda/topics
   - executive summary
   - key discussion points
   - decisions
   - action items
   - owners
   - deadlines
   - risks/blockers
   - open questions
   - follow-ups
7. Generate a very concise spoken meeting brief from the analyzed result.
8. Generate that brief as a local audio file using an offline TTS engine.
9. Play the generated audio directly inside the application.
10. Organize meetings by date and support search across meetings.
11. Link related information across multiple meetings.
12. Keep recordings, transcripts, analysis, metadata, and generated audio entirely local.
13. Keep the application itself small; AI model files must be downloadable separately and must not be bundled into the normal application installer.

## 2. Core Product Principle

The application is the product. AI models are optional runtime assets.

The installer must NOT contain large AI model weights.

The application must be useful without models for browsing, searching, editing metadata, and viewing already-generated meeting results.

When AI functionality is needed on a new laptop, the application must provide an in-app Model Manager that downloads the required model packs.

No mandatory cloud account. No mandatory API key. No mandatory internet connection after runtime/model installation.

## 3. Target Platform

Primary target: Windows 10/11 x64.

Design the code so the architecture can later support Linux/macOS, but do not compromise the Windows-first lightweight release to achieve cross-platform parity in v1.

## 4. Recommended Technology Stack

### Desktop UI

Use **.NET 8 or later LTS-compatible desktop technology** with a modern Windows UI. Prefer **WPF** if that gives faster delivery and stronger local-system integration; use WinUI 3 only if it does not materially increase deployment complexity.

UI goals:
- Windows 11 visual language
- clean, minimal, modern layout
- dark/light mode
- responsive progress indicators
- keyboard-friendly navigation
- no Electron unless there is a compelling technical reason

### Backend/Application Layer

Use C# with clean separation of concerns:

- Application/Core layer
- Infrastructure layer
- AI layer
- Persistence layer
- UI layer

Do not create a web-server dependency just to run the desktop application.

### Database

SQLite.

Use migrations and a repository/service abstraction.

### Video/Audio Processing

FFmpeg as a local executable dependency.

The application must use FFmpeg to extract/normalize audio from supported video files.

Preferred normalized transcription input:
- mono
- 16 kHz
- PCM WAV
- 16-bit

### Speech-to-Text

Use **whisper.cpp** as the local transcription runtime.

Default transcription profile:
- multilingual Whisper Small or the best currently available small profile supported by the bundled runtime
- optional English-focused model if the user selects English-only meetings
- CPU fallback always available
- GPU acceleration when supported by the machine/runtime
- VAD enabled where appropriate

Model weights must be downloaded separately.

### LLM Runtime

Use **llama.cpp** with GGUF models.

Requirements:
- local inference
- CPU fallback
- GPU acceleration when supported
- configurable context size
- structured JSON output where supported
- streamed generation only where useful
- no cloud endpoint required

Default model class:
- approximately 3B–4B instruct model, 4-bit quantized
- model registry must be configurable so a newer/better small model can be added without changing the application architecture

Do NOT hard-code a single model identity throughout the code. Store model metadata in a local registry/configuration file.

### Text-to-Speech

Use a fully local/offline TTS implementation.

Preferred strategy for Windows v1:
1. Use a locally available Windows speech voice when possible to keep the base installation very small.
2. Support an optional downloaded local neural TTS model/engine as an AI model pack for higher-quality voices.

TTS must never require cloud connectivity.

The generated audio brief must be saved locally in a compressed format such as WAV/Opus/MP3, with the exact codec selected based on reliable local playback support.

## 5. High-Level Architecture

```text
                         ┌───────────────────────────┐
                         │     Windows Desktop UI     │
                         │  Meetings / Search / Play  │
                         └─────────────┬─────────────┘
                                       │
                         ┌─────────────▼─────────────┐
                         │      Application Core      │
                         │ orchestration + workflows  │
                         └───────┬──────┬──────┬──────┘
                                 │      │      │
              ┌──────────────────┘      │      └──────────────────┐
              ▼                         ▼                         ▼
        ┌─────────────┐          ┌─────────────┐          ┌─────────────┐
        │ FFmpeg      │          │ whisper.cpp │          │ llama.cpp   │
        │ media       │          │ ASR         │          │ local LLM   │
        └──────┬──────┘          └──────┬──────┘          └──────┬──────┘
               │                         │                        │
               └─────────────────────────┴────────────────────────┘
                                         │
                                         ▼
                                ┌─────────────────┐
                                │ Structured Data │
                                │     SQLite      │
                                └────────┬────────┘
                                         │
                                         ▼
                                ┌─────────────────┐
                                │ Local TTS       │
                                │ Audio Brief     │
                                └────────┬────────┘
                                         │
                                         ▼
                                   In-App Player
```

## 6. User Workflow

### First launch on a new laptop

1. Start application.
2. Show setup state:
   - Core app ready.
   - AI models not installed.
3. Model Manager offers:
   - Transcription model
   - Meeting analysis model
   - Optional TTS model
4. User chooses a model profile.
5. Application downloads only the selected model files.
6. Validate model checksum.
7. Mark model as installed.
8. Application becomes AI-ready.

### Import meeting

1. User drags a recording onto the app or clicks **Add Meeting**.
2. App detects date/time from filename/media metadata where possible.
3. User can correct date/time, title, and participant information.
4. App creates a meeting record immediately.
5. Processing starts in the background.
6. UI shows stages:

```text
Queued
  ↓
Extracting audio
  ↓
Transcribing
  ↓
Segmenting
  ↓
Analyzing
  ↓
Generating meeting brief
  ↓
Generating audio
  ↓
Completed
```

7. User can continue browsing the database while processing runs.

## 7. Transcription Requirements

Store timestamped transcript segments.

Each segment should contain:
- start time
- end time
- speaker label if available
- text
- confidence if the runtime exposes a reliable confidence value

Example:

```json
{
  "start": "00:12:41",
  "end": "00:12:53",
  "speaker": "Speaker 2",
  "text": "We should complete the API migration by Friday."
}
```

Do not pretend speaker names are known unless the user explicitly maps a speaker label to a person.

## 8. Speaker Handling

Speaker diarization is OPTIONAL in v1.

The app must work perfectly without it.

Base mode:
- no speaker model required
- transcript can be speaker-neutral

Enhanced mode:
- optional local diarization component/model
- outputs Speaker 1, Speaker 2, etc.
- allow user to rename speakers after analysis

Never infer a person's real identity from voice alone.

## 9. Meeting Analysis Output

The LLM must return structured data matching an application schema.

Required top-level structure:

```json
{
  "meetingTitle": "",
  "meetingDate": "",
  "summary": "",
  "agenda": [],
  "topics": [],
  "keyDiscussionPoints": [],
  "decisions": [],
  "actionItems": [],
  "deadlines": [],
  "risks": [],
  "openQuestions": [],
  "followUps": [],
  "participants": [],
  "entities": [],
  "relatedMeetingHints": []
}
```

### Action item structure

```json
{
  "task": "Complete authentication migration",
  "owner": "Sarah",
  "deadline": "2026-09-18",
  "status": "Open",
  "sourceSegmentIds": [42, 43]
}
```

### Decision structure

```json
{
  "decision": "Use the new authentication service for the migration",
  "sourceSegmentIds": [51, 52]
}
```

The LLM must distinguish:
- a decision
- a proposal
- a question
- an unresolved item
- a factual statement

Do not convert speculative discussion into a decision.

## 10. Concise Audio Meeting Brief

This is a first-class feature, not an afterthought.

The application must generate a short spoken briefing from the structured analysis.

The brief should be:
- crisp
- point-to-point
- agenda-oriented
- outcome-oriented
- free of conversational filler
- normally 30–120 seconds
- configurable by the user

Default script structure:

```text
1. Meeting purpose/topic
2. Main agenda items
3. Key outcomes/decisions
4. Action items + owners + deadlines
5. Open risks/questions
```

Example generated script:

> "Today's meeting focused on the API migration.\n\nThe team agreed to complete the authentication migration by Friday, owned by Sarah. Mike will handle the database migration, also targeted for Friday.\n\nThe main unresolved item is the production rollback strategy. No other major blockers were identified."

The script must be concise even when the source meeting is long.

### Audio generation rules

- Generate the script with the local LLM.
- Generate speech with local TTS.
- Save the audio locally.
- Persist the file path in SQLite.
- Do not regenerate audio when the user only re-opens the meeting.
- Regenerate only when the user requests it or when the source analysis changes.

## 11. In-App Audio Player

The generated meeting brief MUST be playable inside the application.

Required controls:
- Play
- Pause
- Stop
- Seek/progress bar
- Current time / duration
- Volume
- Restart
- Playback speed: 0.75x, 1x, 1.25x, 1.5x, 2x

Do not open an external player by default.

The full meeting recording may also have an optional in-app playback button, but the generated concise brief is mandatory.

## 12. Date-Wise Meeting Organization

Primary navigation:

```text
All Meetings

2026
 ├── September 17
 │    ├── API Migration
 │    ├── Sprint Planning
 │    └── Client Review
 ├── September 16
 │    └── Architecture Review
 └── September 15
      └── Release Planning
```

Support:
- Today
- Yesterday
- This Week
- This Month
- Custom Date Range

## 13. Cross-Meeting Knowledge

The app must maintain relationships between facts extracted from different meetings.

Examples:

```text
10 Sep
Decision: migrate authentication
      ↓
14 Sep
Action: Sarah owns migration
      ↓
17 Sep
Status: migration nearly complete
```

A search such as:

> authentication migration

should return related meetings and a synthesized timeline.

Do NOT require an external vector database in v1.

Start with:
- SQLite full-text search where appropriate
- normalized entities
- deterministic references
- optional embeddings only if they materially improve search without making the app heavy

## 14. Suggested SQLite Schema

### Meetings

- Id
- Title
- MeetingDate
- StartTime
- DurationSeconds
- SourceFilePath
- AudioFilePath
- TranscriptFilePath
- AnalysisFilePath
- AudioBriefPath
- ProcessingStatus
- CreatedAt
- UpdatedAt

### TranscriptSegments

- Id
- MeetingId
- Sequence
- StartMs
- EndMs
- Speaker
- Text
- Confidence

### Topics

- Id
- MeetingId
- Name
- Description

### Decisions

- Id
- MeetingId
- DecisionText
- SourceSegmentIds
- CreatedAt

### ActionItems

- Id
- MeetingId
- Task
- Owner
- Deadline
- Status
- SourceSegmentIds

### OpenQuestions

- Id
- MeetingId
- Question
- Status

### MeetingEntities

- Id
- MeetingId
- EntityType
- EntityName

### MeetingRelations

- Id
- SourceMeetingId
- TargetMeetingId
- RelationType
- Evidence

## 15. Search

Provide one global search box.

Search across:
- meeting title
- date
- transcript
- summary
- topics
- decisions
- action items
- people
- questions

Example queries:

- `authentication migration`
- `meetings about database`
- `what decisions were made this week`
- `open questions`
- `actions assigned to Sarah`

Use deterministic DB search first. Use the local LLM only for semantic/synthesized questions.

## 16. Main Screens

### Dashboard

Show:
- today's meetings
- recent meetings
- processing queue
- pending action items
- latest audio briefs
- search

### Meeting Detail

Tabs/sections:
- Audio Brief
- Summary
- Agenda
- Decisions
- Action Items
- Risks
- Open Questions
- Topics
- Transcript
- Source Recording

### Calendar/Timeline

Date-wise list of meetings.

### Search

Global results with grouped matches.

### Model Manager

Show:
- installed models
- model type
- size
- status
- download/remove/update
- CPU/GPU support information
- currently selected model

### Settings

Include:
- library root folder
- model folder
- default language
- transcription profile
- LLM profile
- audio brief duration
- TTS voice
- playback speed
- auto-processing on import
- retention/cleanup settings
- theme

## 17. File Layout

Use a predictable portable data layout.

```text
MeetingKnowledge/
│
├── MeetingKnowledge.exe
├── runtime/
│   ├── ffmpeg/
│   ├── whisper/
│   └── llama/
│
├── models/
│   ├── whisper/
│   ├── llm/
│   └── tts/
│
├── data/
│   ├── meetings.db
│   ├── meetings/
│   │   └── YYYY/MM/DD/<meeting-id>/
│   └── cache/
│
├── config/
│   └── appsettings.json
│
└── logs/
```

Do not store large duplicated intermediate files unnecessarily.

## 18. Processing Pipeline

```text
Input Recording
      ↓
Validate file
      ↓
Read metadata
      ↓
Create Meeting DB record
      ↓
FFmpeg audio extraction
      ↓
Whisper transcription
      ↓
Optional diarization
      ↓
Transcript normalization
      ↓
Chunk transcript
      ↓
LLM section summaries
      ↓
LLM structured extraction
      ↓
Persist meeting intelligence
      ↓
Create concise brief script
      ↓
Local TTS
      ↓
Save audio brief
      ↓
Update DB + notify UI
```

## 19. Long Meeting Strategy

Never depend on fitting an entire two-hour transcript into one LLM context window.

For long meetings:

1. split transcript into logical time/topic chunks
2. summarize chunks locally
3. extract facts from chunks
4. merge facts deterministically where possible
5. run a final synthesis pass on the compact intermediate representation
6. generate the concise audio brief from the final structured result

The final audio brief must remain concise regardless of meeting length.

## 20. Privacy Rules

Hard requirements:

- No meeting data uploaded to cloud services.
- No telemetry containing transcript/content.
- No external AI APIs.
- No analytics SDK that receives meeting content.
- No external web request during processing.
- Network access should only be used when the user explicitly requests model download/update or another clearly local-independent operation.
- Provide a visible **Offline Mode** that disables all non-essential network calls.
- Log locally without storing unnecessary transcript content in debug logs.

## 21. Model Manager

Model Manager must support separate model packs.

Example profiles:

### Light
- small transcription model
- 3B-class quantized LLM
- system TTS voice

### Standard
- better transcription model
- 4B–8B quantized LLM depending on available RAM
- optional neural TTS

### Custom
- user-selected models

Model registry item should include:
- id
- display name
- type
- version
- file name
- download source
- size
- SHA-256 checksum
- minimum RAM
- minimum GPU VRAM if applicable
- supported runtime

Do not hard-code secrets or credentials.

## 22. Hardware Adaptation

At startup, detect:
- OS
- CPU cores
- RAM
- GPU vendor/model if available
- VRAM if available

Then recommend an appropriate profile.

Never require a GPU.

CPU-only operation is a supported baseline.

## 23. Performance Goals

The app itself should remain lightweight when idle.

AI processes should run only when needed.

Use background worker queues for:
- transcription
- LLM analysis
- TTS

Never freeze the UI during processing.

Allow:
- pause
- resume where practical
- cancel
- retry failed stage

Cache successful pipeline stages so an error in TTS does not force a full re-transcription.

## 24. Error Handling

Every processing job must expose:
- current stage
- percentage where available
- human-readable status
- recoverable/non-recoverable error
- retry action

Example:

```text
Analysis complete ✓
Audio generation failed
Reason: TTS model not installed

[Install TTS] [Retry]
```

## 25. Export

Support:
- Markdown meeting minutes
- JSON structured analysis
- plain text transcript
- audio brief file

PDF export can be added only if it does not significantly increase application complexity; it is not required for v1.

## 26. Packaging

Produce a clean Windows release package.

The normal installer/package must include:
- application binaries
- required lightweight runtime dependencies
- FFmpeg runtime if licensing/distribution is handled correctly
- whisper.cpp runtime
- llama.cpp runtime
- no model weights

Provide a portable option if feasible:

```text
MeetingKnowledge-Portable/
    MeetingKnowledge.exe
    runtime/
    models/
    data/
```

## 27. Licensing / Distribution Safety

Track the licenses for:
- FFmpeg build
- whisper.cpp
- llama.cpp
- each downloadable model
- any TTS engine/model

Do not package a model until its redistribution terms are verified and compatible with the project distribution model.

## 28. Application UX Requirements

Keep the UI simple.

The primary actions should be immediately visible:

```text
+ Add Meeting

Search meetings...

Today
Recent Meetings
Pending Actions
Audio Briefs
```

Do not overbuild the navigation.

A user should be able to:

1. install app
2. download AI pack
3. drop a meeting recording
4. wait for processing
5. open the meeting
6. press Play on the concise audio brief
7. inspect decisions/actions/outcomes

with no technical knowledge.

## 29. Security

- Validate all imported file paths.
- Avoid command injection when invoking FFmpeg/AI runtimes.
- Use argument arrays/process APIs rather than unsafe shell concatenation.
- Sanitize model/download paths.
- Verify SHA-256 checksums for downloaded models.
- Restrict model execution to known local binaries.
- Never execute arbitrary downloaded scripts.

## 30. Development Rules

Use clean architecture, strong typing, dependency injection, async APIs, cancellation tokens, and structured logging.

Avoid unnecessary microservices.

This is a local desktop application, not a distributed cloud system.

Prefer:
- simple
- maintainable
- testable
- offline
- fast startup
- small deployment

over architectural complexity.

## 31. Required Automated Tests

Minimum coverage:

### Unit tests
- transcript parsing
- timecode conversion
- JSON schema validation
- meeting aggregation
- action item extraction mapping
- date grouping
- model registry
- path management

### Integration tests
- FFmpeg conversion
- Whisper invocation with a test audio clip
- LLM invocation with a tiny test model or mocked local endpoint
- TTS generation
- SQLite persistence
- end-to-end meeting pipeline on a short fixture recording

### UI tests
At minimum verify:
- import meeting
- processing status
- open meeting
- play/pause audio brief
- search
- model manager

## 32. Definition of Done

The project is considered complete only when all of the following are true:

1. A fresh Windows installation can launch the app without any AI models installed.
2. The app can download required models from Model Manager.
3. A user can drag a meeting recording into the app.
4. The app extracts audio locally.
5. The app transcribes the recording locally.
6. The app produces a structured meeting analysis locally.
7. The app produces a crisp point-to-point spoken brief.
8. The generated brief is saved locally.
9. The brief plays inside the application.
10. Meetings are visible date-wise.
11. Search works across stored meeting information.
12. Existing analysis can be viewed without the AI models running.
13. No cloud AI/API is required.
14. The application installer does not contain model weights.
15. A failed later processing stage can be retried without unnecessarily repeating successful earlier stages.
16. The project builds from a clean checkout using documented commands.
17. A release package is produced.
18. A README explains installation, model download, usage, troubleshooting, and privacy behavior.

## 33. Deliverables

The agent must deliver:

- complete source code
- solution/project files
- database migrations
- UI
- local AI integration
- model manager
- TTS integration
- in-app audio player
- tests
- README
- architecture documentation
- sample configuration
- build scripts
- Windows release package
- portable release package if feasible

## 34. Agent Working Rule

Do not stop after scaffolding.

Do not deliver only architecture or pseudocode.

Implement the actual working application end-to-end.

When a technology choice has multiple reasonable options, select the simplest production-appropriate option and continue without asking for unnecessary confirmation.

Keep the app local-first and lightweight throughout implementation.

## 35. Official Runtime References

Use the current official documentation/repositories for the selected runtime versions before implementing integrations:

- ggml-org/whisper.cpp — local Whisper inference, Windows support, CPU/GPU backends, VAD, model downloads.
- ggml-org/llama.cpp — local GGUF LLM inference and local server/runtime support.

Do not copy code blindly from examples; adapt to the actual application architecture and verify current command-line/API behavior.
