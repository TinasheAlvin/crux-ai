# Crux AI

CSV-first BI for South African service-business owners.

V1 spine in this repo:

`CSV upload / map → validate → persist RowIds → health KPIs (period compare) → receipted why → morning brief`

Nango connectors, WhatsApp, email, and multi-story digests remain **out of scope**. Azure OpenAI is optional (intent classification only); the local demo uses a deterministic verifier and still **fails closed** without RowId citations. Morning brief generation is **on-demand at next visit** — Azure Functions are not required.

## Stack

- .NET 8 / ASP.NET Core
- Blazor Web App (Interactive Server)
- EF Core with **SQLite** for local demo
- Local filesystem CSV storage
- Cookie **demo auth** that bootstraps an Organisation + owner membership

Placeholders (not wired as the trust boundary):

- Microsoft Entra External ID (`Auth:EntraExternalId` in config, TODOs in `src/CruxAI.Web/Program.cs`)
- Azure SQL (`ConnectionStrings:AzureSql`, set `Database:Provider` to `AzureSql`)
- Azure Blob (`Storage:AzureBlob`, set `Storage:Provider` to `AzureBlob`)
- Azure OpenAI (`AzureOpenAI:*` — if Endpoint / DeploymentName / ApiKey are set, it may classify *which KPI* a free-text question is about. It never authors amounts. Without keys, the deterministic verifier runs alone.)

## Solution layout

```
src/CruxAI.sln
src/CruxAI.Core/              Domain, mapping, validation, health KPI calculator, why verifier, brief composer, analytics contracts
src/CruxAI.Infrastructure/    EF Core, CSV reader, file storage, import + health + why + morning brief + event log
src/CruxAI.Web/               Blazor UI + demo auth + partner dump endpoint
src/CruxAI.Tests/
testdata/sample-transactions.csv
testdata/sample-transactions-with-cash.csv
testdata/sample-transactions-clean.csv
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

If you already ran an earlier slice, delete `src/CruxAI.Web/App_Data/cruxai.db` so SQLite picks up Why / morning-brief tables (or let the startup patch create those tables).

### Demo path (CSV map → health KPIs → receipted why → morning brief)

1. Click **Continue as demo owner**. This signs you in with a cookie and creates:
   - Organisation: `Harbour Street Studio`
   - User: `owner@harbourstreet.local`
   - Owner membership
2. Click **Use sample CSV** (or upload `testdata/sample-transactions.csv`). For a faster happy path with no broken cells, use `testdata/sample-transactions-clean.csv`.
3. Confirm auto-guessed columns, then persist. The default sample has Feb + Mar 2026 rows and a few broken March cells — fix those in place, then persist.
4. You land on **health KPIs**: revenue, expenses, and profit for the latest month in the file vs the previous month.
5. **Cash is hidden** on this sample — there is no balance column, so Crux never shows a fake R0 cash card.
6. Tap a KPI card. Crux seeds “Why did this change?” and answers from the Transaction store. The answer includes a **receipt** of the exact persisted RowIds (and columns such as Date, Description, Amount).
7. Open the receipt to see those rows. Type a different question in the chat box (on Health or Why). If the question cannot be cited to RowIds, you get **Can't verify that yet.** and **Try another question** — never an uncited number or draft answer.
8. After a **cited** why, an opt-in sheet appears with one primary CTA: **Send me the morning brief**. Dismiss it once with **Not now** and it will not come back (it is not buried in settings).
9. Open **Home** or **Brief** (next visit). You get **yesterday’s snapshot KPIs** plus **one** receipted explanation and a receipt strip. If nothing can be cited: **No verified brief today** and a path back to **Ask why** — never a fake or multi-story digest.
10. Use **Remap columns** if the mapping was wrong. To see cash: **Sample with cash** (or `testdata/sample-transactions-with-cash.csv`). `Running Balance` maps to Balance; the cash card uses the latest usable balance in each month, and cash-why cites those Balance rows.

## How KPIs are computed

- Periods follow the **latest transaction date** in the org (not today’s calendar), so a March 2026 CSV still compares Mar vs Feb.
- Revenue = sum of positive amounts; expenses = absolute sum of negative amounts; profit = revenue − expenses.
- Cash is shown only when the current import mapping includes **Balance** *and* at least one current-month row has a usable balance. Otherwise the card is omitted.
- Partial metrics still render: if February is missing, March cards stay up with a short compare note.

## How receipted why works

- Why is computed from the same persisted transactions as the KPI cards. Totals in a verified answer are taken from the cited RowIds.
- Every verified answer stores Q&A (question, answer, organisation, timestamps) plus a citation table (`WhyAnswer` → `RowId[]` and the columns that support the claim).
- If the verifier cannot produce that citation trail — unknown question, hidden cash, missing comparison rows — it returns **Can't verify that yet.** and logs the Q&A with an empty citation list.
- Azure OpenAI is not required. When configured it may only label the KPI; it is not allowed to invent financial facts.

## How the morning brief works

- Opt-in appears only after a **trusted (cited) why**, as a sheet with one primary CTA (**Send me the morning brief**). Dismiss once and it does not return.
- Flags are stored per organisation + user (`MorningBriefPreference`).
- Next visit (Home or Brief) generates or reads today’s `MorningBrief`: frozen snapshot KPIs + **one** explanation + RowId citations. Same fail-closed rules as why — no citations means **No verified brief today**, never a fabricated digest.
- Generation is on-demand at next visit (`MorningBriefService.EnsureTodaysBriefAsync`). `MorningBriefFunctionsStub` is the Azure Functions timer hook; local demo does not need Functions.

## Design-partner instrumentation

Lightweight scoreboard events for design-partner sessions. **No new product features** — a tiny trust prompt after a cited why, and a clearly labelled partner-only waitlist stub. There is no paid analytics vendor.

`IAnalytics` stamps `orgId`, `userId`, `timestamp`, and optional properties, then appends to `IEventLog`. Local demo default is a **JSONL file**; tests use an in-memory sink. `Analytics:Sink` can also be `Sqlite` (`App_Data/partner-events.db`) or `Memory`.

### Event names

| Event | When |
| --- | --- |
| `finishes_upload` | CSV import is persisted (RowIds written) |
| `asks_why_session_one` | First why ask in this browser circuit |
| `rates_explanation_trustworthy` | Yes on “Was this trustworthy enough to act on?” after a cited why |
| `receipt_distrust` | No on that trust prompt |
| `returns_for_brief_within_7_days` | Opted-in next visit shows the brief, and opt-in was ≤ 7 days ago (not the same circuit as opt-in) |
| `pay_or_waitlist_signal` | Partner-only **Join waitlist** on Home or Brief |
| `map_abandon` | Leaves column map/confirm without saving (interactive circuit only; prerender dispose is ignored) |
| `receipt_open` | Opens the receipt / cited row list |

### How to dump events after a partner session

Default sink (from the Web project directory):

```bash
cat src/CruxAI.Web/App_Data/partner-events.jsonl
```

Count by name:

```bash
python3 -c "import json,collections,pathlib
p=pathlib.Path('src/CruxAI.Web/App_Data/partner-events.jsonl')
c=collections.Counter(json.loads(l)['name'] for l in p.read_text().splitlines() if l.strip())
print('\n'.join(f'{n:4} {k}' for k,n in c.most_common()))"
```

While signed in, `GET http://localhost:5028/internal/partner-events` returns the same events as JSON.

