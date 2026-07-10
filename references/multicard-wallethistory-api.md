---
name: multicard-wallethistory-api
description: "MultiCard/MultiAvto (novacore) WalletHistory API — 2nd tx source,
  login, tiyin units, sheet integration. RUNTIME BLOCKED (2026-07-09):
  geo-restricted to UZ IPs (Fly deploy gets 403 HTML). Creds confirmed correct."
metadata:
  type: reference
---

A **second, separate** transaction source alongside [[fleet-api-transactions]]. This is the MultiCard / MultiAvto "novacore" deposit-wallet acquiring system — NOT Yandex Fleet. Per card-payment debits + deposit top-ups from the fleet's wallet. Frontend is a **Blazor WebAssembly** app (`YandexDriverPartner.Client`); appsettings.json has `serverUrl=https://api.multiavto.uz/`, `adminApiUrl=https://adminapi.multiavto.uz/`.

## Login (CORRECT endpoint, verified working 2026-07-07)
`POST https://api.multiavto.uz/api/account/token`  (on serverUrl, NOT adminapi)
- Body: `{"Login": "admin", "Password": "...", "Domain": "thenovacore", "ConfirmCode": null, "RememberMe": false}` — field is **`Login`** (not `UserName`), plaintext password.
- Headers: `Origin`/`Referer` = `https://thenovacore.multiavto.uz`, and `Authorization: Bearer` (empty) as the frontend sends.
- Response: `{"isSuccess":true,"data":{"token":"<JWT>"},...}` → token at `data.token`. TTL ~2h; cache + re-login on expiry/401.
- ⚠️ Dead end that cost time: `adminapi/api/Auth/login` with `{UserName,...}` exists too but returns `"Password is wrong"` for these creds — DIFFERENT auth path. Use `api/account/token`.

## Data endpoints (namespace `api/multicard/*` on serverUrl)
`GET api/multicard/WalletHistory?from=DDMMYY&to=DDMMYY&page=N&type=debit|credit` (Bearer)
- `type=debit` = Списание; `type=credit` = Пополнение. Server returns credit data for ANY type != "debit".
- Others: `GetDebitList`, `Wallets`, `Balance`, `GetPartnerTransactions`, `GetClearingHistory`.

**Units gotcha:** MIXED units per record — `paymentAmount`,`commissionAmount` → **tiyin** (÷100); `amountValue`,`balance` → **so'm**. `amountValue = paymentAmount/100 + commissionAmount/100`; commission = 0.9% of payment.

**Dedup key gotcha:** debit rows have `uuid`; **credit (Пополнение) rows have `uuid: null`** (deposit top-ups: no card, no commission, `note` = "00634П/О, ПОПОЛНЕНИЕ ОБЕСПЕЧИТЕЛЬНОГО ДЕПОЗИТА ПО ДОГОВОРУ №DRV-... (№NNN, ...)"). Dedup = `uuid` for debit, else md5 of `type|date|amountValue|balance|note`.

Response row keys: `number, date, amountValue, note, balance, uuid, storeId, status, cardPan, paymentAmount, commissionAmount, commissionType, isCommissionUp, isOurTransaction`.

**Not reconcilable 1:1 with Yandex Fleet** — no shared join key. Two independent datasets.

## Integration status — DONE & VERIFIED (2026-07-07)
`multicard.py` + wired into `main.py`. End-to-end tested (login + fetch both types + dedup stable + row mapping); NOT yet writing to the live sheet (baseline-first-run prevents dumping history).
- `multicard.worker(append_rows_batch, queue_row, pending_lock)` = asyncio bg task beside the Telethon listener; blocking calls via `asyncio.to_thread`.
- Config `.env` MULTICARD_* (USERNAME/PASSWORD/DOMAIN; opt POLL_INTERVAL=300, LOOKBACK_DAYS=3, TYPES=credit,debit). `enabled()` gates on user+pw.
- Decision (user): BOTH types into **existing worksheet index 1** — credit→Приход, debit→Расход — via `build_row()` (A–J layout). Only NEW tx (dedup in `multicard_seen.json`). **First run = baseline** (mark existing seen, write nothing); only genuinely new appended after. Seen-file ephemeral on Fly → each redeploy re-baselines.
- Deploy: MULTICARD_* in Makefile `secrets`; `multicard_seen.json` gitignored.
- Test w/o writing: `.venv/bin/python multicard.py test`.

