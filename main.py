import os
import re
import json
import asyncio
from telethon import TelegramClient, events
from telethon.sessions import StringSession
import gspread
from gspread.exceptions import APIError
from google.oauth2.service_account import Credentials
from dotenv import load_dotenv

load_dotenv()

# Sheets'ga yozib bo'lmagan qatorlar shu faylga saqlanadi va keyin qayta urinib ko'riladi.
PENDING_FILE = os.getenv("PENDING_FILE", "pending_rows.jsonl")
RETRY_INTERVAL = 600  # 10 daqiqa
pending_lock = asyncio.Lock()

api_id = int(os.getenv("API_ID"))
api_hash = os.getenv("API_HASH")
SOURCE = os.getenv("SOURCE")      # endi GURUH (bot xabar tashlaydigan), bot emas
# Raqamli guruh ID (-100...) bo'lsa int ga o'tkazamiz; xato '@' bo'lsa ham tozalaymiz
if SOURCE:
    _s = SOURCE.lstrip("@")
    if _s.lstrip("-").isdigit():
        SOURCE = int(_s)
BOT = os.getenv("BOT") or None    # guruhda xabar yozadigan bot — faqat shuning xabarlari olinadi
SHEET_ID = os.getenv("SHEET_ID")

# Yoziladigan varaq NOMI. Ilgari varaq indeks bo'yicha olinardi (get_worksheet(1)) —
# hisobchi varaqlar tartibini o'zgartirgach, index 1 "Инфо" ma'lumotnomasiga tushib
# qoldi va yozuvlar noto'g'ri varaqqa ketdi (2026-07-28). Nom bo'yicha topish shu
# turdagi buzilishni butunlay yopadi: varaq topilmasa — ilova darrov xato beradi.
SHEET_TAB = os.getenv("SHEET_TAB") or "Нахд Приход&Расход"

# Ma'lumot jadvali "Сана" sarlavhasidan boshlanadi. Sarlavha qatori raqami qat'iy
# yozilmaydi — ustidagi hisobot bloki o'sishi mumkin, shuning uchun ishga tushganda
# topiladi (pastdagi resolve_table_range).
HEADER_ANCHOR = "Сана"

# Ustunlar master workbook'dagi "КУНЛИК ОПЕРАЦИЯЛАР КИРИТИШ БАЗАСИ" bilan bir xil.
# YANGI SHABLON (2026-07): jadval A emas, B ustunidan boshlanadi (B->K), A bo'sh.
HEADER = [
    "Сана", "Фирма / Филиал", "Марка", "Инфо", "Статья расход",
    "Приход", "Тўлов тури Приход", "Расход", "Тўлов тури Расход", "Изоҳ (Комент)",
]


def _after(line):
    """Emoji'dan keyingi qismni qaytaradi: '💳 ***2804' -> '***2804'."""
    parts = line.split(None, 1)
    return parts[1].strip() if len(parts) > 1 else ""


def _num(line):
    """'➖ 63 000.00 UZS' -> 63000.0 (float). Bo'sh bo'lsa '' qaytaradi."""
    digits = re.sub(r"[^\d.]", "", _after(line).replace(" ", ""))
    return float(digits) if digits else ""


def _datetime(line):
    """'🕓 24.06.26 00:02' -> '2026-06-24 00:02'."""
    rest = _after(line)
    m = re.match(r"(\d{2})\.(\d{2})\.(\d{2})\s+(\d{2}):(\d{2})", rest)
    if m:
        dd, mm, yy, hh, mi = m.groups()
        return f"20{yy}-{mm}-{dd} {hh}:{mi}"
    return rest


