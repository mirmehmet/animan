# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Working rules (non-negotiable)

**Never run git or GitHub operations.** No commit, push, branch, merge, tag, `gh` CLI, or PR. The user owns all version control personally. Read-only inspection (`git log`, `git status`) to understand the codebase is fine; anything that mutates history or leaves the machine is not. Releases are cut by pushing a `v*.*.*` tag — when that is needed, write out the exact commands **step by step for the user to run**, never run them.

**Close every exchange by asking about what is still undecided.** If any point is unresolved — scope, version number, whether a related fix is in or out, user-visible behavior — put it to the user as multiple-choice questions and get an explicit answer before acting. Do not settle open questions with your own judgment, even reasonable-looking defaults.

## Project

AniMan — a Windows-only WPF desktop app (net10.0-windows) for tracking anime and manga, backed by the Tenrai (MyAnimeList) API with AniList as an automatic fallback. UI is WPF UI 3 (Fluent), MVVM via CommunityToolkit source generators, persistence via EF Core 9 + SQLite.

## Commands

```powershell
dotnet restore AniMan.slnx
dotnet build AniMan.slnx
dotnet run --project src/AniMan/AniMan.csproj

dotnet test AniMan.slnx                                  # all tests
dotnet test AniMan.slnx --filter FullyQualifiedName~TrackingServiceTests
dotnet test AniMan.slnx --filter "FullyQualifiedName~ExportServiceTests.Import_Merge_AddsMissingItem"

# self-contained single-file exe → publish/AniMan.exe
dotnet publish src/AniMan/AniMan.csproj -c Release -o publish
```

`AniMan.slnx` is the new-format solution file; always pass it explicitly to `dotnet` commands.

CI (`.github/workflows/ci.yml`) runs restore → build → test on `windows-latest`. Releases are cut by pushing a `v*.*.*` tag; MinVer derives the version from git tags, which is why the release workflow checks out with `fetch-depth: 0`.

### EF Core migrations

Migrations live in `src/AniMan.Infrastructure/Migrations/` (catalog at the root, library under `Migrations/LibraryDb/`) and **are committed**. They were gitignored until v0.2.2, which meant every build — including CI release artifacts — shipped without a schema: `MigrateAsync()` found nothing to apply and every query then failed with `no such table`. Keep them in version control.

Requires the `dotnet-ef` tool (`dotnet tool install --global dotnet-ef`). Any model change needs a new migration for the affected context:

```powershell
dotnet ef migrations add <Name> --project src/AniMan.Infrastructure --context CatalogDbContext
dotnet ef migrations add <Name> --project src/AniMan.Infrastructure --context LibraryDbContext
```

Design-time factories (`CatalogDbContextFactory`, `LibraryDbContextFactory`) point at throwaway `*_design.db` files in the project directory, so `dotnet ef database update` verifies a migration without touching real user data — delete those files afterwards. **Do not pass `--no-build`** to `dotnet ef`: it uses the last Debug build, which will not contain a migration you just added, and the command then reports "already up to date" against an empty database. Generated migration files are exempted from style analyzers in `.editorconfig`; never hand-edit them.

## Architecture

Three projects plus tests, strictly layered — `AniMan` (WPF) → `AniMan.Infrastructure` → `AniMan.Core`.

- **AniMan.Core** — POCOs, enums, `Result`/`Result<T>`, and the service interfaces. No EF, no HTTP, no WPF.
- **AniMan.Infrastructure** — EF contexts, the media-source clients, and all service implementations. Registered in one place: `ServiceCollectionExtensions.AddInfrastructure(appDataPath)`.
- **AniMan** — views, view models, theming, localization. DI container is built in `App.BuildServiceProvider()` and exposed as `App.Services`.

### Two databases, deliberately separate

