# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

MOTHistory is a Blazor Server (.NET 10, Interactive Server render mode) app with a page that looks up a UK vehicle's MOT test history via the DVSA "MOT History API" and renders the results in a table, plus a Settings page for entering API credentials at runtime. A .NET MAUI app (`MOTHistory.Maui/`, "Reg Trace UK") lives in a subdirectory and provides the same lookups natively with a garage, offline cache and iOS MOT-expiry reminders.

Beyond MOT history, both apps optionally call the **DVLA Vehicle Enquiry Service (VES)** for tax status and emissions data, and derive mileage/VED/ULEZ insight locally.

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
- `DVLAApiService.cs` — calls the DVLA Vehicle Enquiry Service: `POST {BaseUrl}/vehicle-enquiry/v1/vehicles` with a `{"registrationNumber": "..."}` body. Auth is an `x-api-key` header only — **no OAuth**, unlike DVSA. Same `(JsonElement? Json, string? Error)` contract as `DVSAApiService`, with two deliberate conventions:
  - **No DVLA key configured returns `(null, null)`** — not an error. Callers treat that as "tax data unavailable" so the app stays fully usable on MOT data alone.
  - **HTTP 404 also returns `(null, null)`** — DVLA simply holds no record; that isn't a configuration problem worth showing the user.
  - VES supplies `taxStatus`, `taxDueDate`, `co2Emissions`, `euroStatus`, `markedForExport`, `dateOfLastV5CIssued`, `monthOfFirstRegistration`, `revenueWeight`, `typeApproval`, `wheelplan`. It returns `make` but **not** `model`; MOT History returns both, so the two are complementary rather than redundant.
- `Shared/` — pure logic with **no platform dependencies**, compiled into the web app by its implicit glob and linked into MAUI via `<Compile Include="..\Shared\*.cs" LinkBase="Shared" />`. Namespace `MOTHistory.Shared`. This exists so the VED rate tables have one source of truth rather than drifting between the two apps.
  - `VehicleTaxInfo.cs` — parses the VES payload. Handles `monthOfFirstRegistration` arriving as `yyyy-MM` (not a full date).
  - `VedCalculator.cs` — estimates annual road tax across the three VED regimes (pre-2001 engine size / 2001-2017 CO2 bands / 2017-onwards flat rate). **The rate table is tax-year specific — `RateTableTaxYear` is currently `2025/26` and must be updated each April.** Output is always presented as an estimate; the £40k list-price supplement cannot be determined from VES data. A `co2Emissions` of 0 on a combustion car means "DVLA holds no figure", not band A, and is treated as unknown.
  - `UlezChecker.cs` — indicative ULEZ compliance from `euroStatus` (petrol needs Euro 4+, diesel Euro 6+), falling back to registration date when no Euro status is published. Always labelled an estimate alongside a link to TfL's authoritative checker.
  - `MileageAnalysis.cs` — derives clocking/anomaly warnings and average annual mileage from MOT odometer readings. No extra API call. Takes a neutral `MileageReading(DateTime, double Miles)` so it doesn't depend on either app's `MotTest` type. Rollbacks under 100 miles are ignored (same-day retests and keying slips); implausible jumps need both a >50,000 mi/yr rate *and* a >30,000 mile absolute delta so short gaps don't trip it.