def parse_message(text):
    data = {
        "sana": "", "tur": "", "yonalish": "",
        "summa": "", "karta": "", "joy": "", "balans": "",
    }
    for raw in text.splitlines():
        line = raw.strip()
        if not line:
            continue
        if line.startswith("🔴") or line.startswith("🟢"):
            data["tur"] = _after(line)
            data["yonalish"] = "chiqim" if line.startswith("🔴") else "kirim"
        elif line.startswith("➖") or line.startswith("➕"):
            value = _num(line)
            # chiqim -> manfiy, kirim -> musbat (Sheets'da SUM uchun qulay)
            if value != "" and line.startswith("➖"):
                value = -value
            data["summa"] = value
        elif line.startswith("💳"):
            data["karta"] = _after(line)
        elif line.startswith("📍"):
            data["joy"] = _after(line)
        elif line.startswith("🕓"):
            data["sana"] = _datetime(line)
        elif line.startswith("💵"):
            data["balans"] = _num(line)
    return data


# Google Sheets ulanishi
# Serverda creds.json fayl o'rniga GOOGLE_CREDS secret'idan (JSON matn) o'qiydi.
scopes = ["https://www.googleapis.com/auth/spreadsheets"]
_creds_env = os.getenv("GOOGLE_CREDS")
if _creds_env:
    creds = Credentials.from_service_account_info(json.loads(_creds_env), scopes=scopes)
else:
    creds = Credentials.from_service_account_file("creds.json", scopes=scopes)
gc = gspread.authorize(creds)


def open_sheet():
    """Operatsiyalar varag'ini NOM bo'yicha ochadi (indeks bo'yicha EMAS)."""
    book = gc.open_by_key(SHEET_ID)
    try:
        return book.worksheet(SHEET_TAB)
    except gspread.WorksheetNotFound:
        mavjud = ", ".join(w.title for w in book.worksheets())
        raise SystemExit(
            f"'{SHEET_TAB}' varag'i topilmadi. Mavjud varaqlar: {mavjud}\n"
            f"To'g'ri nomni SHEET_TAB muhit o'zgaruvchisiga yozing."
        )


def resolve_table_range(ws):
    """Qatorlar qo'shiladigan diapazon: 'B<sarlavha qatori>:K'.

    Sheets API append'i diapazondagi jadvalni topib, uning oxiriga yozadi. Diapazonni
    aniq ko'rsatmasak, API jadvalni o'zi taxmin qiladi — varaq tepasidagi hisobot
    bloki tufayli bu ishonchsiz. Sarlavha ("Сана") qatorini topib, aniq bog'laymiz.
    """
    col_b = ws.col_values(2)
    for i, value in enumerate(col_b, start=1):
        if str(value).strip() == HEADER_ANCHOR:
            return f"B{i}:K"
    raise SystemExit(
        f"'{SHEET_TAB}' varag'ining B ustunida '{HEADER_ANCHOR}' sarlavhasi topilmadi — "
        f"jadval strukturasi yana o'zgargan bo'lishi mumkin."
    )


sheet = open_sheet()
TABLE_RANGE = resolve_table_range(sheet)
print(f"Varaq: '{sheet.title}' · jadval diapazoni: {TABLE_RANGE}")

# Serverda TG_SESSION secret'idan (matn sessiya) o'qiydi; lokalda esa "session" fayldan.
_session = StringSession(os.getenv("TG_SESSION")) if os.getenv("TG_SESSION") else "session"
client = TelegramClient(_session, api_id, api_hash)


def try_append(row):
    """Qatorni Sheets'ga yozadi. Muvaffaqiyatli bo'lsa True, xato bo'lsa False."""
    try:
        sheet.append_row(row, value_input_option="USER_ENTERED", table_range=TABLE_RANGE)
        return True
    except Exception as e:  # APIError (503/429/...) yoki tarmoq xatosi
        print("Sheets append xatosi:", e)
        return False


def queue_row(row):
    """Yozib bo'lmagan qatorni navbat fayliga (JSON-lines) qo'shadi."""
    with open(PENDING_FILE, "a", encoding="utf-8") as f:
        f.write(json.dumps(row, ensure_ascii=False) + "\n")


