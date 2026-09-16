# Crux AI

CSV-first BI for South African service-business owners.

V1 spine in this repo so far:

`CSV upload / map → validate → persist RowIds → health KPIs (period compare)`

Azure OpenAI receipted “why”, morning brief, Nango connectors, and WhatsApp are **out of scope** for this pass. Tapping a KPI card only **seeds** a why prompt.

## Stack

- .NET 8 / ASP.NET Core
- Blazor Web App (Interactive Server)
- EF Core with **SQLite** for local demo
- Local filesystem CSV storage
- Cookie **demo auth** that bootstraps an Organisation + owner membership

Placeholders (not wired yet):

- Microsoft Entra External ID (`Auth:EntraExternalId` in config, TODOs in `src/CruxAI.Web/Program.cs`)
- Azure SQL (`ConnectionStrings:AzureSql`, set `Database:Provider` to `AzureSql`)
- Azure Blob (`Storage:AzureBlob`, set `Storage:Provider` to `AzureBlob`)

## Solution layout

```
src/CruxAI.sln
src/CruxAI.Core/              Domain, mapping, validation, health KPI calculator
src/CruxAI.Infrastructure/    EF Core, CSV reader, file storage, import + health services
src/CruxAI.Web/               Blazor UI + demo auth
src/CruxAI.Tests/
testdata/sample-transactions.csv
testdata/sample-transactions-with-cash.csv
```

## Run locally

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet restore src/CruxAI.sln
dotnet build src/CruxAI.sln
dotnet test src/CruxAI.sln
dotnet run --project src/CruxAI.Web --launch-profile http
```

Open http://localhost:5028

The first run creates `src/CruxAI.Web/App_Data/cruxai.db` and `src/CruxAI.Web/App_Data/uploads/` (gitignored).

If you already ran the CSV-map slice, delete `src/CruxAI.Web/App_Data/cruxai.db` so SQLite picks up the optional `Balance` column (or just let the startup patch add it).

### Demo path (CSV map → health KPIs)

1. Click **Continue as demo owner**. This signs you in with a cookie and creates:
   - Organisation: `Harbour Street Studio`
   - User: `owner@harbourstreet.local`
   - Owner membership
2. Click **Use sample CSV** (or upload `testdata/sample-transactions.csv`).
3. Confirm auto-guessed columns, then persist. The sample has Feb + Mar 2026 rows and a few broken March cells — fix those in place, then persist.
4. You land on **health KPIs**: revenue, expenses, and profit for the latest month in the file vs the previous month.
5. **Cash is hidden** on this sample — there is no balance column, so Crux never shows a fake R0 cash card.
6. Tap a KPI card to seed a “Why did this change?” prompt (no AI answer yet). Use **Remap columns** if the mapping was wrong.
7. To see cash: **Sample with cash** (or `testdata/sample-transactions-with-cash.csv`). `Running Balance` maps to Balance; the cash card uses the latest usable balance in each month.

`testdata/sample-transactions-clean.csv` is a happy-path file with no validation errors (still no cash).

## How KPIs are computed

- Periods follow the **latest transaction date** in the org (not today’s calendar), so a March 2026 CSV still compares Mar vs Feb.
- Revenue = sum of positive amounts; expenses = absolute sum of negative amounts; profit = revenue − expenses.
- Cash is shown only when the current import mapping includes **Balance** *and* at least one current-month row has a usable balance. Otherwise the card is omitted.
- Partial metrics still render: if February is missing, March cards stay up with a short compare note.

## Configuration and secrets

Committed files contain **placeholders only**. Do not put real connection strings, client secrets, or account keys in git.

| Setting | Local demo | Later |
| --- | --- | --- |
| `Database:Provider` | `Sqlite` | `AzureSql` |
| `ConnectionStrings:Sqlite` | `Data Source=App_Data/cruxai.db` | unused |
| `ConnectionStrings:AzureSql` | empty | user-secrets |
| `Storage:Provider` | `Local` | `AzureBlob` |
| `Storage:LocalRoot` | `App_Data/uploads` | unused |
| `Storage:AzureBlob:*` | empty placeholders | user-secrets |
| `Auth:Provider` | `Demo` | Entra External ID |
| `DemoAuth:*` | Harbour Street Studio owner | unused once Entra is live |

Copy `src/CruxAI.Web/appsettings.Example.json` for a full list of keys, then store real values in user secrets:

```bash
dotnet user-secrets set "ConnectionStrings:AzureSql" "Server=tcp:..." --project src/CruxAI.Web
dotnet user-secrets set "Storage:AzureBlob:ConnectionString" "DefaultEndpointsProtocol=https;..." --project src/CruxAI.Web
dotnet user-secrets set "Auth:EntraExternalId:TenantId" "..." --project src/CruxAI.Web
dotnet user-secrets set "Auth:EntraExternalId:ClientId" "..." --project src/CruxAI.Web
dotnet user-secrets set "Auth:EntraExternalId:ClientSecret" "..." --project src/CruxAI.Web
```

The Web project already has a `UserSecretsId`. User secrets live on the developer machine, not in the repo.

HTTPS is available via `--launch-profile https` once the ASP.NET Core developer certificate is trusted.

## Auth placeholder

Demo login is intentional so the CSV + KPI flow can be exercised without an Entra tenant. Replacing it:

1. Add `Microsoft.Identity.Web`.
2. Fill `Auth:EntraExternalId` via user-secrets.
3. Follow the commented block in `src/CruxAI.Web/Program.cs`.
4. Map the Entra `oid` onto `AppUser.ExternalId` and keep using `Membership` for org access.

## License / product

Product name: **Crux AI**.
