"""Mavjud  session.session faylini StringSession matniga aylantiradi
va 'tg_session.txt' ga yozadi (Fly secret uchun). Qayta login shart emas.
Ishlatish:  python export_session.py   (yoki: make session)"""
import os
from telethon.sync import TelegramClient
from telethon.sessions import StringSession
from dotenv import load_dotenv

load_dotenv()

with TelegramClient("session", int(os.getenv("API_ID")), os.getenv("API_HASH")) as client:
    s = StringSession.save(client.session)

with open("tg_session.txt", "w") as f:
    f.write(s)

print("Sessiya 'tg_session.txt' ga yozildi. Endi 'make secrets' (yoki 'make ship') ishlating.")
