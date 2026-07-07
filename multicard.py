"""MultiCard / MultiAvto (novacore) tranzaksiyalarini olib, mavjud Google Sheet'ga
yozadigan modul. main.py'dagi Telegram listener yonida fon vazifasi (worker) sifatida
ishlaydi.

Oqim:
  1. adminapi.multiavto.uz/api/Auth/login orqali JWT token olinadi (~2 soat yashaydi,
     muddati tugasa qayta login qilinadi).
  2. api.multiavto.uz/api/multicard/WalletHistory'dan type=credit (Пополнение) va
     type=debit (Списание) tranzaksiyalari sahifalab olinadi.
  3. Faqat YANGI tranzaksiyalar (uuid bo'yicha) — dedup fayl (multicard_seen.json).
     Birinchi ishga tushishda mavjud tarix "ko'rilgan" deb belgilanadi va YOZILMAYDI
     (eski qatorlarni to'kib tashlamaslik uchun). Keyin faqat yangilari qo'shiladi.
  4. Har bir tranzaksiya master jadval ustunlariga (A->J) joylanadi:
     credit -> Приход, debit -> Расход.

Sozlamalar .env orqali (MULTICARD_*). Foydalanuvchi/parol bo'lmasa modul o'chiq turadi.

Test (sheet'ga yozmasdan, faqat login+olishni tekshirish):
    .venv/bin/python multicard.py test
"""

import os
import json
import base64
import hashlib
import asyncio
import datetime

import requests

# ---------- Sozlamalar (.env) ----------
USERNAME = os.getenv("MULTICARD_USERNAME")
PASSWORD = os.getenv("MULTICARD_PASSWORD")
DOMAIN = os.getenv("MULTICARD_DOMAIN", "thenovacore")

API = os.getenv("MULTICARD_API", "https://api.multiavto.uz").rstrip("/")
TENANT_URL = os.getenv("MULTICARD_TENANT_URL", f"https://{DOMAIN}.multiavto.uz").rstrip("/")
LOGIN_URL = os.getenv("MULTICARD_LOGIN_URL", API + "/api/account/token")

# credit = Пополнение, debit = Списание
TYPES = [t.strip() for t in os.getenv("MULTICARD_TYPES", "credit,debit").split(",") if t.strip()]
POLL_INTERVAL = int(os.getenv("MULTICARD_POLL_INTERVAL", "300"))   # soniya (default 5 daq)
LOOKBACK_DAYS = int(os.getenv("MULTICARD_LOOKBACK_DAYS", "3"))     # har so'rovda nechа kun orqaga
PAGE_SIZE = int(os.getenv("MULTICARD_PAGE_SIZE", "50"))            # sahifadagi yozuvlar soni
MAX_PAGES = int(os.getenv("MULTICARD_MAX_PAGES", "20"))            # sahifalar chegarasi
SEEN_FILE = os.getenv("MULTICARD_SEEN_FILE", "multicard_seen.json")
SEEN_PRUNE_DAYS = int(os.getenv("MULTICARD_SEEN_PRUNE_DAYS", "30"))  # bundan eski uuid'lar tozalanadi

TZ = datetime.timezone(datetime.timedelta(hours=5))  # Asia/Tashkent

# ---------- Holat (module global) ----------
_token = None
_token_exp = 0.0          # unix seconds
_seen = {}                # {uuid: "YYYY-MM-DD"}
_first_run = True         # SEEN_FILE mavjud bo'lmasa True — birinchi ishga tushish (baseline)


def enabled():
    """Modul yoqilganmi? (foydalanuvchi/parol berilgan bo'lsa)."""
    return bool(USERNAME and PASSWORD)


def _headers(auth=None):
    h = {
        "Accept": "application/json",
        "Origin": TENANT_URL,
        "Referer": TENANT_URL + "/",
        "User-Agent": "telegram-sheeter/multicard",
    }
    if auth:
        h["Authorization"] = "Bearer " + auth
    return h


# ---------- Token ----------
def _jwt_exp(token):
    """JWT ichidan 'exp' (unix seconds) ni o'qiydi. Bo'lmasa now+90min qaytaradi."""
    now = datetime.datetime.now(tz=datetime.timezone.utc).timestamp()
    try:
        payload = token.split(".")[1]
        payload += "=" * (-len(payload) % 4)          # base64 padding
        data = json.loads(base64.urlsafe_b64decode(payload))
        return float(data.get("exp", now + 5400))
    except Exception:
        return now + 5400


def _extract_token(data):
    """Login javobidan JWT'ni chiqaradi (javob shakli noma'lum bo'lgani uchun moslashuvchan)."""
    if isinstance(data, str):
        return data if data.count(".") >= 2 and len(data) > 60 else None
    if isinstance(data, dict):
        for k in ("token", "accessToken", "access_token", "jwt", "authToken",
                  "AUTHToken", "Token", "AccessToken"):
            v = data.get(k)
            if isinstance(v, str) and v.count(".") >= 2:
                return v
        for k in ("data", "result", "payload", "response"):
            if k in data:
                t = _extract_token(data[k])
                if t:
                    return t
        for v in data.values():
            if isinstance(v, str) and v.count(".") == 2 and len(v) > 60:
                return v
    return None


