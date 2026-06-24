"""Mavjud 'session.session' faylini StringSession matniga aylantiradi.
Ishlatish:  python export_session.py
Chiqqan matnni TG_SESSION secret sifatida Fly'ga beramiz (qayta login shart emas)."""
import os
from telethon.sync import TelegramClient
from telethon.sessions import StringSession
from dotenv import load_dotenv

load_dotenv()

with TelegramClient("session", int(os.getenv("API_ID")), os.getenv("API_HASH")) as client:
    print("\n===== TG_SESSION (shuni nusxalang) =====\n")
    print(StringSession.save(client.session))
    print("\n========================================\n")
