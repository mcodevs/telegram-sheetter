---
name: sheet-template-2026-07
description: "Master Sheet got a new accountant template ~2026-07-28: ops sheet moved to
  index 0 (so get_worksheet(1)/WorksheetIndex=1 now hit 'Инфо' and MultiCard rows are
  landing in the wrong sheet), data table shifted to B..M starting at row 21."
metadata:
  type: project
---

The live workbook `1Qh9KyNdaiJCJL1s3aPU-_WLPGTSzCx4W3cIt_VhXr4o` (title **"Komilova Nazira"**, same `SHEET_ID` as always) was re-templated by the accountant around **2026-07-28 ~22:00**. Diagnosed 2026-07-31 from a downloaded copy + a read-only pass over the live sheet. Two independent breakages.

## 1. Worksheet ORDER changed → both writers aim at the wrong sheet
Live order is now `0: Нахд Приход&Расход` · `1: Инфо` · `2: Расходы` · `3: СASH FLOW` · `4: Кредиторка` · `5: Июнь ПНЛ`. The operations sheet used to be index 1; it is now **index 0**.

Both writers are hard-pinned to index 1 — `main.py` `gc.open_by_key(SHEET_ID).get_worksheet(1)` and the desktop app's `GoogleSheet:WorksheetIndex = 1` — so they now resolve to **`Инфо`**, the car/статья reference sheet.

- **MultiCard desktop app: actively corrupting `Инфо`.** 17 transaction rows sit in `Инфо` **rows 216–232, columns I..R** (`MultiCard пополнение/списание`, amounts, `Мультисард`, long Изоҳ). First one `2026-07-28 22:15`, still arriving `2026-07-31 13:45`. Row 216 is worse than the rest — it overlaps a REAL mapping row (`A216=Зарплата`), so cleanup is not a plain row-delete.
- **Telegram bot survived — by luck, not design.** [`main.py`](../main.py) resolves the worksheet **once at import** (module level) and gspread caches the worksheet **title**, so the long-running Fly process still appends to `Нахд Приход&Расход` (its 2026-07-30 rows landed correctly). **The next `fly deploy` / restart re-resolves index 1 and it will start writing into `Инфо` too.**
- `Нахд Приход&Расход`!L1 heartbeat froze at `Oxirgi sinxron: 2026-07-28 21:58` — the last write before the switch. `Инфо`!L1 is empty (accountant likely cleared it).

**FIXED in code 2026-07-31** — see "Fix as implemented" at the bottom. Both apps now resolve the worksheet **by title**; a missing title is a loud startup failure, never a silent write to the wrong tab.

## 2. Data-table LAYOUT shifted one column right + 3 new columns
`Нахд Приход&Расход` is now: **rows 1–18** = dashboard (per-account `SUMIFS` of Остатка/Приход/Расход/Баланс over `B21:B51085`), **row 19** = title, **row 20** = header, **row 21+** = data. Column **A is empty**.

| old (code) | new | column |
|---|---|---|
| A | **B** | Сана |
| B | C | Фирма / Филиал |
| C (unnamed) | D | **Марка** (now named) |
| D | E | Инфо |
| E | F | Статья расход |
| F | G | Приход |
| G | H | Тўлов тури Приход |
| H | I | Расход |
| I | J | Тўлов тури Расход |
| J | K | Изоҳ (Комент) |
| — | **L** | **Cash Flow** (formula, NEW) |
| — | **M** | **Группа расходы** (formula, NEW) |

The existing 10-value row still maps correctly **only because** the Sheets `values.append` table-detection happens to start at column B. Fragile — pin the range explicitly (`B:K`) rather than relying on it.

## Formula chain the writers must feed
```
F (Статья)   = IFERROR(VLOOKUP(E, 'Инфо'!C:I, 7, 0), …)
L (CashFlow) = IFERROR(VLOOKUP(F, 'Инфо'!C:I, 7, 0), "")
M (Группа)   = IF(I="", F, VLOOKUP(F, 'Инфо'!C:J, 8, 0))
```
Bot/app rows leave **E and F empty** → `L` blank and `M` = `#N/A` (visible on rows 1566–1568). The dashboard SUMIFS still total correctly (they only read B, G/H, I/J), but those rows **drop out of the CASH FLOW and ПНЛ classification**. Open question for the accountant: what should the writers put in `E` / `F`?