def _login():
    """api/account/token -> JWT (javob: {isSuccess, data:{token}}).
    Muvaffaqiyatsizlikda Exception ko'taradi."""
    global _token, _token_exp
    body = {"Login": USERNAME, "Password": PASSWORD, "Domain": DOMAIN,
            "ConfirmCode": None, "RememberMe": False}
    h = _headers()
    h["Authorization"] = "Bearer"  # frontend bo'sh Bearer yuboradi
    r = requests.post(LOGIN_URL, json=body, headers=h, timeout=30)
    try:
        data = r.json()
    except ValueError:
        data = r.text
    token = _extract_token(data)
    if not token:
        msg = data.get("message") if isinstance(data, dict) else str(data)
        raise RuntimeError(f"login token topilmadi (HTTP {r.status_code}): {str(msg)[:200]}")
    _token = token
    _token_exp = _jwt_exp(token)
    return token


def _get_token(force=False):
    now = datetime.datetime.now(tz=datetime.timezone.utc).timestamp()
    if force or not _token or now >= _token_exp - 120:   # 2 daq zaxira
        return _login()
    return _token


# ---------- Ko'rilgan uuid'lar (dedup) ----------
def _load_seen():
    global _seen, _first_run
    if not os.path.exists(SEEN_FILE):
        _seen = {}
        _first_run = True
        return
    _first_run = False
    try:
        with open(SEEN_FILE, "r", encoding="utf-8") as f:
            raw = json.load(f)
        _seen = raw if isinstance(raw, dict) else {u: "" for u in raw}
    except Exception:
        _seen = {}


def _save_seen():
    """SEEN_FILE'ni yozadi; SEEN_PRUNE_DAYS'dan eski uuid'larni tozalaydi."""
    try:
        cutoff = (datetime.datetime.now(TZ).date()
                  - datetime.timedelta(days=SEEN_PRUNE_DAYS)).isoformat()
        pruned = {u: d for u, d in _seen.items() if (d or "9999") >= cutoff}
        tmp = SEEN_FILE + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(pruned, f)
        os.replace(tmp, SEEN_FILE)
        _seen.clear()
        _seen.update(pruned)
    except Exception as e:
        print("MultiCard: seen saqlash xatosi:", e)


# ---------- WalletHistory ----------
def _fetch_page(tx_type, frm, to, page):
    """Bitta sahifani oladi. 401 bo'lsa qayta login qilib bir marta qayta urinadi."""
    url = API + "/api/multicard/WalletHistory"
    params = {"from": frm, "to": to, "page": page, "type": tx_type}
    r = requests.get(url, params=params, headers=_headers(_get_token()), timeout=30)
    if r.status_code == 401:
        r = requests.get(url, params=params, headers=_headers(_get_token(force=True)), timeout=30)
    r.raise_for_status()
    data = r.json()
    return data.get("data") or []


def _dedup_key(tx, tx_type):
    """Barqaror dedup kaliti. Debit qatorlarda `uuid` bor; credit (Пополнение)
    qatorlarda `uuid` null — shuning uchun date+amount+balance+note'dan hash yasaymiz."""
    u = tx.get("uuid")
    if u:
        return u
    raw = f"{tx_type}|{tx.get('date')}|{tx.get('amountValue')}|{tx.get('balance')}|{tx.get('note')}"
    return "h:" + hashlib.md5(raw.encode("utf-8")).hexdigest()


def _collect_new():
    """Barcha turlar bo'yicha lookback oynasidagi YANGI (ko'rilmagan) tranzaksiyalar.
    Qaytaradi: [(key, date, tx_type, tx_dict), ...] (eskidan yangiga qarab tartiblanmagan)."""
    today = datetime.datetime.now(TZ).date()
    frm = (today - datetime.timedelta(days=LOOKBACK_DAYS)).strftime("%d%m%y")
    to = (today + datetime.timedelta(days=1)).strftime("%d%m%y")

    found = []
    seen_now = set()
    for tx_type in TYPES:
        for page in range(1, MAX_PAGES + 1):
            rows = _fetch_page(tx_type, frm, to, page)
            if not rows:
                break
            for tx in rows:
                u = _dedup_key(tx, tx_type)
                if u in _seen or u in seen_now:
                    continue
                seen_now.add(u)
                date = str(tx.get("date", "")).split("T")[0]
                found.append((u, date, tx_type, tx))
            if len(rows) < PAGE_SIZE:
                break
    return found


