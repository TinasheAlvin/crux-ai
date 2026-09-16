# Crux AI

CSV-first BI for South African service-business owners.

This repository currently contains the **CSV map first slice** of the V1 spine:

`CSV upload / map → validate (fix in place) → persist normalized transactions with stable RowIds`

Health KPI dashboards, Azure OpenAI “why”, morning brief, Nango connectors, and WhatsApp are **out of scope** for this pass.

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
src/CruxAI.Core/              Domain, mapping, validation
src/CruxAI.Infrastructure/    EF Core, CSV reader, file storage, import service
src/CruxAI.Web/               Blazor UI + demo auth
src/CruxAI.Tests/
testdata/sample-transactions.csv
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

### Demo path (CSV map)

1. Click **Continue as demo owner**. This signs you in with a cookie and creates:
   - Organisation: `Harbour Street Studio`
   - User: `owner@harbourstreet.local`
   - Owner membership
2. On the empty home, click **Upload CSV**.
3. Choose `testdata/sample-transactions.csv`. Headers such as `Txn Date`, `Details`, and `ZAR Amount` are auto-guessed; override if needed, then **Confirm mapping**.
4. The sample file includes broken rows (`not-a-date`, empty description, `abc` amount). Edit those cells, click **Re-validate**, then **Persist**.
5. The result page lists normalized rows and their stable `RowId` values (`imp_{importId}_r{sourceRowNumber}`).

`testdata/sample-transactions-clean.csv` is a happy-path file with no validation errors.

You can leave validation, come back via **Recent imports**, and keep fixing the same import — you do not need to re-upload.

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

Demo login is intentional so the CSV flow can be exercised without an Entra tenant. Replacing it:

1. Add `Microsoft.Identity.Web`.
2. Fill `Auth:EntraExternalId` via user-secrets.
3. Follow the commented block in `src/CruxAI.Web/Program.cs`.
4. Map the Entra `oid` onto `AppUser.ExternalId` and keep using `Membership` for org access.

## License / product

Product name: **Crux AI**.
