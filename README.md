# Longform Audio Mix Generator

A Windows desktop application that automatically generates long-form audio mixes
for YouTube channels from batch folders containing audio tracks.

> **Status:** v1 scaffold. Architecture approved; module implementations to follow.

## Tech stack

- **Language:** C# 12
- **Framework:** .NET 8 (`net8.0`, UI on `net8.0-windows`)
- **UI:** WPF + MVVM (CommunityToolkit.Mvvm)
- **Database:** SQLite via Dapper + `Microsoft.Data.Sqlite`
- **Audio processing:** FFmpeg (bundled `ffmpeg.exe` + `ffprobe.exe`)
- **Hosting / DI:** `Microsoft.Extensions.Hosting` + `Microsoft.Extensions.DependencyInjection`
- **Logging:** Serilog (rolling file + in-memory ring buffer for the Processing screen)

## Solution layout

```
LongformAudioMixGenerator.sln
├── src/
│   ├── LAMG.Domain/          Pure domain models and enums (no deps)
│   ├── LAMG.Common/          Small primitives shared across projects
│   ├── LAMG.Application/     Interfaces, use cases, job orchestration
│   ├── LAMG.Infrastructure/  SQLite/Dapper, FFmpeg, file system, logging
│   └── LAMG.UI/              WPF app (3 screens: Import, Settings, Processing)
└── tools/
    └── ffmpeg/               Bundled ffmpeg.exe / ffprobe.exe (Windows)
```

## Build

```
dotnet restore
dotnet build -c Debug
```

On non-Windows hosts the UI project is built with `EnableWindowsTargeting=true`,
which downloads the Windows Desktop targeting pack as a NuGet reference.
WPF can only be **run** on Windows.

## Run (Windows)

```
dotnet run --project src/LAMG.UI/LAMG.UI.csproj
```

## FFmpeg

Place `ffmpeg.exe` and `ffprobe.exe` under `tools/ffmpeg/`. The executables are
located at startup by `IFFmpegLocator` in the following order:

1. `tools/ffmpeg/` next to the application (bundled, preferred).
2. User-configured override path from settings.
3. System `PATH`.

The application refuses to start if neither executable can be located.

## Database

SQLite database `lamg.db` is created in the per-user application data folder on
first run. Schema is managed by plain SQL files under
`src/LAMG.Infrastructure/Persistence/Migrations/` and applied in lexical order
by `MigrationRunner`. The current version is tracked in the `_SchemaVersion`
table.

## Development notes

- This codebase favours **practical code over complex code**. Avoid clever
  abstractions unless they remove duplication or make a real feature possible.
- All long-running work goes through `IJobOrchestrator`, which persists
  checkpoints to the `Jobs` table so the application can resume after crashes,
  power loss, or user-initiated pauses.
- The Processing screen merges live logs and finished mixes into one view; no
  separate Logs/Results screens.
