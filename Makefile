# Telegram -> Google Sheets bot — boshqaruv va deploy buyruqlari.
# Ishlatish:  make <buyruq>     (masalan: make ship)
# Buyruqlar ro'yxati:  make help

-include .env

PY  := .venv/bin/python
APP := telegram-sheeter

.DEFAULT_GOAL := help
.PHONY: help run session secrets deploy scale ship logs status ssh restart destroy

help: ## Mavjud buyruqlar ro'yxati
	@grep -hE '^[a-zA-Z_-]+:.*?## .*$$' Makefile \
	  | awk 'BEGIN{FS=":.*?## "}{printf "  \033[36m%-10s\033[0m %s\n", $$1, $$2}'

# ---------- Lokal ----------
run: ## Lokal ishga tushirish (shu kompyuterda)
	$(PY) main.py

session: ## Sessiyani tg_session.txt ga matn qilib chiqaradi (Fly uchun)
	$(PY) export_session.py

# ---------- Fly.io deploy ----------
secrets: ## Fly secret'larini .env, creds.json va tg_session.txt dan o'rnatadi
	@test -f tg_session.txt || { echo "Avval 'make session' ishlating"; exit 1; }
	@test -f creds.json     || { echo "creds.json topilmadi"; exit 1; }
	fly secrets set \
	  API_ID="$(API_ID)" \
	  API_HASH="$(API_HASH)" \
	  SOURCE="$(SOURCE)" \
	  SHEET_ID="$(SHEET_ID)" \
	  TG_SESSION="$$(cat tg_session.txt)" \
	  GOOGLE_CREDS="$$(cat creds.json)"

deploy: ## Fly'ga deploy qilish
	fly deploy

scale: ## Bitta mashinaga tushirish (dublikat yozuvning oldini oladi)
	fly scale count 1 --yes

ship: session secrets deploy scale ## TO'LIQ DEPLOY: session + secrets + deploy + scale
	@echo "✅ Tayyor. 'make logs' bilan kuzating."

# ---------- Kuzatuv / boshqaruv ----------
logs: ## Jonli log'lar
	fly logs

status: ## Mashinalar holati
	fly status

ssh: ## Ishlab turgan mashinaga kirish
	fly ssh console

restart: ## Botni qayta ishga tushirish
	fly apps restart $(APP)

destroy: ## Appni butunlay o'chirish (EHTIYOT BO'LING)
	fly apps destroy $(APP)
