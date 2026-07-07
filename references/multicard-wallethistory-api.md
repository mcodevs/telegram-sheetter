---
name: multicard-wallethistory-api
description: MultiCard/MultiAvto (novacore) WalletHistory API — second tx source, tiyin units, JWT auth, login flow
metadata:
  type: reference
---

A **second, separate** transaction source alongside [[fleet-api-transactions]]. This is the MultiCard / MultiAvto "novacore" deposit-wallet acquiring system — NOT Yandex Fleet. Distinct money flow: per card-payment debits from the fleet's wallet, not the Yandex ride-level ledger. Frontend is a **Blazor WebAssembly** app (`YandexDriverPartner.Client`).

## Hosts (from wwwroot/appsettings.json)
- `serverUrl` = `https://api.multiavto.uz/` — main API (data)
- `adminApiUrl` = `https://adminapi.multiavto.uz/` — admin API (**login**)

## Login (discovered 2026-07-07)
`POST https://adminapi.multiavto.uz/api/Auth/login`
- Body: `{"UserName": "...", "Password": "...", "Domain": "thenovacore"}` (plaintext password, NO client-side hashing).
- Headers: `Origin`/`Referer` = `https://thenovacore.multiavto.uz` (backend derives tenant from these).
- Returns a JWT bearer token. Token TTL ~2h (`exp - nbf = 7200s`); cache + re-login on expiry.
- Errors seen: missing field → 400 `{errors:{UserName:[...]}}`; bad pw → 200-ish body `"Password is wrong"`.
- The `admin` / `_X@OOGN8uE8a` creds given by user were REJECTED by the server ("Password is wrong") though UserName resolved — password needs re-verification (likely O/0 transcription).

## Data endpoints (namespace `api/multicard/*` on serverUrl)
- `POST api/multicard/WalletHistory?from=DDMMYY&to=DDMMYY&page=N&type=debit|credit` (Bearer) — `type=debit`=Списание, `type=credit`=Пополнение.
- Also present: `api/multicard/GetDebitList`, `api/multicard/Wallets`, `api/multicard/Balance`, `api/multicard/GetPartnerTransactions`, `api/multicard/GetClearingHistory`.

**Units gotcha (why amounts look "wrong"):** MIXED units within one record:
- `paymentAmount`, `commissionAmount` → **tiyin** (÷100 for so'm)
- `amountValue`, `balance` → **so'm** already

Verified relationships (50-row `type=debit` page, 2026-07-07): `amountValue = paymentAmount/100 + commissionAmount/100` held on **50/50**; commission = 0.9% of payment on 49/50 (one row rounded UP by 1 tiyin). Balance chain reconciles only across debit+credit merged by date — `type=debit` alone hides top-ups.

`type=credit` (Пополнение) rows are deposit top-ups ("ПОПОЛНЕНИЕ ОБЕСПЕЧИТЕЛЬНОГО ДЕПОЗИТА ПО ДОГОВОРУ №DRV-..."): no card, no commission, `Наша система: Нет`.

Response shape: `{ isSuccess, message, data:[{ number, date, amountValue, note, balance, uuid, storeId, status, cardPan, paymentAmount, commissionAmount, commissionType, isCommissionUp, isOurTransaction }], total, appVersion }`. Dedup key = `uuid`.

**Not reconcilable 1:1 with Yandex Fleet.** No shared join key: MultiCard rows carry `cardPan`/`uuid`/`storeId`; Yandex rows carry `order_id`/`driver_profile_id`. They measure complementary flows, not the same events. Treat as two independent datasets.

## Integration goal (project)
Extend telegram-sheeter (currently: Telethon parses bot msgs → Google Sheet) to ALSO pull MultiCard transactions into the sheet. Needs: login+token-cache+refresh, paginate WalletHistory (debit+credit), dedup by `uuid`, map to sheet. See [[fleet-api-transactions]] and [[park-api-credentials]].
