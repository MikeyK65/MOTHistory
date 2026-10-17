# MOT History

Look up a UK vehicle's full MOT history by registration number. Built on the official DVSA **MOT History API**, with optional tax and emissions data from the DVLA **Vehicle Enquiry Service (VES)**.

This repo contains two apps that share the same core logic:

| App | Description |
| --- | --- |
| **Web app** (repo root) | Blazor Server (.NET 10) site with a lookup page and a Settings page for entering API credentials. |
| **Reg Trace UK** (`MOTHistory.Maui/`) | .NET MAUI app with a saved-vehicle garage, offline cache and iOS MOT-expiry reminders. |

## Features

- **MOT history** – every test with date, result, expiry date and odometer reading, plus advisories and failures (minor / major / dangerous).
- **Outstanding recall banner** – flags vehicles with an open manufacturer recall.
- **Mileage analysis** – highlights possible clocking or implausible mileage jumps and shows average annual mileage, derived locally from MOT odometer readings.
- **Tax & emissions** (optional, needs a DVLA key) – tax status and due date, CO₂, Euro status, export marker, and more.
- **Estimated road tax (VED)** – across the three VED regimes (pre-2001, 2001–2017 CO₂ bands, 2017 onwards).
- **Indicative ULEZ check** – based on Euro status or registration date, with a link to TfL's authoritative checker.
- **Newer vehicles** – cars under three years old have no MOT yet, so the app shows the DVLA data it does have.
- **MAUI app extras** – garage of saved cars, offline cache, MOT-expiry reminders on iOS.

> VED and ULEZ figures are **estimates** only. Always confirm with GOV.UK and TfL.

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A DVSA MOT History API credential set (free – see below)
- *(Optional)* a DVLA VES API key
- *(MAUI app only)* the .NET MAUI workload and, for iOS builds, a Mac with Xcode

### Get API credentials

1. **DVSA MOT History API** – register at <https://documentation.history.mot.api.gov.uk/> to receive an API key, client ID, client secret, scope URL and token URL.
2. **DVLA Vehicle Enquiry Service** *(optional)* – request a key at <https://developer-portal.driver-vehicle-licensing.api.gov.uk/>. Without it the app still works using MOT data alone.

### Run the web app

```bash
git clone https://github.com/<your-username>/MOTHistory.git
cd MOTHistory
dotnet run
```

Then open <http://localhost:5166> (or <https://localhost:7283>).

1. Go to **Settings** (`/settings`).
2. Enter your DVSA credentials and, optionally, your DVLA API key.
3. Save, return to the home page, and enter a registration number.

Use `dotnet watch run` for hot reload during development.

### Run the MAUI app

```bash
cd MOTHistory.Maui
dotnet build -t:Run -f net10.0-ios   # or another target framework you have installed
```

Enter your credentials on the app's Settings screen. They are stored on-device via MAUI `Preferences`.

## Configuration & credentials

Credentials are entered through the Settings page and saved to
`%LocalAppData%\MOTHistory\dvsa-settings.json` – **outside the repository**, so they are never committed.

`appsettings.json` has `DVSAApi` and `DVLAApi` sections that are used only as an initial fallback if that file doesn't exist yet. Leave the values blank in source control.

## Project structure

```
MOTHistory/
├── Program.cs               # Hosting, DI, middleware
├── DVSAApiService.cs        # DVSA MOT History API client (OAuth2 + x-api-key)
├── DVLAApiService.cs        # DVLA VES client (x-api-key)
├── DVSASettings*.cs         # Settings model + persistence
├── Components/              # Blazor pages (lookup, settings) and layout
├── Shared/                  # Platform-free logic shared with the MAUI app
│   ├── VehicleTaxInfo.cs    #   VES payload parsing
│   ├── VedCalculator.cs     #   Road tax estimate
│   ├── UlezChecker.cs       #   ULEZ indication
│   └── MileageAnalysis.cs   #   Clocking / anomaly detection
└── MOTHistory.Maui/         # Reg Trace UK (.NET MAUI)
```

The VED rate table in `Shared/VedCalculator.cs` is tax-year specific and needs updating every April.

## What this app does *not* do

Registered-keeper details, stolen markers, insurance write-off categories and outstanding finance are **not** available through any public API and are deliberately not included. They are personal data or only available via licensed commercial providers.

## Data sources & disclaimer

Vehicle data is provided by the DVSA and DVLA under their respective API terms. This is an independent project and is not affiliated with or endorsed by either agency. Results are provided as-is, with no guarantee of accuracy.

## Contributing

Issues and pull requests are welcome. Please open an issue first for larger changes.

## Licence

<!-- Add a LICENSE file and state it here, e.g. MIT. -->
