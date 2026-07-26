# AniMan

[![CI](https://github.com/mirmehmet/animan/actions/workflows/ci.yml/badge.svg)](https://github.com/mirmehmet/animan/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-violet.svg)](LICENSE)

A WPF desktop app for tracking anime and manga, powered by the [Tenrai](https://tenrai.org/) (MyAnimeList) API with [AniList](https://anilist.co/) as an automatic fallback.

---

## Features

- Search and discover anime & manga from the MyAnimeList catalogue
- Keeps working when the primary API is down — requests fall back to AniList and return on their own
- Personal library with status tracking (Watching, Completed, On-hold, Dropped, Plan to watch)
- Episode and chapter progress tracking
- Cover art downloaded and cached locally
- Stats page with score distribution, top genres, and monthly activity
- Export & import library as a portable JSON backup (Merge or Overwrite)
- Light / Dark / System theme with violet accent
- Turkish and English localization

---

## Built With

- **WPF + WPF UI 3** — UI framework and Fluent controls
- **CommunityToolkit.Mvvm** — MVVM source generators
- **Tenrai + AniList** — MyAnimeList metadata over REST, with AniList's GraphQL API standing in when the primary cannot answer
- **EF Core 9 + SQLite** — Two separate databases: `catalog.db` (disposable API cache) and `library.db` (user data)
- **Polly** — Retry and resilience for API calls
- **Serilog** — Structured daily rolling logs
- **xUnit** — Unit and integration tests

---

## Data

All user data lives under `%APPDATA%\AniMan\`:

```
AniMan/
├── catalog.db    — API metadata cache (can be deleted safely)
├── library.db    — your library, progress, notes
├── covers/       — downloaded cover images
└── logs/         — daily log files
```

---

## Download

Go to **[Releases](https://github.com/mirmehmet/animan/releases)** and grab the latest `AniMan.exe`. No installation or .NET runtime required — just run it.

---

## Build & Run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Windows only (WPF).

```bash
git clone https://github.com/mirmehmet/animan.git
cd animan
dotnet restore AniMan.slnx
dotnet build AniMan.slnx
dotnet run --project src/AniMan/AniMan.csproj
```

To produce a single self-contained executable:

```bash
dotnet publish src/AniMan/AniMan.csproj -c Release -o publish
```

Output: `publish/AniMan.exe` — no .NET installation needed on the target machine.

To run tests:

```bash
dotnet test AniMan.slnx
```

---

*Made with curiosity by [Mir](https://github.com/mirmehmet)*
