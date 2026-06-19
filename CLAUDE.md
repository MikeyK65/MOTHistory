# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

MOTHistory is a Blazor Server (.NET 10, Interactive Server render mode) app with a page that looks up a UK vehicle's MOT test history via the DVSA "MOT History API" and renders the results in a table, plus a Settings page for entering DVSA API credentials at runtime.

## Commands

```
dotnet build                 # build
dotnet run                   # run (http://localhost:5166, https://localhost:7283)
dotnet watch run              # run with hot reload
```

There is no test project in this repo currently.

## Architecture

- `Program.cs` — minimal hosting setup. Registers `DVSASettingsService` as a singleton and `DVSAApiService` via `AddHttpClient<DVSAApiService>()` (so it gets a managed `HttpClient`), wires up Razor Components with interactive server render mode, and sets up the standard exception/status-code/HSTS middleware.
- `DVSAApiService.cs` — the only API-calling service in the app. All calls to the DVSA API go through `GetMotHistoryAsync(registration)`, which reads its config from `DVSASettingsService.Current` (not `IConfiguration` directly).
  - The DVSA MOT History API has exactly one endpoint for single-vehicle lookups: `GET {BaseUrl}/v1/trade/vehicles/registration/{registration}` (registration is a *path* segment, not a query parameter — despite the literal `trade` in the path, this same endpoint serves both public- and trade-tier API keys; access tier is determined by the OAuth token's role claim, not the URL). There is no separate "public" endpoint — an earlier version of this code incorrectly assumed `/v1/vehicles?registration=...` was a valid public-tier path, which DVSA rejects.
  - Every request requires both an OAuth2 client-credentials Bearer token (`GetAccessTokenAsync`, against `TokenUrl` using `ClientId`/`ClientSecret`/`ScopeUrl`) and the `x-api-key` header — this is true regardless of access tier.
  - Returns `(JsonElement? Json, string? Error)` — callers branch on `Error` rather than catching exceptions for expected API failures (missing/invalid credentials, 401/403, non-success status).
  - Registration numbers are normalized (uppercased, whitespace stripped) before being sent.
  - `GetMotHistoryAsync` clones `doc.RootElement` (`.Clone()`) before returning it, since the backing `JsonDocument` is disposed (`using`) at the end of the method — returning the element without cloning would throw `ObjectDisposedException` on first access by the caller.
- `DVSASettings.cs` — plain POCO holding the DVSA credential/config fields (`ApiKey`, `BaseUrl`, `ClientId`, `ClientSecret`, `ScopeUrl`, `TokenUrl`).
- `DVSASettingsService.cs` — singleton that is the single source of truth for `DVSASettings`. On construction it loads from a JSON file in `%LocalAppData%\MOTHistory\dvsa-settings.json`; if that file doesn't exist yet, it falls back to the `DVSAApi` section of `appsettings.json`/`appsettings.Development.json` (in-memory only — it does not write that fallback to disk). `Save(settings)` persists to the same file and updates the in-memory copy. `Current` returns a clone, so callers can't mutate shared state. Storing the file outside the repo means credentials entered via the Settings page are never written into source-controlled config.
- `Components/Pages/MOTHistory.razor` — the MOT lookup page (`@page "/"`). Calls `DVSAApiService.GetMotHistoryAsync`, then manually parses the returned `JsonElement` into a local `MotTest` record (date, result, expiry, odometer). Each `motTests[].defects[]` entry has a `type` of `ADVISORY`, `MINOR`, `MAJOR`, `DANGEROUS`, or the legacy `FAIL`; `MAJOR`/`DANGEROUS`/`FAIL` are bucketed as `Failures`, `ADVISORY`/`MINOR` as `Advisories`. `expiryDate` can be JSON `null` (e.g. on a failed test) and is guarded accordingly. There's no shared DTO layer — JSON parsing happens directly in the page's code block. Links to `/settings`.
- `Components/Pages/Settings.razor` — the Settings page (`@page "/settings"`). An `EditForm` bound to a `DVSASettings` instance loaded from `DVSASettingsService.Current`; `Save` writes the edited model back via `DVSASettingsService.Save`. Shows all fields (`ApiKey`, `BaseUrl`, `ClientId`, `ClientSecret`, `ScopeUrl`, `TokenUrl`) unconditionally — there's no public/trade mode toggle since there's only one API mode.
- `Components/Layout/`, `Components/Routes.razor`, `Components/App.razor` — standard Blazor Web App scaffolding (router, main layout, reconnect modal for SignalR disconnects).

## Configuration

`appsettings.json` / `appsettings.Development.json` still have a `DVSAApi` section (`ApiKey`, `BaseUrl`, `ClientId`, `ClientSecret`, `ScopeUrl`, `TokenUrl`) — this is only used as the *initial* fallback the first time the app runs with no `%LocalAppData%\MOTHistory\dvsa-settings.json` file present. Once a user saves credentials via the `/settings` page, that JSON file is the source of truth and `appsettings.json` is no longer read. (The section may still contain stale `UsePublicApi`/`PublicBaseUrl` keys from before that distinction was removed — they're simply ignored.)

**Note:** `appsettings.json` still has live-looking `ApiKey`/`ClientSecret` values committed in plaintext from before the Settings page existed. Treat these as sensitive — don't echo them in full. Since credentials can now be entered through the UI and persisted outside the repo, it's worth clearing the real values out of `appsettings.json` once you've confirmed the Settings-page flow works for you.
