---
name: park-api-credentials
description: Yandex Fleet/Taxi park API credentials stored in .env; not yet wired into code
metadata:
  type: project
---

Yandex Fleet (Taxi park) API credentials were added to `.env` on 2026-07-07, sourced from a "Ключ создан" screenshot (`park_credentials.jpg`). The API key is shown only once and cannot be recovered, so treat `.env` as the single source of truth.

Variable names chosen (not dictated by existing code):
- `PARK_CLID` — value includes the literal `taxi/park/` prefix exactly as shown in the screenshot (verify the Fleet API doesn't expect the bare ID before use).
- `PARK_API_KEY`
- `PARK_ID` — same hex value as the CLID's suffix.

Status: `main.py` does **not** reference these yet — they anticipate future Fleet API integration. When wiring them in, read via `os.getenv(...)` to match the existing env conventions (see `API_ID`, `API_HASH`, `SOURCE`, `BOT`, `SHEET_ID`).

Security note: `.env` is gitignored. `park_credentials.jpg` (the screenshot holding the same secrets) was also added to `.gitignore` on 2026-07-07 and is now ignored by git.