Everything lives under `%APPDATA%\AniMan\`:

- `catalog.db` — cache of media-source responses (`CachedAnime`, `CachedEpisode`, `CachedGenre`, …). Disposable; deleting it only costs a re-fetch.
- `library.db` — user-owned data (`LibraryItem`, `MediaSnapshot`, progress, notes, `AppSetting`). Must never depend on catalog rows surviving.
- `covers/` — `{malId}_{mediatype}.jpg`, owned by `CoverStore`.
- `logs/` — Serilog daily rolling files, 7 retained.

The bridge between them is the **snapshot pattern**: when an item is added to the library, `SnapshotService` copies the metadata it needs (title, synopsis, totals, genres JSON, cover path) into a `MediaSnapshot` row in `library.db`. Library and detail views read the snapshot, not the catalog — so wiping the cache or losing API access never degrades the user's library. Denormalized fields (`Genres`, `StreamingPlatforms`) are stored as JSON strings.

Both contexts are consumed through `IDbContextFactory<T>` and short-lived `await using var db = ...` scopes — never a long-lived injected `DbContext`.

### Error handling contract

Services return `Result` / `Result<T>` and never throw for expected failures: catch `OperationCanceledException` and rethrow, log everything else, return `Result.Failure(message)`. View models check `IsSuccess`. The global handlers in `App.RegisterGlobalExceptionHandlers` are a last-resort net — anything reaching them is a bug.

### Media sources

**`IMediaSourceClient` is the contract; two clients implement it and a decorator picks between them:**

```
CatalogService ──> IMediaSourceClient ──> FallbackMediaClient
                                            ╱              ╲
                                  TenraiClient        AniListClient
                                (always tried first)  (only on failure)
