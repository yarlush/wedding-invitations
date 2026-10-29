"""Создаёт демонстрационную БД: вымышленная пара, гости, события, вопросы и шаблоны.

Использование:
    py samples/seed_demo.py [--base путь] [--db путь]

По умолчанию базовая схема берётся из src/Wedding.Api/wedding.db,
результат пишется в src/Wedding.Api/demo.db.
"""
from __future__ import annotations

import argparse
import json
import secrets
import shutil
import sqlite3
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_BASE = ROOT / "src" / "Wedding.Api" / "seed" / "wedding.db"
DEFAULT_DB = ROOT / "src" / "Wedding.Api" / "demo.db"
GUESTS_JSON = ROOT / "samples" / "demo_guests.json"

EVENTS = [
    {
        "id": 1,
        "name": "Церемония",
        "date": "2027-06-12 15:30:00",
        "location": "Загородный клуб «Лесная усадьба», Москва",
        "description": "Сбор гостей, приветственный напиток и торжественная церемония на открытой площадке.",
        "order": 1,
    },
    {
        "id": 2,
        "name": "Банкет",
        "date": "2027-06-12 18:00:00",
        "location": "Ресторан «Лесная усадьба»",
        "description": "Ужин, танцы и поздравления. Дресс-код: оливковый, пудровый и золотой.",
        "order": 2,
    },
]

QUESTIONS = [
    {
        "id": 1,
        "event_id": 2,
        "text": "Выбор горячего блюда",
        "type": 1,
        "options": ["Рыба", "Мясо", "Вегетарианское"],
        "per_companion": 0,
        "required": 1,
        "order": 1,
    },
    {
        "id": 2,
        "event_id": 2,
        "text": "Блюдо для спутника",
        "type": 1,
        "options": ["Рыба", "Мясо", "Вегетарианское"],
        "per_companion": 1,
        "required": 0,
        "order": 2,
    },
    {
        "id": 3,
        "event_id": 2,
        "text": "Нужен ли вам трансфер?",
        "type": 3,
        "options": [],
        "per_companion": 0,
        "required": 0,
        "order": 3,
    },
    {
        "id": 4,
        "event_id": 2,
        "text": "Какие напитки предпочитаете?",
        "type": 2,
        "options": ["Вино", "Шампанское", "Безалкогольные напитки"],
        "per_companion": 0,
        "required": 0,
        "order": 4,
    },
    {
        "id": 5,
        "event_id": None,
        "text": "Какой трек поставить для вас на вечере?",
        "type": 0,
        "options": [],
        "per_companion": 0,
        "required": 0,
        "order": 10,
    },
    {
        "id": 6,
        "event_id": None,
        "text": "Расскажите пару слов о себе — для тёплых слов и тостов",
        "type": 0,
        "options": [],
        "per_companion": 0,
        "required": 0,
        "order": 20,
    },
    {
        "id": 7,
        "event_id": None,
        "text": "Пожелания для вашей пары",
        "type": 0,
        "options": [],
        "per_companion": 1,
        "required": 0,
        "order": 30,
    },
]

GUESTS = [
    ("Анна Смирнова", {1: 1, 2: 1}),
    ("Михаил Иванов", {1: 0, 2: 0}),
    ("Елена Кузнецова", {1: 0, 2: 0}),
    ("Павел Морозов", {1: 1, 2: 1}),
    ("Ольга Соколова", {2: 0}),
    ("Дмитрий Попов", {2: 1}),
    ("Татьяна Лебедева", {1: 0, 2: 0}),
    ("Сергей Новиков", {1: 0}),
    ("Ирина Волкова", {1: 1, 2: 1}),
    ("Алексей Фёдоров", {2: 0}),
    ("Мария Павлова", {1: 0, 2: 0}),
    ("Никита Орлов", {1: 0, 2: 0}),
]


def build(db_path: Path, base_path: Path) -> None:
    if db_path.exists():
        db_path.unlink()
    shutil.copy2(base_path, db_path)

    con = sqlite3.connect(db_path)
    cur = con.cursor()
    for table in ("Answers", "Submissions", "Companions", "GuestEvents", "Questions", "Events", "Guests", "Uploads", "Settings"):
        cur.execute(f'DELETE FROM "{table}"')
    cur.execute("DELETE FROM sqlite_sequence WHERE name IN ('Answers','Submissions','Companions','GuestEvents','Questions','Events','Guests','Uploads')")

    for ev in EVENTS:
        cur.execute(
            'INSERT INTO Events (Id, Name, Date, Location, Description, "Order") VALUES (?,?,?,?,?,?)',
            (ev["id"], ev["name"], ev["date"], ev["location"], ev["description"], ev["order"]),
        )

    for q in QUESTIONS:
        cur.execute(
            'INSERT INTO Questions (Id, EventId, Text, Type, Options, IsPerCompanion, IsRequired, "Order", IsActive) VALUES (?,?,?,?,?,?,?,?,1)',
            (
                q["id"],
                q["event_id"],
                q["text"],
                q["type"],
                json.dumps(q["options"], ensure_ascii=False) if q["options"] else "",
                q["per_companion"],
                q["required"],
                q["order"],
            ),
        )

    now = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S")
    mapping = []
    for gid, (name, events) in enumerate(GUESTS, start=1):
        token = secrets.token_hex(16)
        cur.execute("INSERT INTO Guests (Id, Name, Token, CreatedAt) VALUES (?,?,?,?)", (gid, name, token, now))
        for event_id, plus in events.items():
            cur.execute(
                "INSERT INTO GuestEvents (GuestId, EventId, CompanionsAllowed, Attendance) VALUES (?,?,?,0)",
                (gid, event_id, plus),
            )
        mapping.append({"id": gid, "name": name, "token": token, "events": events})

    templates = {
        "InvitationTemplate": (ROOT / "samples" / "template-wedding.html").read_text(encoding="utf-8"),
        "ConfirmationTemplate": (ROOT / "samples" / "template-confirmation.html").read_text(encoding="utf-8"),
    }
    for key, value in templates.items():
        cur.execute("INSERT INTO Settings (Key, Value) VALUES (?,?)", (key, value))

    con.commit()
    con.close()
    GUESTS_JSON.write_text(json.dumps(mapping, ensure_ascii=False, indent=2), encoding="utf-8")

    print("database:", db_path)
    print("guests:", len(mapping), "events:", len(EVENTS), "questions:", len(QUESTIONS))
    for item in mapping:
        print(f"  {item['name']:<22} /invite/{item['token']}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", type=Path, default=DEFAULT_BASE)
    parser.add_argument("--db", type=Path, default=DEFAULT_DB)
    args = parser.parse_args()
    build(args.db.resolve(), args.base.resolve())


if __name__ == "__main__":
    main()