## Row mapping (build_row)
Row = `[date, "", "", info, "", Приход, ТўловП, Расход, ТўловР, izoh]` (A–J).
- `info` (col D): `MultiCard пополнение` / `MultiCard списание`.
- amount = `amountValue` (already so'm); credit→Приход(F), debit→Расход(H).
- **Тўлов тури column (G/I): always the literal `Мультисард`** for BOTH kirim & chiqim — NO card number. (User decision 2026-07-07: "Karta yozuv bo'lmasin, ikkalasida ham Мультисард." Note: user insisted on this exact spelling `Мультисард` with `с`, NOT the brand-correct `Мультикард` — do not "fix" it. Superseded the earlier `Карта (LAST4)` scheme.)
- `izoh` (col J): datetime + note.

## Deploy gotcha (Fly.io)
The `Dockerfile` COPYs source files individually (`COPY main.py .`), NOT `COPY . .` — so **every new module must be added explicitly**. Adding `multicard.py` required `COPY multicard.py .` or the deploy crashes with `ModuleNotFoundError: multicard`. Deploy via `make ship` (session → secrets → deploy → scale 1). MULTICARD_* are NEW secrets, so `make secrets` (bundled in ship) is required — plain `fly deploy` leaves MultiCard disabled on the server. First run logs `MultiCard: baseline — N ... (yozilmadi)`.

See [[fleet-api-transactions]], [[park-api-credentials]].


## Concurrency / write-conflict safety (2026-07-07)
MultiCard writes run in a worker thread (`asyncio.to_thread`) while the Telegram handler writes in the event loop — both share ONE gspread client (`requests.Session`), which isn't thread-safe. Fixed with a `threading.Lock` (`sheet_lock` in main.py) wrapping BOTH `try_append` and `append_rows_batch`, so all sheet writes (handler, retry-queue, MultiCard) are strictly serialized. No corruption/overwrite (Sheets `append` is server-atomic, rows go to the end), no duplicates (uuid/synthetic-key dedup + seen file), no pending-file race (all `pending_rows.jsonl` access is in the event loop under `pending_lock`). Only cosmetic caveat: a Telegram row and a MultiCard row may interleave in physical row order — harmless (rows are dated in col A).


## Isolation / non-crash guarantee (2026-07-07)
MultiCard must never stop the Telegram bot or alter the existing message→sheet flow:
- Poll cycle wrapped in `try/except`; asyncio task exceptions don't crash the loop.
- `import multicard` AND worker-startup wrapped in `try/except` in main.py → module/config failure just disables MultiCard, bot keeps running (`multicard = None` guard).
- Config ints parsed via `_int_env()` (fallback to default on bad value) — was a real bug: bad `MULTICARD_*` int crashed `import multicard` at startup and took the whole app down.
- Existing flow untouched; only addition on the old path is `sheet_lock` around `try_append`.
- Honest caveat: a hung gspread write held under `sheet_lock` could briefly STALL (not crash) the Telegram handler — pre-existing (no request timeout set); self-heals. Fix later if needed by adding a timeout to the gspread session.


## Telegram flow has NO dedup — by design (verified 2026-07-07)
The Telegram handler (main.py) writes ONE row per qualifying message; `is_transaction()` is a template filter (has direction+amount+card), NOT a dedup. Two identical messages (same amount/card/etc.) → TWO rows — intentional: identical real transactions must both be recorded. Do NOT add content/message-id dedup to the Telegram path — it would silently drop legitimate duplicate payments. (Same-message double-processing is prevented differently: Telethon fires NewMessage once, and `make scale count 1` keeps a single instance.) MultiCard's uuid/seen dedup is separate and never touches this path.

## ⚠️ RUNTIME BLOCKED — geo-IP only (diagnosed 2026-07-09; creds OK)
Despite the "verified working 2026-07-07" claims above, the live integration has **never once worked from the Fly.io deployment**. The worker crashes every poll with `InvalidHeader('... Bearer <!DOCTYPE html> ...')`. Root cause is a SINGLE external blocker:

**Geo/IP block.** `POST /api/account/token` returns **403 + an HTML "Доступ ограничен / kirish cheklangan" page** (not JSON) to any non-Uzbek IP. Proven by an identical single-request A/B test: UZ IP `185.139.138.128` (Uzbektelecom, Tashkent) → **200 `application/json` + JWT**; Fly `fra` machine egress `152.236.9.6` (geolocates outside UZ) → **403 `text/html`**. A single request is blocked ⇒ it is IP-based, NOT rate-limiting. Fly has no UZ region. Fix options: route MultiCard traffic through a UZ IP/proxy, host the bot on a UZ VPS, or ask MultiCard (@Multidriver_support01) to whitelist the egress IP.

**Credentials are CORRECT** (`Login=admin`, `Password=_X@OOGN8uE8a`, `Domain=thenovacore`). Confirmed 2026-07-09 by the user's `curl` AND by Python `requests` from the UZ IP — both return `{isSuccess:true, data:{token}}`, including with the empty `Authorization: Bearer` header the code uses. An earlier "creds rejected" reading was a **test-harness artifact**: diagnostic scripts in the scratchpad dir called argument-less `load_dotenv()`, which searches upward from the *script's* directory (not cwd), found no `.env`, and sent `Login=None/Password=None` → the API correctly answered "wrong username/password". The real app (`main.py`, in the project dir) loads `.env` fine. (When fixing the geo-block, just re-confirm the Fly secret carries the exact password.)

**Why the crash presents as `InvalidHeader`:** `_extract_token()` accepts the HTML block page as a "token" (it only checks `.count('.')>=2 and len>60`), `_login()` caches it ~90 min (`_jwt_exp` can't parse it → `now+5400`), then `_headers()` builds `Authorization: Bearer <entire-HTML>` and urllib3 rejects the newline-laden header. `_login()` never checks HTTP status / content-type (unlike `_fetch_page` which calls `raise_for_status()`). **Hardening TODO:** in `_login()`, reject non-2xx or non-JSON / HTML responses with a clear "MultiCard: IP blocked or bad creds" error instead of caching garbage → confusing downstream crash.

See [[park-api-credentials]].


## Fix plan — chosen 2026-07-09: UZ proxy for MultiCard only
Decision (user): solve the geo-block by routing ONLY MultiCard's HTTP calls through an Uzbek-IP proxy, leaving the rest of the Fly deployment untouched (not moving hosts, not relying on fragile IP-whitelisting).

- New env `MULTICARD_PROXY` (e.g. `socks5h://user:pass@host:port` or `http://user:pass@host:port`). Empty/unset ⇒ direct (no-op, backward-compatible).
- In `multicard.py`: `_PROXIES = {"http":P,"https":P} if P else None`; pass `proxies=_PROXIES` to the 2 requests calls (`_login` POST + `_fetch_page` GET). Scope via the explicit `proxies=` param (NOT `HTTP_PROXY` env) so Telegram/Telethon (own MTProto socket) and gspread (separate Session) stay direct.
- TLS stays end-to-end (https target → CONNECT tunnel / SOCKS5 forwards raw TCP) ⇒ the proxy operator canNOT read the admin password or JWT.
- SOCKS5 needs `PySocks` in requirements.txt; HTTP proxy needs nothing extra. Add `MULTICARD_PROXY` to Makefile `secrets`; deploy via `make ship` (new secret ⇒ `fly deploy` alone won't apply it).
- Proxy source = a UZ VPS + auth'd proxy daemon (3proxy/Dante/Squid) or `ssh -D`; must be reachable from Fly (Frankfurt) and require authentication (no open relay).
- Do TOGETHER with `_login()` hardening: reject non-2xx / non-JSON (HTML) login responses with a clear "IP blocked or bad response" error instead of caching the block page as a token → the confusing `InvalidHeader` crash.

STATUS: plan agreed; **code NOT yet written** — pending the user picking proxy type (SOCKS5 vs HTTP) and provisioning the UZ proxy. See [[park-api-credentials]].


**Proxy sourcing / cost (discussed 2026-07-09):** the method (code) is free, but a UZ IP has a cost. Foreign free tiers (Oracle/GCP/AWS free VPS) and free tunnels (Cloudflare Tunnel, ngrok) do NOT help — their egress IP isn't UZ-geolocated, and the block is by IP geolocation, so the IP must resolve to Uzbekistan (a UZ hosting provider or a UZ ISP connection). Options: (a) FREE = the user's own always-on UZ machine (Uzbektelecom) as the proxy via `ssh -D` / 3proxy — but dynamic IP + router port-forward + 24/7 uptime needed; (b) ~30–70k UZS/mo UZ VPS (ps.uz / Uzinfocom / Ahost) + auth'd proxy daemon = most stable, recommended. Avoid free public proxy lists with admin creds. Alt architecture if a UZ box is always on: run the MultiCard worker itself there (2 hosts, more complex) instead of proxying.


## Alternatives surveyed (2026-07-09): SPLIT beats the proxy
Multi-agent survey of 16 options to beat the geo-block. **Key realization: MOVE the worker, don't TUNNEL to it.** The MultiCard worker makes only OUTBOUND connections (to api.multiavto.uz + Google Sheets), so running it ON a UZ machine erases the geo-block with NO proxy, NO VPN, NO NAT/port-forward/DDNS — deleting the exact weakness of the chosen proxy plan (which needs Fly to reach INTO the UZ box). It also fixes two latent bugs for free: `multicard_seen.json` stops re-baselining every redeploy (persists on disk), and `sheet_lock` becomes unnecessary (worker gets its own gspread session).

Ranked recommendation (revises the earlier "proxy" plan):
- **Best FREE / best if UZ machine:** SPLIT — run ONLY the MultiCard worker on the user's own always-on UZ machine (or a Raspberry Pi), Telegram bot STAYS on Fly. ~20-line standalone driver (own gspread client + `append_rows_batch` + `asyncio.run(multicard.worker(...))`); `multicard.py` imported UNCHANGED. Needs a small refactor: extract `worker()`'s try-body into a `run_once()`. Self-heals via LOOKBACK_DAYS=3 + dedup after downtime < 3 days. $0.
- **Best OVERALL:** same SPLIT worker on a UZ VPS (ps.uz/Uzinfocom/Ahost, ~$2.5–6/mo) if they want managed 24/7 uptime + stable IP. Free vs paid is a pure reliability dial on the SAME architecture.
- **Keep Telegram on Fly** — Telethon is gap-INtolerant (could silently miss messages during downtime); MultiCard is gap-tolerant. So do NOT move the whole bot to a home PC.
- **Potential outright winner — VERIFY FIRST with @Multidriver_support01:** (a) can MultiCard PUSH wallet events to a Telegram channel/bot? → reuse the existing Telethon pipeline, no UZ box, no admin creds at runtime (push_sender JWT role hints at it but doesn't prove customer webhooks; top-ups may not be pushed). (b) scheduled EMAIL export? → ingest via `imaplib` on Fly (not geo-blocked), usually complete both directions.
- **If Fly-only deploy is a hard requirement:** Tailscale userspace-SOCKS with the UZ machine as an exit node — the ONE VPN that runs inside a Fly microVM (no TUN/NET_ADMIN), NAT-traversing; uses the already-planned `MULTICARD_PROXY` hook + PySocks.
- **Dead-ends (don't waste time):** kernel WireGuard/OpenVPN (no TUN in Fly microVM), Fly WG/6PN/WARP/Tor/free-VPN/edge/serverless (no UZ egress), dedicated Fly IPv4 (inbound only), whitelisting Fly egress (shared NAT IP rotates → silent re-block).

STATUS: recommendation shifted from "UZ proxy" to "SPLIT worker on a UZ host"; code NOT yet written; pending user's host choice + a support question about Telegram-push/email-export. See [[park-api-credentials]].


## Chosen direction (2026-07-09): accountant-PC desktop sync app
User picked the concrete form of the SPLIT worker: a standalone, auto-start DESKTOP APP that runs on the **accountant's (bugalter's) own Uzbek computer** and syncs MultiCard → the shared Sheet. Rationale: the accountant is the data consumer, and their PC is naturally on a UZ IP → no geo-block, outbound-only, no proxy/VPN/NAT/port-forward. Telegram bot STAYS on Fly (Telethon is gap-intolerant; the accountant PC is not 24/7).

Design requirements agreed:
- **Adaptive lookback (CRITICAL):** the accountant PC is off nights/weekends/holidays, so a fixed `LOOKBACK_DAYS=3` would LOSE transactions during longer gaps. Make the from-date cover "since last successful sync" (or set `LOOKBACK_DAYS`≈30) and keep `SEEN_PRUNE_DAYS` > lookback so pruned uuids aren't re-written. Over-fetching is harmless (dedup drops dupes) → downtime < the window self-heals, nothing lost.
- **Auto-start + auto-restart:** package with PyInstaller into a single `.exe` (accountant needn't install Python); launch via Windows Task Scheduler ("at log on", "restart on failure") or Startup folder. Single-instance lock (two copies = two seen files → duplicates).
- **Visibility for a non-technical user:** a "last synced: <time>" heartbeat cell in the Sheet + a local log file; optional system-tray icon (green/red) later.
- **Security:** bundle the Google service-account creds (scope to the ONE sheet only) + MultiCard password on the accountant PC.
- **Code:** extract `worker()`'s body into a reusable `run_once()`; add a ~20-line desktop driver (own gspread client + append_rows_batch) + adaptive lookback. `multicard.py` core logic otherwise unchanged. Two writers to one Sheet is safe (append is server-atomic; dedup is single-owner on this app).

STATUS: direction agreed; code NOT yet written. Pending from user: accountant PC OS (assume Windows) + form factor (headless vs tray). See [[park-api-credentials]].


## RESTRUCTURED (2026-07-09): MultiCard split into its own sub-project
MultiCard logic was REMOVED from the main telegram-sheeter (Telegram bot) app and moved into a self-contained sub-project at `multicard/` (inside the repo) so it can run on a UZ machine independently. Supersedes the earlier "Deploy gotcha (COPY multicard.py)" and "Integration status DONE 2026-07-07 (wired into main.py)" notes — MultiCard is NO LONGER part of the Fly app.
- Removed from main app: `main.py` (the `import multicard` block + the worker-startup block + the multicard-only `append_rows_batch` / `sheet_lock` / `import threading`; `try_append` reverted to a plain `append_row`), `Dockerfile` (`COPY multicard.py .`), `Makefile` `secrets` (MULTICARD_* no longer pushed to Fly). The Fly deploy is now Telegram→Sheet ONLY.
- New sub-project `multicard/`: `multicard.py` (moved via `git mv`, logic UNCHANGED), `.env` (MULTICARD_* + SHEET_ID copied from root), `requirements.txt` (requests, python-dotenv + gspread, google-auth for the coming driver), `README.md`, `.gitignore` (ignores .env/creds.json/multicard_seen.json/build artifacts).
- Creds NOT lost: MULTICARD_* remain in BOTH root `.env` AND `multicard/.env`.
- VERIFIED 2026-07-09: `cd multicard && python multicard.py test` from the UZ IP → login OK (token), 20 tx fetched over 3-day lookback, build_row correct. The moved logic runs standalone.
- NEXT (pending): a desktop driver + Windows `.exe` inside `multicard/` — auto-start on boot, periodic sync to the Sheet, adaptive lookback for PC-off gaps (see the "Chosen direction" section). User confirmed: `.exe` format, runs at startup, writes to the Sheet periodically. See [[park-api-credentials]].


## DESKTOP APP BUILT (2026-07-09): C#/.NET 10/Avalonia in `multicard/`
Per the user's PROJECT_CONTEXT.md, the accountant-PC sync app was implemented as a **C# 14 / .NET 10 / Avalonia UI 12 (MVVM)** Windows desktop app (NOT Python — the Python `multicard.py` moved to `multicard/reference/` as prototype). Clean architecture:
- **Core** (`multicard/src/Core`, no UI/infra deps): models, options, `Abstractions/` (IMultiCardClient, ISheetWriter, ISeenStore + MultiCardBlockedException), `Logic/DedupKey` + `Logic/RowBuilder` (ports of _dedup_key / build_row), and `SyncService` (worker port: adaptive lookback + baseline-first-run + dedup + write; events StateChanged/ItemsSynced/Log; SemaphoreSlim-serialized RunOnce).
- **Data** (`multicard/src/Data`): `MultiCardClient` (HttpClient typed client; empty `Bearer` login → PascalCase body; token cache via data.expiry; 401 re-login; geo-block/HTML → MultiCardBlockedException), `GoogleSheetWriter` (Google.Apis.Sheets.v4, service account, resolves worksheet by index→title, append A:J USER_ENTERED, L1 heartbeat), `SeenStore` (SQLite+Dapper; seen + meta tables; UTC "O" timestamps; dedup + UI history + prune).
- **App** (`multicard/src/App`): Avalonia MVVM. `Program.cs` builds MS.DI + Serilog(file) + typed HttpClient; config from EMBEDDED appsettings.json + appsettings.Secrets.json. `MainWindowViewModel` (CommunityToolkit.Mvvm): status dot, totals, last-sync, SyncNow/Toggle/OpenSheet/ToggleStartup commands, ObservableCollection of synced tx. `MainWindow.axaml`: header + stats + DataGrid (Sana/Yo'nalish/Summa/Izoh/Holat/Sinxron) + startup checkbox. `WindowsStartupRegistrar` (HKCU\...\Run, guarded OperatingSystem.IsWindows()).
- **Tests** `multicard/tests/Core.Tests` (xunit+Shouldly): DedupKey + RowBuilder.

**Build/secrets:** `dotnet` NOT installed on the dev Mac → NOT compiled locally; builds via `.github/workflows/multicard-build.yml` (windows-latest → single self-contained .exe, path-filtered to `multicard/**`, separate from fly-deploy.yml). Secrets are NOT in the repo (.env/creds.json gitignored) → CI writes `appsettings.Secrets.json` from 3 GitHub Secrets (`MULTICARD_PASSWORD`, `MULTICARD_SHEET_ID`, `GOOGLE_CREDENTIALS_JSON`) which get EMBEDDED into the exe. Release on tag `mc-v*`. SHEET_ID=1Qh9KyNdaiJCJL1s3aPU-_WLPGTSzCx4W3cIt_VhXr4o; Google SA=auto-deploy@chaqqon-chaqqon-463906.iam.gserviceaccount.com; worksheet index 1.
STATUS: full code written + structure/JSON verified; FIRST CI run may surface compile fixes (blind build). Runtime data at %APPDATA%\MultiCardSync\. See [[park-api-credentials]].

**Local dev/run (2026-07-09):** the Avalonia app is cross-platform → runnable on the dev Mac via `cd multicard && dotnet run --project src/App` (only WindowsStartupRegistrar is a no-op off Windows). Because the dev Mac is on a UZ IP, the FULL flow works there (login + fetch + real sheet write; first run = safe baseline). `dotnet` is NOT yet installed on the Mac (need .NET 10 SDK). A gitignored `multicard/src/App/appsettings.Secrets.json` was generated from `reference/.env` + `creds.json` so local run is turnkey. Running on Mac is also the fast loop to catch compile fixes from the blind build before the Windows CI build.

**Build fix (2026-07-09):** PROJECT_CONTEXT.md was WRONG that Avalonia 12 exists — latest is **11.3.18**. All Avalonia packages must be `11.3.*` (App.csproj fixed; the doc/CI comments still say 12). `Serilog.Extensions.Logging 9.*` also unavailable → use `8.*`. Non-fatal warnings that remain: NU1510 (Microsoft.Win32.Registry is framework-provided on net10.0, removable) and NU1903 (SQLitePCLRaw 2.1.11 high-severity vuln, transitive via Microsoft.Data.Sqlite). Iterating compile fixes locally via `dotnet run` on the Mac (SDK 10.0.301 now installed).

**Tray / background (2026-07-09):** app runs like Telegram — Avalonia `TrayIcon` + `ShutdownMode.OnExplicitShutdown`; window X → `e.Cancel` + `Hide()` (close-to-tray, sync loop keeps running); tray left-click / "Ochish" reopens; menu Ochish/Hozir sinxronla/Chiqish. Startup registry launches with `--tray` arg → starts hidden in tray on boot; manual launch shows the window. Tray icon = `src/App/Assets/tray.png` (yellow ✓, embedded AvaloniaResource); loaded via `avares://MultiCardSync/Assets/tray.png`. Works on macOS menu bar too (testable on the dev Mac).

**CI secrets ops:** GitHub repo is `mcodevs/telegram-sheetter` (double-t); `gh` CLI is installed + authed (mcodevs, `repo` scope) so secrets can be set from the CLI. The 3 required secrets — `MULTICARD_PASSWORD`, `MULTICARD_SHEET_ID`, `GOOGLE_CREDENTIALS_JSON` — can be set from existing files: `gh secret set MULTICARD_PASSWORD --body "$(grep '^MULTICARD_PASSWORD=' multicard/reference/.env | cut -d= -f2-)"`, same for SHEET_ID, and `gh secret set GOOGLE_CREDENTIALS_JSON < creds.json`. Must be set BEFORE (or re-run workflow after) the push, else the .exe builds with empty creds. Local `multicard/src/App/appsettings.Secrets.json` is gitignored (never pushed).

**Secrets SET + push gotcha (2026-07-10):** all 3 GitHub Secrets (`MULTICARD_PASSWORD`, `MULTICARD_SHEET_ID`, `GOOGLE_CREDENTIALS_JSON`) are now SET on `mcodevs/telegram-sheetter` (plus pre-existing `FLY_API_TOKEN`). GOTCHA: `.github/workflows/fly-deploy.yml` triggers on ANY push to `main` (no path filter) → pushing the multicard changes to main ALSO redeploys the Telegram bot to Fly. To build the `.exe` WITHOUT touching Fly, push a branch + open a PR — `multicard-build.yml` runs on `pull_request` (paths `multicard/**`) and same-repo PRs get secrets, so the artifact builds; fly-deploy stays idle until merge. main.py/Dockerfile/Makefile multicard-removal is already committed (HEAD `0454016`); uncommitted work = the C# app + docs.