```

Only `FallbackMediaClient` is registered as `IMediaSourceClient`; the concrete clients are registered as themselves, so nothing can bind straight to a single source. The switch is per request with no circuit breaker, so the app returns to the primary the moment it starts answering — no state to reset, no timer to wait out. When both sources fail, the **primary's** error is surfaced, because that is the message the UI already words for the user.

The DTOs in `MediaSource/Dtos` are the shared schema both clients produce; `MediaMapper` turns them into domain models. Note `AnimeEpisodeDto` is named that way on purpose — a plain `EpisodeDto` would collide with the export-backup record of the same name in `AniMan.Infrastructure.Services`.

`TenraiClient` composes `TenraiRateLimiter` over a Polly retry pipeline; `AniListClient` composes `AniListRateLimiter` (30/min — the degraded budget AniList reports in `X-RateLimit-Limit`) over GraphQL POSTs. Both configure the shared `SlidingWindowRateLimiter`, which is `TimeProvider`-injectable so windows drain in virtual time under test. Tenrai publishes no rate limits and sends no `X-RateLimit-*` headers but does answer 429 under bursts, so its budgets (3/sec, 60/min) are a deliberate guess carried over from Jikan's documented figures. `CatalogService` serves cached rows immediately and, when older than the `CacheRefreshDays` setting, queues a fire-and-forget background refresh rather than blocking the UI.

**Why Tenrai and not Jikan.** AniMan used Jikan v4 until 2026-07-26. **The Jikan public API is being shut down** — announced by its maintainer on 2026-06-14, for personal and funding reasons rather than technical ones: maintenance mode from that date (no fixes, which is why upstream issue #610 was never answered), brownout mode from 2026-09-01, fully discontinued 2026-10-01. Self-hosting is the maintainer's suggested path, which is no use to a desktop app shipped to end users.

Separately, from ~2026-07-10 the service stopped reaching MyAnimeList at all — sustained DDoS attacks were reported on their status channel through April, May and July. Every success came back `X-Cache-Status: STALE` with data frozen at 10–13 July, and anything not already cached returned 504 (`BadResponseException`) whose wording blames MyAnimeList. It was not MyAnimeList: scraping it directly returned full pages throughout.

[Tenrai](https://tenrai.org/) is the successor the Jikan community moved to; its v1 schema is a drop-in match for Jikan v4. The move was a base-URL change plus renames, and every DTO field was verified against the live API. **Do not migrate back.**

**No content filtering, by decision.** Neither source filters by genre, rating or adult flag on any path — the search URLs carry no `sfw` parameter and the AniList queries carry no `isAdult` argument. AniMan lists catalogue metadata rather than serving the works, so the user decides what belongs in their own library. The absence is deliberate; do not "restore" it.

`AniListMapper` translates AniList's GraphQL model into the shared DTOs, so `CatalogService` never learns which source replied. Three of its rules are load-bearing: entries whose `idMal` is null are **dropped** (MAL id is the primary key — 12 of the 50 newest anime have none, and unfiltered they would all collide at id 0); `averageScore` is divided by 10 (AniList scores 0–100); and `Rank`/`Popularity` stay null because AniList's `popularity` is a member count, not MyAnimeList's rank. AniList genres arrive as bare strings and are emitted with `MalId = 0`, which tells `CatalogService.ResolveGenreIdsByNameAsync` to match them by name and allocate unknown ones from a reserved range at 10000+, so a genre stays one row whichever source supplied it.

**A cache write must never sink a successful API response.** `CatalogService` maps DTOs first, then persists inside `TryCacheAsync`, which logs and swallows write failures. This is what made the missing-migrations bug fatal instead of merely slow: a 200 response was discarded because the row could not be cached. Keep new fetch paths on the same shape.

### UI conventions

- Pages and view models are both DI-registered; each page takes its VM in the constructor and assigns `DataContext`. Discover's VM is a **singleton** on purpose — it preserves tab/search/scroll state across navigation; the others are transient.
- Navigation is WPF UI's `INavigationService`. Parameters are passed out-of-band via the singleton `NavigationBag` (`Put` on the source page, `Consume<T>()` in `OnNavigatedToAsync`). `LibraryNavigationState` is a similar static hand-off for "open the library on the Favorites tab".
- View models derive from `ObservableObject` with `[ObservableProperty]` / `[RelayCommand]`. Shared library-page behavior lives in `LibraryViewModelBase`; subclasses supply only the media type and progress projection. Sentinel filter ids: `0` = All, `98` = Ratings, `99` = Favorites; real status ids are in `TrackingStatusIds`.
- Dialogs use WPF UI `ContentDialog` through `IContentDialogService`, raised by the VM as an event and shown by the page code-behind.
- Theming: `Themes/Tokens.xaml` → `Palette.xaml` → `Styles.xaml`, merged after the Wpf.Ui dictionaries so the overrides win. `AppThemeManager` re-applies the app palette and the brand violet accent (`#8B5CF6`) on **every** theme switch — resources set there must use `DynamicResource` at the binding site.
- Localization: `Resources/Strings.resx` + `Strings.tr.resx`, resolved via `{loc:Loc Key}` or `LocalizationManager.Get`. Values resolve at XAML load time, so a language change requires an app restart. Add every new user-facing string to both resx files.
- Deletion is soft: `DeletedAt` marks trash, queries filter `DeletedAt == null`, and startup purges items older than 30 days.

### Packaging gotchas (already fixed — don't undo)

`AniMan.csproj` carries two non-obvious workarounds, each explained in a comment there: `animan.ico` must stay a `<Resource>` (loose Content breaks single-file publish, where `Assembly.Location` is empty), and `PinStableAssemblyVersion` forces `AssemblyVersion=1.0.0.0` after MinVer so the Turkish satellite assembly still binds.

## Style

`.editorconfig` is the source of truth and `EnforceCodeStyleInBuild` is on: file-scoped namespaces, `var`, `_camelCase` private fields, 4-space C# / 2-space XAML+csproj, CRLF. Several CA rules are deliberately disabled with rationale — read the comments before re-enabling one. Infrastructure code uses `.ConfigureAwait(false)` throughout.

## Tests

xUnit + FluentAssertions + Moq, all in `src/AniMan.Tests`. Integration tests use `SqliteContextFactory<T>` — a single kept-open in-memory SQLite connection, so foreign keys, unique indexes, and cascade deletes actually run (unlike the EF InMemory provider). Prefer it over `Microsoft.EntityFrameworkCore.InMemory` for anything touching relational behavior. Test naming is `Method_Scenario_Expectation` (CA1707 is off for this project).
