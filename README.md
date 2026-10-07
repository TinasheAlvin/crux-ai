# Vhona AI

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

Local demo defaults (no Azure account required):

- Cookie **demo auth** (`Auth:Provider` = `Demo`)
- **SQLite** (`Database:Provider` = `Sqlite`)
- Local filesystem CSV storage (`Storage:Provider` = `Local`)

Hosted demo, when those settings are switched (see [Host on Azure](#host-on-azure)):

- Microsoft Entra External ID (`Auth:Provider` = `EntraExternalId`)
- Azure SQL (`Database:Provider` = `AzureSql`) — transactions, KPI inputs, why citations, morning brief rows
- Azure Blob (`Storage:Provider` = `AzureBlob`) — the CSV bytes
- Azure OpenAI stays optional (`AzureOpenAI:*` — if Endpoint / DeploymentName / ApiKey are set, it may classify *which KPI* a free-text question is about. It never authors amounts. Without keys, the deterministic verifier runs alone.)

## Solution layout

```
src/VhonaAI.sln
src/VhonaAI.Core/              Domain, mapping, validation, health KPI calculator, why verifier, brief composer, analytics contracts
src/VhonaAI.Application/       Service interfaces the Blazor UI calls (a later HTTP API and MCP server can reuse them)
src/VhonaAI.Infrastructure/    EF Core, CSV and Excel readers, file storage, import + health + why + morning brief + event log
src/VhonaAI.Web/               Blazor UI + demo auth + partner dump endpoint
src/VhonaAI.Tests/
testdata/sample-transactions.csv
testdata/sample-transactions-with-cash.csv
testdata/sample-transactions-clean.csv
```

## Run locally

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet restore src/VhonaAI.sln
dotnet build src/VhonaAI.sln
dotnet test src/VhonaAI.sln
dotnet run --project src/VhonaAI.Web --launch-profile http
```

Open http://localhost:5028

The first run creates `src/VhonaAI.Web/App_Data/vhonaai.db` and `src/VhonaAI.Web/App_Data/uploads/` (gitignored). Startup applies EF Core migrations. A database left by the old build-schema-on-first-start path is stamped with the baseline migration and kept; only later migrations run. Delete `vhonaai.db` if you want an empty local file.

### Demo path (CSV map → health KPIs → receipted why → morning brief)

1. Click **Continue as demo owner**. This signs you in with a cookie and creates:
   - Organisation: `Harbour Street Studio`
   - User: `owner@harbourstreet.local`
   - Owner membership
2. Click **Use sample CSV** (or upload `testdata/sample-transactions.csv`). For a faster happy path with no broken cells, use `testdata/sample-transactions-clean.csv`.
3. Confirm auto-guessed columns, then persist. The default sample has Feb + Mar 2026 rows and a few broken March cells — fix those in place, then persist.
4. You land on **health KPIs**: revenue, expenses, and profit for the latest month in the file vs the previous month.
5. **Cash is hidden** on this sample — there is no balance column, so Vhona never shows a fake R0 cash card.
6. Tap a KPI card. Vhona seeds “Why did this change?” and answers from the Transaction store. The answer includes a **receipt** of the exact persisted RowIds (and columns such as Date, Description, Amount).
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
cat src/VhonaAI.Web/App_Data/partner-events.jsonl
```

Count by name:

```bash
python3 -c "import json,collections,pathlib
p=pathlib.Path('src/VhonaAI.Web/App_Data/partner-events.jsonl')
c=collections.Counter(json.loads(l)['name'] for l in p.read_text().splitlines() if l.strip())
print('\n'.join(f'{n:4} {k}' for k,n in c.most_common()))"
```

While signed in, `GET http://localhost:5028/internal/partner-events` returns JSON for **that business only**. A signed-in person with no business gets 403. Emails listed in `Auth:InternalAdminEmails` see every business.

If you set `Analytics:Sink` to `Sqlite`:

```bash
sqlite3 src/VhonaAI.Web/App_Data/partner-events.db \
  "SELECT name, org_id, user_id, timestamp, properties_json FROM partner_events ORDER BY id;"
```

Each JSONL line looks like:

```json
{"name":"finishes_upload","orgId":"...","userId":"...","timestamp":"2026-09-16T21:04:00.0000000Z","properties":{"importJobId":"...","rowCount":"12"}}
```

The file lives under gitignored `App_Data/`. Delete it between partner sessions if you want a clean dump.

## Configuration and secrets

Committed files contain **placeholders only**. Do not put real connection strings, client secrets, or account keys in git.

| Setting | Local demo | Hosted demo |
| --- | --- | --- |
| `Database:Provider` | `Sqlite` | `AzureSql` |
| `ConnectionStrings:Sqlite` | `Data Source=App_Data/vhonaai.db` | unused |
| `ConnectionStrings:AzureSql` | empty | App Service setting or Key Vault reference |
| `Storage:Provider` | `Local` | `AzureBlob` |
| `Storage:LocalRoot` | `App_Data/uploads` | unused |
| `Storage:AzureBlob:ConnectionString` | empty | App Service setting or Key Vault reference |
| `Storage:AzureBlob:ContainerName` | `csv-uploads` | `csv-uploads` |
| `Storage:DataProtection:ContainerName` | `vhona-keys` | `vhona-keys` (auth keys, only when storage is Azure Blob) |
| `Auth:Provider` | `Demo` | `EntraExternalId` for the timed path. `Demo` still works on the host if Entra is not filled in. |
| `Auth:DefaultOrganizationName` | demo-owner fallback only | `Harbour Street Studio`. Entra sign-in does not use it. |
| `Auth:InternalAdminEmails` | empty | comma-separated emails that may read every business's partner events |
| `Auth:EntraExternalId:*` | placeholders | App Service settings. `ClientSecret` is a secret. |
| `DemoAuth:*` | Harbour Street Studio owner | unused once Entra is the provider |
| `AzureOpenAI:Endpoint` | placeholder / empty | user-secrets or App Service setting |
| `AzureOpenAI:DeploymentName` | placeholder / empty | user-secrets or App Service setting |
| `AzureOpenAI:ApiKey` | empty | user-secrets or App Service setting |
| `Analytics:Sink` | `File` (JSONL) | `File` |
| `Analytics:FilePath` | `App_Data/partner-events.jsonl` | `/home/vhona/partner-events.jsonl` on App Service |
| `Analytics:SqlitePath` | `App_Data/partner-events.db` | unused on the host |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | unset | set only when App Insights is enabled |

Copy `src/VhonaAI.Web/appsettings.Example.json` for a full list of keys, then store real values in user secrets:

```bash
dotnet user-secrets set "ConnectionStrings:AzureSql" "Server=tcp:..." --project src/VhonaAI.Web
dotnet user-secrets set "Storage:AzureBlob:ConnectionString" "DefaultEndpointsProtocol=https;..." --project src/VhonaAI.Web
dotnet user-secrets set "Database:Provider" "AzureSql" --project src/VhonaAI.Web
dotnet user-secrets set "Storage:Provider" "AzureBlob" --project src/VhonaAI.Web
dotnet user-secrets set "Auth:Provider" "EntraExternalId" --project src/VhonaAI.Web
dotnet user-secrets set "Auth:EntraExternalId:Instance" "https://YOUR_TENANT.ciamlogin.com/" --project src/VhonaAI.Web
dotnet user-secrets set "Auth:EntraExternalId:Domain" "YOUR_TENANT.onmicrosoft.com" --project src/VhonaAI.Web
dotnet user-secrets set "Auth:EntraExternalId:TenantId" "..." --project src/VhonaAI.Web
dotnet user-secrets set "Auth:EntraExternalId:ClientId" "..." --project src/VhonaAI.Web
dotnet user-secrets set "Auth:EntraExternalId:ClientSecret" "..." --project src/VhonaAI.Web
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://YOUR_RESOURCE.openai.azure.com/" --project src/VhonaAI.Web
dotnet user-secrets set "AzureOpenAI:DeploymentName" "your-deployment" --project src/VhonaAI.Web
dotnet user-secrets set "AzureOpenAI:ApiKey" "..." --project src/VhonaAI.Web
```

The Web project already has a `UserSecretsId`. User secrets live on the developer machine, not in the repo.

HTTPS is available via `--launch-profile https` once the ASP.NET Core developer certificate is trusted.

## Host on Azure

The hosted demo is the same spine on a public HTTPS URL. App Service Linux (.NET 8, zip deploy) is the host: Blazor Interactive Server needs WebSockets and sticky sessions (ARR affinity), and that does not need a container registry. Azure SQL Basic holds transactions, citations, and briefs. A StorageV2 account (Standard LRS) holds the CSV container `csv-uploads` and the data-protection container `vhona-keys`. The database stays on the Basic tier (not serverless) so the first request is not waiting for a paused database.

Region default is **South Africa North**. The plan default is **B1** (smallest Linux size with Always On). A staging slot raises the plan to **S1**, because slots are a Standard feature. Key Vault and Application Insights are off unless you opt in.

`infra/main.parameters.json` does not contain passwords. Pass `sqlAdminPassword` on the command line. The password needs Azure SQL complexity (12+ characters, upper, lower, digit, symbol) and must not contain `;`.

```bash
az group create --name vhona-ai-demo --location southafricanorth
az deployment group create \
  --resource-group vhona-ai-demo \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json \
  --parameters sqlAdminPassword='REPLACE_WITH_A_STRONG_PASSWORD'
```

The deployment outputs `webAppName` and `healthUrl`. Copy `webAppName` into the GitHub secret `AZURE_WEBAPP_NAME`.

Flip providers without editing the template by changing App Service settings (these are what the Bicep template sets for a hosted site):

| App setting | Hosted value |
| --- | --- |
| `Database__Provider` | `AzureSql` |
| `ConnectionStrings__AzureSql` | SQL connection string, or a Key Vault reference |
| `Storage__Provider` | `AzureBlob` |
| `Storage__AzureBlob__ConnectionString` | storage connection string, or a Key Vault reference |
| `Storage__AzureBlob__ContainerName` | `csv-uploads` |
| `Auth__Provider` | `EntraExternalId` or `Demo` |
| `Auth__EntraExternalId__Instance` | `https://<tenant>.ciamlogin.com/` |
| `Auth__EntraExternalId__TenantId` | directory (tenant) id |
| `Auth__EntraExternalId__ClientId` | app registration client id |
| `Auth__EntraExternalId__ClientSecret` | client secret |
| `Auth__DefaultOrganizationName` | demo-owner name only; Entra users name their own business |
| `Auth__InternalAdminEmails` | optional; partner-event access across businesses |

Empty, `YOUR_*`, and `<TODO-...>` values are rejected at startup when that provider is selected. A literal `@Microsoft.KeyVault(...)` value means the reference was not resolved: grant the app identity secret **get**, then restart.

To exercise Azure SQL and Blob before the Entra tenant exists, deploy with `authProvider=Demo` (the committed parameters file does this). `GET /healthz` then reports `database: AzureSql`, `storage: AzureBlob`, `auth: Demo`, `sampleCsv: true`. The five-minute bar includes Entra sign-in, so switch `authProvider` once the app registration exists:

```bash
az deployment group create \
  --resource-group vhona-ai-demo \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json \
  --parameters sqlAdminPassword='REPLACE_WITH_A_STRONG_PASSWORD' \
  --parameters authProvider=EntraExternalId \
  --parameters entraInstance='https://YOUR_TENANT.ciamlogin.com/' \
  --parameters entraDomain='YOUR_TENANT.onmicrosoft.com' \
  --parameters entraTenantId='YOUR_TENANT_ID' \
  --parameters entraClientId='YOUR_CLIENT_ID' \
  --parameters entraClientSecret='YOUR_CLIENT_SECRET'
```

Redirect URIs on that app registration:

- `https://<webAppName>.azurewebsites.net/signin-oidc`
- `https://<webAppName>.azurewebsites.net/signout-callback-oidc`

Issue ID tokens. Add optional claims **email** and **name**. The sign-in link is a full page navigation (`/auth/signin`) so the Blazor circuit is created after the cookie exists.

### Key Vault (optional)

`--parameters enableKeyVault=true keyVaultAdminObjectId=$(az ad signed-in-user show --query id -o tsv)`

The template writes `sql-connection`, `blob-connection`, and (when set) `entra-client-secret`, then points the app settings at unversioned secret URIs. The web app's system identity gets secret get/list. Without Key Vault, the same values live only in App Service settings.

### Application Insights (optional)

`--parameters enableAppInsights=true` adds a 30-day Log Analytics workspace and sets `APPLICATIONINSIGHTS_CONNECTION_STRING`. Leave it off for the lean demo. Telemetry is not registered when the connection string is empty.

### Staging slot (optional)

`--parameters enableStagingSlot=true` creates a `staging` slot and, on a Basic plan, moves the SKU to S1. Deploy by setting the GitHub secret `AZURE_WEBAPP_SLOT` to `staging`, or run the workflow manually with input `slot=staging`. The slot shares the demo database and storage account. Swap in the portal when you want staging to become production. B1 has no slots.

### GitHub Actions

Workflow: `.github/workflows/azure-demo.yml`.

On every pull request and on `main`: restore, `dotnet test`, publish `src/VhonaAI.Web`, check the sample CSVs are in the publish folder, and compile `infra/main.bicep`.

Deploy runs only when the repository variable `AZURE_DEPLOY_ENABLED` is `true`, and only on a push to `main` or a manual run. Until that variable is set, a missing Azure subscription does not fail the build.

GitHub secrets:

| Secret | Purpose |
| --- | --- |
| `AZURE_CREDENTIALS` | JSON from the deploy service principal (`az ad sp create-for-rbac --sdk-auth`) |
| `AZURE_WEBAPP_NAME` | `webAppName` output from the Bicep deployment |
| `AZURE_WEBAPP_SLOT` | Optional. `staging` to deploy the slot. Empty deploys production. |

Repository variable:

| Variable | Purpose |
| --- | --- |
| `AZURE_DEPLOY_ENABLED` | Set to `true` after the resource group and secrets exist |

Connection strings are not GitHub secrets. Bicep writes them to App Service (or Key Vault). Do not echo them into the workflow.

```bash
az ad sp create-for-rbac \
  --name vhona-ai-deploy \
  --role contributor \
  --scopes /subscriptions/<subscription-id>/resourceGroups/vhona-ai-demo \
  --sdk-auth
```

Put the JSON document in `AZURE_CREDENTIALS`. Scope it to the demo resource group.

Publish path used by the workflow and by a manual zip deploy:

```bash
dotnet publish src/VhonaAI.Web/VhonaAI.Web.csproj -c Release -o ./publish
```

`publish/VhonaAI.Web.dll` is the App Service entry point (`DOTNETCORE|8.0`). `WEBSITE_RUN_FROM_PACKAGE=1`. Sample CSVs are published under `testdata/` so **Use sample CSV** works on the host.

### Entra and the five-minute path

Cold sample CSV → first trusted receipted why is meant to stay within five minutes, sign-in included. These are the delays that blow that budget:

1. **Email verification / sign-up.** Create the demo user in the External ID tenant first and sign in with the password. A user flow that waits on a mailbox will not finish in five minutes.
2. **Wrong authority.** External ID uses `https://<tenant>.ciamlogin.com/`. `https://login.microsoftonline.com/` is a workforce tenant and will not complete this sign-in.
3. **Redirect URI mismatch.** The reply URL must be `https://<app>.azurewebsites.net/signin-oidc` on the same host the browser is using. A missing URI sends the user back to `/login` with an error.
4. **Missing email claim.** Startup and sign-in both fail closed without an email. Add the optional email claim and the `email` scope. The login page shows the error instead of opening a half-signed-in session.
5. **Placeholder client secret.** `Auth:Provider=EntraExternalId` with an empty or `<TODO-...>` secret refuses to boot. `/healthz` will not come up until the secret is real.
6. **Client secret expiry.** A later demo fails at the IdP with a login error, not inside the CSV flow.
7. **Extra profile attributes** on the user flow. Each extra screen is time. Keep the flow to existing-user sign-in.
8. **In-circuit navigation to sign-in.** The Sign in control opts out of Blazor enhanced navigation. Replacing it with `NavigateTo` without a full page load leaves the circuit anonymous.
9. **Cold start.** The first boot runs EF migrations against Azure SQL Basic. An existing database is stamped at the baseline and kept. Basic does not pause. A serverless database can spend the whole five minutes waking up. B1 Always On keeps the process up after that first boot.
10. **Auth cookie lost on restart.** Data-protection keys are stored in the `vhona-keys` blob when storage is Azure Blob, so a restart does not drop the session in the middle of map → why.
11. **ARR affinity off.** Blazor Server drops the circuit if the next request lands on another instance. The template turns client affinity and WebSockets on, and the plan is one worker.
12. **`/healthz` still says `auth: Demo`.** Blob and SQL are live, but the timed path has not started. Set `Auth__Provider` to `EntraExternalId` and fill the Entra settings.

`/healthz` does not touch the database. It reports the active providers and whether the clean sample CSV was published. The product Health page stays at `/health`. Partner events are unchanged: `finishes_upload`, `asks_why_session_one`, `rates_explanation_trustworthy`, `returns_for_brief_within_7_days`, `pay_or_waitlist_signal`, plus `map_abandon`, `receipt_open`, and `receipt_distrust`. On the host they append to `/home/vhona/partner-events.jsonl` (Kudu SSH). While signed in, `GET /internal/partner-events` returns that business's events. `Auth:InternalAdminEmails` is the only path that returns every business.

### Manual smoke on the hosted URL

`dotnet test src/VhonaAI.sln` is the automated check. This is the manual check after deploy. Use the clean sample. Time it from the sign-in click.

1. `curl https://<webAppName>.azurewebsites.net/healthz` returns `status: ok`, `database: AzureSql`, `storage: AzureBlob`, `sampleCsv: true`, and `auth` equal to the provider you deployed.
2. Sign in. Entra: one existing user, full-page Sign in. The first visit asks for a business name; a later sign-in keeps that name. Demo: **Continue as demo owner** (Harbour Street Studio).
3. **Clean sample** (or **Use sample CSV**). Confirm the guessed columns in one tap and persist. Fix any broken cells in place if you used the default sample. Do not restart.
4. Health shows revenue, expenses, and profit for the latest month versus the previous month. Cash is hidden on the clean sample. It appears only for **Sample with cash**, and it is never a fake zero.
5. Tap a KPI. The answer includes a receipt of exact RowIds. Open the receipt and see those rows.
6. Refresh Why. The same receipt is still there (it was read from Azure SQL, not from the browser).
7. In storage, the container `csv-uploads` has a blob under `{org}/{import}/`. That is the file the import reopens.
8. Ask something the verifier cannot cite. The page says **Can't verify that yet.** There is no answer body and no streamed draft.
9. After the cited why, choose **Send me the morning brief**. Sign out and sign in again (next visit). Home shows yesterday's snapshot plus one receipted explanation, or **No verified brief today** with a path back to **Ask why**.
10. While signed in, `GET /internal/partner-events` includes the signals from this business (`finishes_upload`, and `receipt_open` if you opened the receipt). It does not include another business's events.

Local `dotnet run --project src/VhonaAI.Web --launch-profile http` is unchanged: Demo, SQLite, and `App_Data/uploads`. User-secrets override those defaults on that machine only. Set the three providers back to `Demo`, `Sqlite`, and `Local` to return to the local path.

## Marketing site

Public landing page for **Vhona AI**: [https://tinashealvin.github.io/vhona-ai/](https://tinashealvin.github.io/vhona-ai/). Source in [`docs/`](docs/). That GitHub Pages path follows the repository name. Until `TinasheAlvin/crux-ai` is renamed to `TinasheAlvin/vhona-ai`, the live Pages site remains [https://tinashealvin.github.io/crux-ai/](https://tinashealvin.github.io/crux-ai/).

Cloudflare Pages may still serve [https://crux-ai.pages.dev](https://crux-ai.pages.dev) until that project is renamed. See [Names that stay](#names-that-stay).

## Names that stay

Azure cannot rename a resource in place, and this repo does not call Azure to do it. `infra/main.bicep` defaults are for the **next** resource group (`namePrefix` `vhonaai`, SQL login `vhonaadmin`, database `vhonaai`, blob container `vhona-keys`, partner events `/home/vhona/partner-events.jsonl`, tag `project=vhona-ai`).

If the demo was already deployed, these names are still the live ones. Keep passing them (or leave the existing App Service settings) instead of redeploying the new defaults into the same group:

| What | Already deployed name |
| --- | --- |
| Resource group | `crux-ai-demo` |
| Name prefix (web app, plan, SQL server, storage, vault, insights) | `cruxai` |
| SQL admin login | `cruxadmin` |
| SQL database | `cruxai` |
| Data-protection blob container | `crux-keys` |
| Partner event file on App Service | `/home/crux/partner-events.jsonl` |
| Deploy service principal example | `crux-ai-deploy` |
| GitHub Actions artifact (previous) | `crux-web` |

The workflow deploy step still uses the secret `AZURE_WEBAPP_NAME`, so a code deploy keeps hitting the existing app. The published entry point is now `VhonaAI.Web.dll`. A custom startup command that still names `CruxAI.Web.dll` has to change. Data-protection application name is `vhona-ai` and the auth cookie is `vhonaai.auth`, so existing browser sessions sign in again after this build. Local SQLite moves to `App_Data/vhonaai.db` (delete the old `cruxai.db` if you want a clean file). The Web `UserSecretsId` is `vhonaai-web-local-dev`; secrets stored under the old id are not picked up.

GitHub repository rename (owner, Settings — this change set cannot rename the repo):

1. Open [https://github.com/TinasheAlvin/crux-ai/settings](https://github.com/TinasheAlvin/crux-ai/settings).
2. Under **General**, set **Repository name** to `vhona-ai` and confirm **Rename**.
3. GitHub redirects `TinasheAlvin/crux-ai` links. Update a local remote with `git remote set-url origin https://github.com/TinasheAlvin/vhona-ai.git`.
4. Pages → confirm **Deploy from a branch**, `main`, folder `/docs`. The project site becomes [https://tinashealvin.github.io/vhona-ai/](https://tinashealvin.github.io/vhona-ai/).

The same rename from an admin shell is `gh repo rename vhona-ai --repo TinasheAlvin/crux-ai --yes`.

For Benaiah: Cloudflare Pages project **crux-ai** can keep answering at [https://crux-ai.pages.dev](https://crux-ai.pages.dev) after the GitHub rename. In the Cloudflare dashboard, Workers & Pages → **crux-ai** → Settings, rename the project if the `pages.dev` hostname should become `vhona-ai.pages.dev`, and update any custom domain or DNS that still targets the old project. The old `pages.dev` name does not follow the GitHub repository automatically.

## License / product

Product name: **Vhona AI**.
