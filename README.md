# AniTrack

[![CI](https://github.com/mirmehmet/animaTrack/actions/workflows/ci.yml/badge.svg)](https://github.com/mirmehmet/animaTrack/actions/workflows/ci.yml)

A WPF desktop app (.NET 10) for tracking anime and manga, backed by the [Jikan](https://jikan.moe/) (MyAnimeList) API.

## Stack

WPF · WPF UI 3 · CommunityToolkit.Mvvm · EF Core 9 · SQLite · Polly · Serilog · xUnit

## Architecture

| Project | Responsibility |
|---|---|
| `AniTrack.Core` | Domain models, service interfaces, `Result<T>` |
| `AniTrack.Infrastructure` | EF Core (two DbContexts), Jikan client, services |
| `AniTrack` | WPF UI (views, view models) |
| `AniTrack.Tests` | xUnit unit/integration tests |

Two SQLite databases live under `%APPDATA%\AniTrack\`: `catalog.db` (disposable API cache)
and `library.db` (user data). They never share a transaction.

## Build & Test

```bash
dotnet restore AniTrack.slnx
dotnet build AniTrack.slnx -c Release
dotnet test AniTrack.slnx -c Release
```

Requires the .NET 10 SDK. The app builds and runs on Windows only (WPF).

## Backup & Restore

The library can be exported to a portable JSON backup and re-imported in either
**Merge** (add missing, keep existing) or **Overwrite** (replace all) mode.
