# MultiCard → Sheet Sync (Windows desktop ilova)

Hisobchining O'zbekistondagi kompyuterida ishlaydigan mustaqil desktop ilova.
MultiCard (`api.multiavto.uz`) tranzaksiyalarini olib, Google Sheet'ga sinxronlaydi.

**Nega alohida ilova?** `api.multiavto.uz` faqat O'zbekiston IP'lariga ruxsat beradi
(Fly/Frankfurt serveridan 403 HTML blok). Ulanish **outbound** bo'lgani uchun,
ilovani O'zbekistondagi mashinada ishga tushirsak — blok yo'qoladi, proxy/VPN kerak emas.
To'liq tashxis: [`../references/multicard-wallethistory-api.md`](../references/multicard-wallethistory-api.md).

## Asosiy oqim
1. **Birinchi ishga tushishda** o'zini Windows startup'ga qo'shadi (kompyuter yoqilganda avtomatik ishlaydi).
2. Har `PollIntervalSeconds` (default 300s) da MultiCard'ni tekshiradi.
3. Lokal SQLite bazadagi ko'rilganlar bilan solishtiradi (**dedup**).
4. Yangi tranzaksiyalarni **Google Sheet'ga yozadi** va lokal bazaga saqlaydi.
5. Sinxronlangan tranzaksiyalarni **UI jadvalida** ko'rsatadi (holat: ✅ Yozildi / ⏳ Navbatda).
6. **Adaptiv lookback:** kompyuter uzoq o'chiq tursa, oxirgi sinxrondan beri o'tgan davrni
   (`MaxLookbackDays`gacha) qamrab oladi — dedup tufayli hech narsa yo'qolmaydi/takrorlanmaydi.
7. Birinchi ishga tushish = **baseline** (mavjud tarix "ko'rilgan" deb belgilanadi, yozilmaydi).

## Texnologiya
C# 14 / .NET 10 · Avalonia UI 12 (MVVM) · SQLite (Dapper) · Google Sheets API · Serilog.
Stack tafsiloti: `../` dagi PROJECT_CONTEXT hujjati. Struktura:

```
multicard/
├── App.slnx
├── Directory.Build.props / .editorconfig
├── src/
│   ├── Core/    biznes logika (UI'ga bog'liq emas): modellar, dedup, RowBuilder, SyncService
│   ├── Data/    MultiCardClient (HTTP) · GoogleSheetWriter · SeenStore (SQLite)
│   └── App/     Avalonia UI (MVVM) · DI · startup-registratsiya · config
├── tests/Core.Tests/    dedup + row-builder unit testlari
└── reference/           Python prototipi (multicard.py) — faqat namuna
```

## `.exe` ni olish (GitHub Actions — Windows)

Ilova **macOS'da yoziladi**, lekin `.exe` **GitHub Actions (windows-latest)** da build bo'ladi
(`.github/workflows/multicard-build.yml`).

### 1) GitHub Secrets (bir marta)
Repo → Settings → Secrets and variables → Actions → 3 ta secret qo'shing:

| Secret nomi | Qiymati |
|---|---|
| `MULTICARD_PASSWORD` | MultiCard paroli |
| `MULTICARD_SHEET_ID` | Google Sheet ID |
| `GOOGLE_CREDENTIALS_JSON` | `creds.json` faylining **butun matni** |

Bular exe ichiga (embedded) joylanadi → **bitta self-contained `.exe`**, boshqa fayl kerak emas.
(Sirlar repoда saqlanmaydi — faqat GitHub Secrets'da.)

### 2) Build'ni ishga tushirish
`multicard/` ichida biror o'zgarish push qilinsa yoki Actions → *MultiCard Sync* → **Run workflow**.

### 3) `.exe` ni yuklab olish
- **Test uchun:** Actions → run → *Artifacts* → `multicardsync-win-x64`.
- **Hisobchiga tarqatish uchun:** teg qo'ying — Releases sahifasiga chiqadi:
  ```bash
  git tag mc-v1.0.0 && git push origin mc-v1.0.0
  ```
- **Mac terminalidan:** `gh run download -n multicardsync-win-x64`

> Imzolanmagan `.exe` ni Windows SmartScreen "noma'lum nashriyot" deydi — "Batafsil → Baribir ishga tushirish". Ichki ilova uchun normal.

## Lokal ishga tushirish (macOS dev)
```bash
cd multicard
cp src/App/appsettings.Secrets.sample.json src/App/appsettings.Secrets.json
# ↑ real parol / sheet id / creds JSON bilan to'ldiring (bu fayl gitignore qilingan)
dotnet run --project src/App
```

## Config (`src/App/appsettings.json`)
| Bo'lim | Kalit | Default | Izoh |
|---|---|---|---|
| MultiCard | Username / Domain | admin / thenovacore | login |
| Sync | PollIntervalSeconds | 300 | tekshirish davri |
| Sync | LookbackDays | 14 | har so'rovda necha kun orqaga |
| Sync | MaxLookbackDays | 90 | o'chiq davr uchun yuqori chegara |
| Sync | RegisterStartup | true | startup'ga qo'shish |
| GoogleSheet | WorksheetIndex | 1 | 2-varaq |
| GoogleSheet | WriteHeartbeat | true | "oxirgi sinxron" belgisi (L1) |

Sirlar (Password, SpreadsheetId, CredentialsJson) — `appsettings.Secrets.json` orqali.

## Ma'lumot joyi
`%APPDATA%\MultiCardSync\` → `multicardsync.db` (dedup + tarix) va `logs\` (Serilog).
```