If you set `Analytics:Sink` to `Sqlite`:

```bash
sqlite3 src/CruxAI.Web/App_Data/partner-events.db \
  "SELECT name, org_id, user_id, timestamp, properties_json FROM partner_events ORDER BY id;"
```

Each JSONL line looks like:

```json
{"name":"finishes_upload","orgId":"...","userId":"...","timestamp":"2026-09-16T21:04:00.0000000Z","properties":{"importJobId":"...","rowCount":"12"}}
```

The file lives under gitignored `App_Data/`. Delete it between partner sessions if you want a clean dump.

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
| `AzureOpenAI:Endpoint` | placeholder / empty | user-secrets |
| `AzureOpenAI:DeploymentName` | placeholder / empty | user-secrets |
| `AzureOpenAI:ApiKey` | empty | user-secrets |
| `Analytics:Sink` | `File` (JSONL) | `Memory` or `Sqlite` |
| `Analytics:FilePath` | `App_Data/partner-events.jsonl` | unused if Sink is Memory |
| `Analytics:SqlitePath` | `App_Data/partner-events.db` | used when Sink is Sqlite |

Copy `src/CruxAI.Web/appsettings.Example.json` for a full list of keys, then store real values in user secrets:

```bash
dotnet user-secrets set "ConnectionStrings:AzureSql" "Server=tcp:..." --project src/CruxAI.Web
dotnet user-secrets set "Storage:AzureBlob:ConnectionString" "DefaultEndpointsProtocol=https;..." --project src/CruxAI.Web
dotnet user-secrets set "Auth:EntraExternalId:TenantId" "..." --project src/CruxAI.Web
dotnet user-secrets set "Auth:EntraExternalId:ClientId" "..." --project src/CruxAI.Web
dotnet user-secrets set "Auth:EntraExternalId:ClientSecret" "..." --project src/CruxAI.Web
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://YOUR_RESOURCE.openai.azure.com/" --project src/CruxAI.Web
dotnet user-secrets set "AzureOpenAI:DeploymentName" "your-deployment" --project src/CruxAI.Web
dotnet user-secrets set "AzureOpenAI:ApiKey" "..." --project src/CruxAI.Web
```

The Web project already has a `UserSecretsId`. User secrets live on the developer machine, not in the repo.

HTTPS is available via `--launch-profile https` once the ASP.NET Core developer certificate is trusted.

## Auth placeholder

Demo login is intentional so the CSV + KPI + why + morning brief flow can be exercised without an Entra tenant. Replacing it:

1. Add `Microsoft.Identity.Web`.
2. Fill `Auth:EntraExternalId` via user-secrets.
3. Follow the commented block in `src/CruxAI.Web/Program.cs`.
4. Map the Entra `oid` onto `AppUser.ExternalId` and keep using `Membership` for org access.

## Marketing site

Public landing page: [https://tinashealvin.github.io/crux-ai/](https://tinashealvin.github.io/crux-ai/) — source in [`docs/`](docs/).

## License / product

Product name: **Crux AI**.
