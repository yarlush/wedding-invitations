"""Заполняет демо-БД правдоподобными ответами гостей через публичный API.

Использование:
    py samples/fill_demo_answers.py [BASE_URL] [--reset]

По умолчанию BASE_URL = http://127.0.0.1:5199.
Скрипт читает токены из samples/demo_guests.json.
"""
from __future__ import annotations

import argparse
import http.cookiejar
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
GUESTS_JSON = ROOT / "samples" / "demo_guests.json"
USER = os.environ.get("ADMIN_USERNAME", "admin")
PASSWORD = os.environ.get("ADMIN_PASSWORD", "admin")

RESPONSES = {
    "Анна Смирнова": {
        "attendance": {"1": "yes", "2": "yes"},
        "answers": {"1": "Мясо", "3": "да", "4": json.dumps(["Вино", "Шампанское"], ensure_ascii=False),
                    "5": "La Vie en Rose", "6": "Люблю танцевать и путешествовать."},
        "companion": {"name": "Алексей", "answers": {"2": "Рыба", "7": "Будьте счастливы!"}},
    },
    "Михаил Иванов": {
        "attendance": {"1": "yes", "2": "no"},
        "answers": {"1": "Мясо", "3": "нет", "4": json.dumps(["Безалкогольные напитки"], ensure_ascii=False),
                    "5": "Nothing Else Matters", "6": "Друг жениха."},
    },
    "Елена Кузнецова": {
        "attendance": {"1": "yes", "2": "yes"},
        "answers": {"1": "Вегетарианское", "3": "да", "4": json.dumps(["Безалкогольные напитки"], ensure_ascii=False),
                    "5": "Dancing Queen", "6": "Коллега невесты."},
    },
    "Павел Морозов": {
        "attendance": {"1": "yes", "2": "yes"},
        "answers": {"1": "Мясо", "3": "нет", "4": json.dumps(["Вино"], ensure_ascii=False),
                    "5": "Shape of You", "6": "Двоюродный брат."},
        "companion": {"name": "Ольга", "answers": {"2": "Рыба", "7": "Совет да любовь!"}},
    },
    "Ольга Соколова": {
        "attendance": {"2": "yes"},
        "answers": {"1": "Рыба", "3": "да", "4": json.dumps(["Шампанское"], ensure_ascii=False),
                    "5": "Fly Me to the Moon", "6": "Подруга невесты."},
    },
    "Дмитрий Попов": {
        "attendance": {"2": "yes"},
        "answers": {"1": "Мясо", "3": "нет", "4": json.dumps(["Вино", "Безалкогольные напитки"], ensure_ascii=False),
                    "5": "Believer", "6": "Друг семьи."},
        "companion": {"name": "Ксения", "answers": {"2": "Вегетарианское", "7": "Мира и добра!"}},
    },
    "Татьяна Лебедева": {
        "attendance": {"1": "yes", "2": "yes"},
        "answers": {"1": "Рыба", "3": "нет", "4": json.dumps(["Вино"], ensure_ascii=False),
                    "5": "Ain't No Mountain High Enough", "6": "Тётя невесты."},
    },
    "Сергей Новиков": {
        "attendance": {"1": "yes"},
        "answers": {"5": "Here Comes the Sun", "6": "Друг жениха."},
    },
    "Ирина Волкова": {
        "attendance": {"1": "yes", "2": "yes"},
        "answers": {"1": "Вегетарианское", "3": "да", "4": json.dumps(["Безалкогольные напитки"], ensure_ascii=False),
                    "5": "Perfect", "6": "Сестра жениха."},
        "companion": {"name": "Максим", "answers": {"2": "Мясо", "7": "Мира и любви!"}},
    },
    "Алексей Фёдоров": {
        "attendance": {"2": "yes"},
        "answers": {"1": "Мясо", "3": "да", "4": json.dumps(["Шампанское"], ensure_ascii=False),
                    "5": "Uptown Funk", "6": "Друг."},
    },
}

ANNA_SECOND = {
    "attendance": {"1": "yes", "2": "yes"},
    "answers": {"1": "Рыба", "3": "да", "4": json.dumps(["Вино"], ensure_ascii=False),
                "5": "La Vie en Rose", "6": "Люблю танцевать и путешествовать."},
    "companion": {"name": "Алексей", "answers": {"2": "Рыба", "7": "Будьте счастливы!"}},
}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("base", nargs="?", default="http://127.0.0.1:5199")
    parser.add_argument("--reset", action="store_true")
    args = parser.parse_args()

    guests = json.loads(GUESTS_JSON.read_text(encoding="utf-8"))
    by_name = {g["name"]: g for g in guests}

    jar = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))

    def call(method, path, body=None):
        data = None
        headers = {}
        if body is not None:
            data = json.dumps(body, ensure_ascii=False).encode("utf-8")
            headers["Content-Type"] = "application/json"
        req = urllib.request.Request(args.base + path, data=data, headers=headers, method=method)
        try:
            with opener.open(req, timeout=30) as resp:
                raw = resp.read().decode("utf-8")
                return resp.status, json.loads(raw) if raw else None
        except urllib.error.HTTPError as e:
            raw = e.read().decode("utf-8")
            try:
                return e.code, json.loads(raw) if raw else None
            except Exception:
                return e.code, raw

    st, _ = call("POST", "/api/admin/login", {"username": USER, "password": PASSWORD})
    if st != 200:
        print("login failed:", st)
        sys.exit(1)

    if args.reset:
        st, res = call("POST", "/api/admin/tools/reset-all", {})
        print("reset:", st, res)

    def submit(name, payload, label=""):
        guest = by_name[name]
        st, res = call("POST", f"/api/invite/{guest['token']}", payload)
        mark = "OK" if st == 200 else "ERR"
        print(f"{mark} {name:<22} {label:<10} status={st} {'' if st == 200 else res}")
        return st == 200

    for name, payload in RESPONSES.items():
        submit(name, payload)

    submit("Анна Смирнова", ANNA_SECOND, "повторно")

    st, answers = call("GET", "/api/admin/answers")
    if st == 200:
        print("events:", len(answers.get("events", [])), "submissions:", len(answers.get("submissions", [])))
    st, stats = call("GET", "/api/admin/tools/stats")
    if st == 200:
        print("stats:", json.dumps(stats, ensure_ascii=False))


if __name__ == "__main__":
    main()
