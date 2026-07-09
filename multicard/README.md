# MultiCard → Google Sheet sinxronlash (alohida loyiha)

Bu — asosiy `telegram-sheeter` (Telegram bot) loyihasidan **ajratib olingan** MultiCard
logikasi. Sababi: `api.multiavto.uz` faqat O'zbekiston IP'lariga ruxsat beradi
(Fly/Frankfurt serveridan 403 HTML blok). Shuning uchun bu qism **O'zbekistondagi
mashinada** (hisobchining kompyuterida) mustaqil ishlashi kerak — ulanish outbound
bo'lgani uchun proxy, VPN yoki port-forward kerak emas.

Batafsil tashxis va qaror: `../references/multicard-wallethistory-api.md`.

## Tarkib
- `multicard.py` — asosiy logika (login → WalletHistory olish → dedup → sheet qatoriga
  o'girish). Asosiy loyihadan o'zgarishsiz ko'chirilgan.
- `.env` — MultiCard credentiallari (`MULTICARD_*`) va `SHEET_ID`.
- `requirements.txt` — bog'liqliklar.

## Test (Sheet'ga yozmasdan, faqat login + olishni tekshirish)
```bash
python -m venv .venv && . .venv/bin/activate      # (Windows: .venv\Scripts\activate)
pip install -r requirements.txt
python multicard.py test
```

## Keyingi qadam (rejalashtirilgan)
`multicard.py` ni chaqiradigan **desktop driver** + Windows `.exe`:
- kompyuter yoqilganda (startup) o'zi ishga tushadi,
- ma'lum davrda MultiCard'ni tekshirib, yangi tranzaksiyalarni Google Sheet'ga yozadi,
- kompyuter o'chiq turgan davrni (adaptiv lookback + dedup) quvib yetadi.
