---
name: fleet-api-transactions
description: Yandex Fleet API transaction endpoints, verified request/response schema, header auth
metadata:
  type: reference
---

Yandex Fleet API **does** support pulling transactions — verified with a live call on 2026-07-07 using the credentials from [[park-api-credentials]]. Relevant for reading park transactions into Google Sheets.

Base URL: `https://fleet-api.taxi.yandex.net` (confirmed working)

Transaction endpoints (all `POST`):
- `/v2/parks/transactions/list` — all park transactions. **Verified.** Filters: `driver_profile_id`, `event_at` (`from`/`to`), `category_ids`, `limit`, `cursor` (pagination). Primary one to use.
- `/v2/parks/driver-profiles/transactions/list` — single driver's transactions.
- `/v2/parks/orders/transactions/list` — transactions per order.

Auth via **headers**, mapping directly to `.env` vars (verified correct):
- `X-Client-ID` ← `PARK_CLID` (full `taxi/park/...` string — required as-is)
- `X-Api-Key` ← `PARK_API_KEY`
- `X-Park-ID` ← `PARK_ID`
- `Accept-Language: ru`

Verified request body:
```json
{ "query": { "park": { "id": "<PARK_ID>",
    "transaction": { "event_at": { "from": "<rfc3339>", "to": "<rfc3339>" } } } },
  "limit": 20 }
```

Verified response — `transactions[]` items have: `id`, `event_at`, `category_id`, `category_name`, `group_id`, `amount` (decimal string), `currency_code`, `description`, `driver_profile_id`, `order_id`, `order.short_id`, `external_event_id`, `created_by.identity`. Response also returns a top-level `cursor` for the next page.

Key facts:
- **Пополнения vs Списания** is determined ONLY by the sign of `amount` (positive = пополнение, negative = списание). There is no separate type field.
- Real category examples: `Наличные`, `Оплата картой` (income); `Комиссия сервиса за заказ`, `Комиссия партнёра за заказ`, `Удержание налога с оборота`, `Чаевые` (charges).
- To get ALL transactions you must loop on `cursor` (one page = up to `limit`); rate-limited, so throttle sequential calls.

Working reference script: `scratchpad/fetch_tx.py` (parses `.env`, uses stdlib urllib only).

Docs: https://fleet.yandex.uz/docs/api/ru/
