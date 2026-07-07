---
name: fleet-api-transactions
description: Yandex Fleet API transaction endpoints and header auth mapping to our .env creds
metadata:
  type: reference
---

Yandex Fleet API **does** support pulling transactions — relevant for a future feature that reads park transactions into Google Sheets. Uses the credentials from [[park-api-credentials]].

Base URL: `https://fleet-api.taxi.yandex.net`

Transaction endpoints (all `POST`):
- `/v2/parks/transactions/list` — all park transactions; filters: `driver_profile_id`, `event_at` (`from`/`to`), `category_ids`, `limit`, `cursor` (pagination). Primary one to use.
- `/v2/parks/driver-profiles/transactions/list` — single driver's transactions.
- `/v2/parks/orders/transactions/list` — transactions per order.

Auth is via **headers**, mapping directly to our `.env` vars:
- `X-Client-ID` ← `PARK_CLID` (full `taxi/park/...` string)
- `X-Api-Key` ← `PARK_API_KEY`
- `X-Park-ID` ← `PARK_ID`
- `Accept-Language: ru`

Gotchas: rate-limited (throttle sequential calls, ~1 req / few seconds); response returns a `cursor` for the next page. Exact request-body JSON schema should be confirmed against the official reference (`fleet.yandex.uz/docs/api/ru/` → Ресурсы API → Транзакции) before relying on it — endpoint paths/headers are confirmed, body shape is from community wrappers.

Docs: https://fleet.yandex.uz/docs/api/ru/ · wrapper reference: https://github.com/RiddlerX2/yandex-fleet-wrapper