def load_pending():
    """Navbat faylidagi barcha qatorlarni o'qiydi. Fayl yo'q bo'lsa [] qaytaradi."""
    if not os.path.exists(PENDING_FILE):
        return []
    rows = []
    with open(PENDING_FILE, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


def rewrite_pending(rows):
    """Navbat faylini qolgan qatorlar bilan qayta yozadi; bo'sh bo'lsa o'chiradi."""
    if not rows:
        if os.path.exists(PENDING_FILE):
            os.remove(PENDING_FILE)
        return
    with open(PENDING_FILE, "w", encoding="utf-8") as f:
        for row in rows:
            f.write(json.dumps(row, ensure_ascii=False) + "\n")


async def flush_pending():
    """Navbatdagi qatorlarni Sheets'ga yozishga urinadi (kelish tartibida).
    Birinchi xatoda to'xtaydi — Google hali ham yiqilgan bo'lishi mumkin."""
    async with pending_lock:
        rows = load_pending()
        if not rows:
            return
        remaining = []
        for i, r in enumerate(rows):
            if not try_append(r):
                remaining.extend(rows[i:])  # hali yiqilyapti — to'xtaymiz
                break
        rewrite_pending(remaining)
        print(f"Navbat: {len(rows) - len(remaining)} yozildi, {len(remaining)} qoldi")


async def retry_worker():
    """Har 10 daqiqada navbatdagi qatorlarni qayta yozishga urinadi."""
    while True:
        await asyncio.sleep(RETRY_INTERVAL)
        try:
            await flush_pending()
        except Exception as e:
            print("retry_worker xatosi:", e)


def is_transaction(data):
    """Xabar haqiqiy operatsiya shablonigami?
    Reklama va boshqa formatdagi offtopic xabarlarni o'tkazib yuborish uchun.
    Shart: yo'nalish (🔴/🟢) + summa (➖/➕) + karta (💳) — uchchalasi ham bo'lsin."""
    return bool(data["yonalish"]) and data["summa"] != "" and bool(data["karta"])


def build_row(data):
    """parse_message natijasini master jadval ustunlariga (B->K) joylaydi."""
    sana = data["sana"].split(" ")[0]  # faqat sana qismi: 2026-06-24
    last4 = re.sub(r"\D", "", data["karta"])[-4:]
    tolov = f"Карта ({last4})" if last4 else ""
    summa = abs(data["summa"]) if data["summa"] != "" else ""

    prixod = tolov_p = rasxod = tolov_r = ""
    if data["yonalish"] == "kirim":
        prixod, tolov_p = summa, tolov
    elif data["yonalish"] == "chiqim":
        rasxod, tolov_r = summa, tolov

    #  B     C              D      E      F        G        H             I        J             K
    # Сана | Фирма/Филиал | Марка | Инфо | Статья | Приход | Тўлов Приход | Расход | Тўлов Расход | Изоҳ
    return [sana, "", "", "", "", prixod, tolov_p, rasxod, tolov_r, ""]


# chats=SOURCE -> qaysi guruhni tinglash; from_users=BOT -> faqat o'sha botning xabarlari
@client.on(events.NewMessage(chats=SOURCE, from_users=BOT))
async def handler(event):
    text = event.message.message
    data = parse_message(text)
    if not is_transaction(data):
        snippet = " ".join(text.split())[:60]
        print("O'tkazib yuborildi (shablonga mos emas):", snippet)
        return
    row = build_row(data)
    if try_append(row):
        print("Qo'shildi:", row)
    else:
        async with pending_lock:
            queue_row(row)
        print("Sheets xato — navbatga saqlandi:", row)


def main():
    print("Ishga tushdi. Yangi xabarlar kutilmoqda...")
    client.start()
    # Avvalgi ishlashdan qolgan navbatni darrov urinib ko'ramiz, keyin har 10 daqiqada.
    client.loop.create_task(flush_pending())
    client.loop.create_task(retry_worker())
    client.run_until_disconnected()


if __name__ == "__main__":
    main()
