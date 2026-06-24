import os
import re
import json
from telethon import TelegramClient, events
from telethon.sessions import StringSession
import gspread
from google.oauth2.service_account import Credentials
from dotenv import load_dotenv

load_dotenv()

api_id = int(os.getenv("API_ID"))
api_hash = os.getenv("API_HASH")
SOURCE = os.getenv("SOURCE")
SHEET_ID = os.getenv("SHEET_ID")

# Ustunlar master workbook'dagi "КУНЛИК ОПЕРАЦИЯЛАР КИРИТИШ БАЗАСИ" bilan bir xil (A->J)
HEADER = [
    "Сана", "Фирма / Филиал", "", "Инфо", "Статья расход",
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
# 2-varaq (index 1) = "Нахд Приход&Расход" operatsiyalar bazasi
sheet = gc.open_by_key(SHEET_ID).get_worksheet(1)

# Serverda TG_SESSION secret'idan (matn sessiya) o'qiydi; lokalda esa "session" fayldan.
_session = StringSession(os.getenv("TG_SESSION")) if os.getenv("TG_SESSION") else "session"
client = TelegramClient(_session, api_id, api_hash)


def build_row(data):
    """parse_message natijasini master jadval ustunlariga (A->J) joylaydi."""
    sana = data["sana"].split(" ")[0]  # faqat sana qismi: 2026-06-24
    last4 = re.sub(r"\D", "", data["karta"])[-4:]
    tolov = f"Карта ({last4})" if last4 else ""
    summa = abs(data["summa"]) if data["summa"] != "" else ""

    prixod = tolov_p = rasxod = tolov_r = ""
    if data["yonalish"] == "kirim":
        prixod, tolov_p = summa, tolov
    elif data["yonalish"] == "chiqim":
        rasxod, tolov_r = summa, tolov

    # Сана | Фирма/Филиал | (филиал) | Инфо | Статья | Приход | Тўлов Приход | Расход | Тўлов Расход | Изоҳ
    return [sana, "", "", "", "", prixod, tolov_p, rasxod, tolov_r, ""]


@client.on(events.NewMessage(chats=SOURCE))
async def handler(event):
    text = event.message.message
    row = build_row(parse_message(text))
    sheet.append_row(row, value_input_option="USER_ENTERED")
    print("Qo'shildi:", row)


def main():
    print("Ishga tushdi. Yangi xabarlar kutilmoqda...")
    client.start()
    client.run_until_disconnected()


if __name__ == "__main__":
    main()