# ---------- Sheet qatoriga o'girish ----------
def build_row(tx, tx_type):
    """MultiCard tranzaksiyasini master jadval ustunlariga (A->J) joylaydi.
    credit -> Приход, debit -> Расход. Summa (amountValue) allaqachon so'mda."""
    date = str(tx.get("date", "")).split("T")[0]
    try:
        amount = float(tx.get("amountValue") or 0)
    except (TypeError, ValueError):
        amount = tx.get("amountValue") or ""

    # Тўлов тури ustuniga — kirim/chiqim ikkalasida ham "Мультисард" (karta raqami yozilmaydi).
    pay = "Мультисард"

    info = "MultiCard пополнение" if tx_type == "credit" else "MultiCard списание"
    dt = str(tx.get("date", "")).replace("T", " ")
    note = (tx.get("note") or "").strip()
    izoh = f"{dt} · {note}" if note else dt

    prixod = tolov_p = rasxod = tolov_r = ""
    if tx_type == "credit":       # Пополнение -> kirim
        prixod, tolov_p = amount, pay
    else:                          # debit -> chiqim
        rasxod, tolov_r = amount, pay

    # Сана | Фирма/Филиал | (филиал) | Инфо | Статья | Приход | Тўлов Приход | Расход | Тўлов Расход | Изоҳ
    return [date, "", "", info, "", prixod, tolov_p, rasxod, tolov_r, izoh]


# ---------- Fon vazifasi ----------
async def worker(append_rows_batch, queue_row, pending_lock):
    """Har POLL_INTERVAL soniyada MultiCard'ni tekshiradi va yangi qatorlarni yozadi.

    append_rows_batch(rows) -> bool : qatorlar ro'yxatini Sheets'ga yozadi (main.py'dan).
    queue_row(row)                  : yozib bo'lmagan qatorni navbat fayliga qo'shadi.
    pending_lock                    : navbat fayli uchun asyncio.Lock (main.py'dan).
    """
    global _first_run
    _load_seen()
    print(f"MultiCard: worker yoqildi (turlar={TYPES}, interval={POLL_INTERVAL}s, "
          f"lookback={LOOKBACK_DAYS}k). Birinchi ishga tushish: {_first_run}")

    while True:
        try:
            found = await asyncio.to_thread(_collect_new)

            if _first_run:
                # Baseline: mavjud tarixni "ko'rilgan" deb belgilaymiz, YOZMAYMIZ.
                for u, date, _t, _tx in found:
                    _seen[u] = date or datetime.datetime.now(TZ).date().isoformat()
                await asyncio.to_thread(_save_seen)
                _first_run = False
                print(f"MultiCard: baseline — {len(found)} ta mavjud tranzaksiya "
                      f"'ko'rilgan' deb belgilandi (yozilmadi). Bundan keyin faqat yangilari.")
            elif found:
                # Eskidan yangiga: sana bo'yicha o'sish tartibida yozamiz.
                found.sort(key=lambda x: (x[3].get("date") or ""))
                rows = [build_row(tx, t) for (_u, _d, t, tx) in found]
                ok = await asyncio.to_thread(append_rows_batch, rows)
                if not ok:
                    async with pending_lock:
                        for r in rows:
                            queue_row(r)
                    print(f"MultiCard: Sheets xato — {len(rows)} qator navbatga saqlandi")
                else:
                    print(f"MultiCard: {len(rows)} yangi tranzaksiya yozildi")
                # Yozilgan yoki navbatga qo'yilgan — ikkalasida ham 'ko'rilgan' deb belgilaymiz.
                for u, date, _t, _tx in found:
                    _seen[u] = date or datetime.datetime.now(TZ).date().isoformat()
                await asyncio.to_thread(_save_seen)
        except Exception as e:
            print("MultiCard worker xatosi:", repr(e))

        await asyncio.sleep(POLL_INTERVAL)


# ---------- CLI test (sheet'ga yozmaydi) ----------
def _selftest():
    if not enabled():
        print("MULTICARD_USERNAME/PASSWORD .env'da yo'q — modul o'chiq.")
        return
    print(f"Login -> {LOGIN_URL} (Login={USERNAME}, Domain={DOMAIN}) ...")
    try:
        tok = _get_token(force=True)
        print(f"  OK, token olindi (exp={datetime.datetime.fromtimestamp(_token_exp)})")
    except Exception as e:
        print("  LOGIN XATO:", e)
        return
    _load_seen()
    found = _collect_new()
    print(f"\nLookback {LOOKBACK_DAYS} kun, turlar {TYPES}: {len(found)} ta tranzaksiya topildi.")
    for u, date, t, tx in found[:8]:
        print(f"  [{t:6}] {date}  {tx.get('amountValue')}  {tx.get('cardPan') or tx.get('note','')[:40]}")
    if found:
        print("\nNamuna qator (build_row):")
        u, date, t, tx = found[0]
        print(" ", build_row(tx, t))


if __name__ == "__main__":
    import sys
    if len(sys.argv) > 1 and sys.argv[1] == "test":
        from dotenv import load_dotenv
        load_dotenv()
        # Standalone ishga tushirishda config import paytida (load_dotenv'dan oldin)
        # o'qilgan bo'lishi mumkin — muhim qiymatlarni qayta o'qiymiz.
        USERNAME = os.getenv("MULTICARD_USERNAME")
        PASSWORD = os.getenv("MULTICARD_PASSWORD")
        DOMAIN = os.getenv("MULTICARD_DOMAIN", "thenovacore")
        TENANT_URL = os.getenv("MULTICARD_TENANT_URL", f"https://{DOMAIN}.multiavto.uz").rstrip("/")
        _selftest()
    else:
        print("Ishlatish: python multicard.py test")
