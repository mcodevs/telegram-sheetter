---
name: multicard-wallethistory-api
description: MultiCard/MultiAvto (novacore) WalletHistory API — 2nd tx source, login, tiyin units, sheet integration (WORKING)
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