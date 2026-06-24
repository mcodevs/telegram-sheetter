FROM python:3.11-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY main.py .

# -u = bufferlanmagan log (Fly logs'da darhol ko'rinadi)
CMD ["python", "-u", "main.py"]