## Тўлов тури is a validated list
`H21:H` and `J21:J` are data-validated against `'Инфо'!$Q:$Q`: `Нақд пул (сум)` · `Мультисард` · `Карта (3933)` · `Карта (4962)` · `Карта (6386)` · `Карта Ислом` · `Нақд пул (USD)` · `Банк NOVA` · `Банк Capital` · `Банк Кейнги авлод`.

`main.py` `build_row()` synthesises `Карта (<last4>)` from the message. The fleet also has cards ending **2804 / 9321 / 0420** (see `Инфо` col J) which are **not** on that list — such a row would fail validation and be silently excluded from the balance SUMIFS.

## Accountant's PC is running an OLD build
The rows in `Инфо` carry `MultiCard пополнение/списание` in the Инфо column plus a long Изоҳ — that is the **pre-`d074f37`** row mapping (`a517d37`-era). The committed code leaves both columns empty, so the `.exe` in use predates the current source. Re-distribute after fixing.

## Fix as implemented (2026-07-31)
Same two changes in both writers, plus verification:

- **Worksheet by title, never by index.** `main.py` gained `SHEET_TAB` (env-overridable, default `Нахд Приход&Расход`) + `open_sheet()`, which raises `SystemExit` listing the available tabs if the title is gone. C# gained `GoogleSheet:WorksheetTitle` (`GoogleSheetOptions.WorksheetTitle`, default is the same title); `GoogleSheetWriter.ResolveSheetTitleAsync` throws with the tab list on a miss. The old `WorksheetIndex` survives **only** as a legacy path taken when the title is explicitly blank, and it now logs a warning.
- **Append range anchored to the header, not hard-coded.** Both resolve the header row at startup by scanning column B for `"Сана"` and build `B<row>:K` (currently `B20:K`). The row number is never hard-coded — the summary block above the table can grow and the range follows it. Python: `resolve_table_range()` + `append_row(..., table_range=TABLE_RANGE)`. C#: `Core/Logic/TableAnchor` (pure, unit-tested) + `GoogleSheetWriter.ResolveAppendRangeAsync`; the old `'{sheet}'!A:J` range is gone.
- **Verified, not assumed.** A throwaway tab in the live workbook (created and deleted in the same run, existing tabs untouched) proved gspread `append_row` with `table_range="B20:K"` lands at `B23:K23`/`B24:K24` — right after the last data row, starting at column B, column A untouched, Приход→G/H and Расход→I/J. A read-only C# probe against the live workbook resolved the same `'Нахд Приход&Расход'!B20:K`. Core tests 17/17 (6 new in `TableAnchorTests`, including "header moves when the summary block grows").

Committed as `165e590`. **The push was blocked by the permission classifier — the user pushes `main` themselves**, which is what triggers both the Fly redeploy and the new `.exe` build.

## Cleanup + data recovery (user decided 2026-07-31: Claude does NOT touch the sheet)
The junk in `Инфо` spans **rows 216–233, columns I..R — 75 cells**. Two selections:
`L216:R216` (⚠️ **leave `I216`/`J216` alone** — real mapping data for `Зарплата`) and `I217:R233` (all foreign).

**The 17 mis-written transactions will NOT come back on their own** — the app's local SQLite already has them marked seen/Written, so dedup skips them after the fix. Cross-checked against the ops sheet: **1 of 17 is already recorded manually** (30.07 Расход 10 090 000 at ops row 1565, part of a Мультисард→Карта(4962) Перемешение); the other **16 are genuinely missing** (Приход 55 000 000 · Расход 42 971 023, spanning 2026-07-28 22:15 → 2026-07-31 13:45). A B→K-shaped CSV of those 16 was handed to the user for manual entry. Alternative if hand-entry is unwanted: delete just those keys from `%APPDATA%\MultiCardSync\multicardsync.db` `seen` table — **never the whole DB**, since an empty DB makes `IsFirstRunAsync` true and the next cycle baselines (writes nothing) instead.

**Still open (needs the user/accountant, not code):**
1. Stop the app on the accountant's PC — the old build keeps appending to `Инфо` (last seen 2026-07-31 13:45) until the new `.exe` replaces it.
2. What should the writers put in `E` (Инфо) / `F` (Статья расход) so rows get classified into Cash Flow / ПНЛ? Still empty → `M` shows `#N/A`.
3. `Карта (<last4>)` values outside the validated list (2804 / 9321 / 0420) would still fall out of the dashboard SUMIFS.

Supersedes the "Row mapping (build_row)" A→J layout in [[multicard-wallethistory-api]]. See [[park-api-credentials]], [[fleet-api-transactions]].