- `DVSASettings.cs` — plain POCO holding credential/config for both APIs: DVSA (`ApiKey`, `BaseUrl`, `ClientId`, `ClientSecret`, `ScopeUrl`, `TokenUrl`) and DVLA (`DvlaApiKey`, `DvlaBaseUrl`).
- `DVSASettingsService.cs` — singleton that is the single source of truth for `DVSASettings`. On construction it loads from a JSON file in `%LocalAppData%\MOTHistory\dvsa-settings.json`; if that file doesn't exist yet, it falls back to the `DVSAApi` section of `appsettings.json`/`appsettings.Development.json` (in-memory only — it does not write that fallback to disk). `Save(settings)` persists to the same file and updates the in-memory copy. `Current` returns a clone, so callers can't mutate shared state. Storing the file outside the repo means credentials entered via the Settings page are never written into source-controlled config.
- `Components/Pages/MOTHistory.razor` — the MOT lookup page (`@page "/"`). Fires `DVSAApiService.GetMotHistoryAsync` and `DVLAApiService.GetVehicleAsync` concurrently via `Task.WhenAll` (they're independent, so the DVLA call adds no latency), then manually parses the returned `JsonElement` into a local `MotTest` record (date, result, expiry, odometer). Also renders the outstanding-recall banner, mileage-anomaly alert and tax/emissions card.
  - **`@using global::MOTHistory.Shared` needs the `global::` prefix here.** This component's own generated class is named `MOTHistory`, which shadows the `MOTHistory` namespace root — an unqualified `MOTHistory.Shared` fails with CS0426.
  - `hasOutstandingRecall` comes from the MOT History payload (vehicle level, not per-test) and is documented as a string `"Yes"`/`"No"`/`"Unknown"`; both apps also accept a JSON boolean defensively. Each `motTests[].defects[]` entry has a `type` of `ADVISORY`, `MINOR`, `MAJOR`, `DANGEROUS`, or the legacy `FAIL`; `MAJOR`/`DANGEROUS`/`FAIL` are bucketed as `Failures`, `ADVISORY`/`MINOR` as `Advisories`. `expiryDate` can be JSON `null` (e.g. on a failed test) and is guarded accordingly. There's no shared DTO layer — JSON parsing happens directly in the page's code block. Links to `/settings`.
- `Components/Pages/Settings.razor` — the Settings page (`@page "/settings"`). An `EditForm` bound to a `DVSASettings` instance loaded from `DVSASettingsService.Current`; `Save` writes the edited model back via `DVSASettingsService.Save`. Shows all DVSA fields unconditionally (there's no public/trade mode toggle since there's only one API mode), plus an optional DVLA section — leaving `DvlaApiKey` blank simply disables the tax/emissions features.
- `Components/Layout/`, `Components/Routes.razor`, `Components/App.razor` — standard Blazor Web App scaffolding (router, main layout, reconnect modal for SignalR disconnects).

## MAUI app (`MOTHistory.Maui/`)

Mirrors the web app's services (`Services/DVSAApiService.cs`, `Services/DVLAApiService.cs`, `Services/DVSASettingsService.cs`) but persists settings via `Preferences` (NSUserDefaults on iOS) rather than a JSON file, and stores no `appsettings.json` fallback. Both HTTP services are registered with `AddHttpClient<T>()` in `MauiProgram.cs`.

- `Pages/SearchPage.xaml(.cs)` — the main lookup screen. Runs the DVSA and DVLA calls concurrently, then builds result cards programmatically in code-behind (so tap gestures work and text doesn't clip). `BuildTaxCard` hides itself entirely when there's no DVLA key *and* no error, so it never shows as an empty shell. `ShowTaxOnlyResults` handles the case of a vehicle under three years old: no MOT tests exist yet, but DVLA tax data does, so the page shows what it has instead of a bare "nothing found".
- `Services/OfflineCacheService.cs` — caches raw payloads per registration, keyed by *kind* (`OfflineCacheService.MotKind` / `DvlaKind`) so MOT and DVLA responses cache independently and tax data survives going offline.

## Configuration

`appsettings.json` / `appsettings.Development.json` have `DVSAApi` and `DVLAApi` sections — these are only used as the *initial* fallback the first time the web app runs with no `%LocalAppData%\MOTHistory\dvsa-settings.json` file present. Once a user saves credentials via the `/settings` page, that JSON file is the source of truth and `appsettings.json` is no longer read. (The `DVSAApi` section may still contain stale `UsePublicApi`/`PublicBaseUrl` keys from before that distinction was removed — they're simply ignored.)

The DVSA credential values in `appsettings.json` have been cleared to empty strings; keep them that way and enter credentials through `/settings`, which persists them outside the repo.

## Data the app deliberately does not provide

Registered-keeper / owner details are **not available via any API** and must not be added. DVLA releases keeper data only under "reasonable cause" via a manual form V888 application, restricted to parties with a legitimate interest; it is personal data under UK GDPR. The same applies to stolen markers (PNC), insurance write-off categories (MIAFTR) and outstanding finance (HPI) — all of which are reachable only through licensed commercial resellers under contract, not a public API.
