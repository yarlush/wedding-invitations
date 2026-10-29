# Свадебные приглашения

Демонстрационный сервис персональных свадебных приглашений: ASP.NET Core 10
Minimal API, Entity Framework Core, SQLite и чистый HTML/CSS/JavaScript.

Все гости, события, вопросы и ответы в репозитории вымышлены. Реальные списки
гостей, фотографии и рабочие базы в репозиторий не входят.

## Возможности

- персональная ссылка `/invite/{token}` для каждого гостя;
- события с признаком «разрешён +1» и отдельным статусом участия;
- вопросы: текст, да/нет, один и несколько вариантов;
- обязательные вопросы и вопросы для спутника;
- повторная отправка с обновлением текущих ответов и историей в `Submissions`;
- административная панель: гости, события, вопросы, ответы, шаблоны;
- выгрузка Excel: итоги, листы событий и общих вопросов;
- HTML-шаблоны хранятся в базе и меняются без перезапуска;
- загрузка изображений в шаблон через `/uploads/{id}`;
- cookie-аутентификация администратора, `noindex` для приглашений.

## Стек

- .NET 10, ASP.NET Core Minimal API;
- Entity Framework Core, SQLite;
- AngleSharp для шаблонов, ClosedXML для Excel;
- HTML/CSS/JavaScript без SPA-фреймворка;
- Docker, systemd, Caddy, HTTPS.

## Быстрый старт

Требуется .NET 10 SDK.

```
cd src/Wedding.Api
dotnet restore
dotnet run
```

Приложение откроется на `http://localhost:5270`. Админка:
`http://localhost:5270/admin/`. Логин и пароль по умолчанию — `admin` / `admin`;
для любого публичного запуска их нужно заменить.

Демонстрационный сервер с готовыми ответами:

```
py samples/run_demo.py --seed --fill
py samples/run_demo.py --stop
```

`run_demo.py` запускает приложение на порту 5199 и использует `demo.db`.

## Демо-данные

В репозитории есть две одинаковые демонстрационные базы:

- `src/Wedding.Api/demo.db` — используется скриптом `run_demo.py`;
- `src/Wedding.Api/seed/wedding.db` — база для переменной `SEED_DB`.

В демо-базе 12 гостей, 2 события, 7 вопросов, 55 ответов, 11 отправок и
4 спутника. Токены гостей лежат в `samples/demo_guests.json`.

## Скрипты

| Скрипт | Назначение |
|---|---|
| `samples/run_demo.py` | Запуск демо-сервера на копии `demo.db` |
| `samples/seed_demo.py` | Пересоздание `demo.db` из `seed/wedding.db` |
| `samples/fill_demo_answers.py` | Заполнение демо-ответов через API |

Скрипты рассчитаны на локальную демонстрацию и не содержат реальных данных.
Демонстрационные изображения уже лежат в `wwwroot/assets`.

## API

Публичные маршруты:

- `GET /invite/{token}` — HTML приглашения;
- `POST /api/invite/{token}` — ответы гостя в JSON;
- `GET /confirm/{token}` — страница подтверждения;
- `GET /uploads/{id}` — изображение из базы;
- `POST /api/admin/login`, `POST /api/admin/logout`, `GET /api/admin/me`.

Административные маршруты находятся в `/api/admin` и требуют cookie:
`guests`, `events`, `questions`, `answers`, `export.xlsx`, `template`,
`uploads`, `tools`.

Ошибки API возвращаются в виде `{ "error": "..." }`, успешные ответы — JSON.
Маршруты и DTO описаны в `src/Wedding.Api/Api/`.

## Конфигурация

| Переменная | Назначение | По умолчанию |
|---|---|---|
| `ADMIN_USERNAME` | Логин администратора | `admin` |
| `ADMIN_PASSWORD` | Пароль администратора | `admin` |
| `DB_PATH` | Путь к файлу SQLite | `wedding.db` |
| `PORT` | Порт HTTP, если задаёт хостинг | — |
| `ASPNETCORE_URLS` | Адрес прослушивания | из launchSettings |
| `SEED_DB` | Демо-база для первого запуска | — |

`SEED_DB` копируется в `DB_PATH`, только если в рабочей базе ещё нет гостей.

## Docker

```
docker build -t wedding:latest .
docker run -d --name wedding \
  -p 127.0.0.1:8080:8080 \
  -v /data:/data \
  -e DB_PATH=/data/wedding.db \
  -e ADMIN_USERNAME=admin \
  -e ADMIN_PASSWORD=change-me \
  wedding:latest
```

Порт 8080 публикуется только на `127.0.0.1`: снаружи приложение должно быть
доступно через обратный прокси.

## Развёртывание на VPS

В каталоге `deploy` есть примеры для Linux:

- `deploy/Caddyfile` — HTTPS и reverse proxy;
- `deploy/wedding.service` — systemd-юнит для контейнера.

Замените `wedding.example.com` на свой домен, а `change-me` — на надёжный
пароль. Caddy сам получит сертификат Let's Encrypt. SQLite должна лежать на
постоянном диске `/data`.

## Amvera и Railway

- `amvera.yml` — Docker-сборка, постоянное хранилище `/data`, порт 8080.
- `railway.json` — сборка по Dockerfile и перезапуск при ошибке.

На обеих платформах задайте `ADMIN_USERNAME`, `ADMIN_PASSWORD`,
`DB_PATH=/data/wedding.db` и подключите постоянный том.

## Безопасность

- смените `admin/admin` до публикации;
- используйте HTTPS: cookie администратора должна передаваться только по TLS;
- не публикуйте порт 8080 напрямую;
- ограничьте доступ к служебной группе `/api/admin/tools` в production;
- не добавляйте в репозиторий реальные списки гостей, фотографии и рабочие базы.

## Структура

```
src/Wedding.Api/
  Program.cs          — запуск, DI, middleware, маршруты
  Data/               — модели EF Core и AppDbContext
  Migrations/         — миграции SQLite
  Api/                — Minimal API endpoint'ы
  Services/           — InvitationService, TemplateService, ExcelService
  wwwroot/admin/      — HTML/CSS/JS административной панели
  wwwroot/assets/     — демонстрационные изображения
samples/              — скрипты демо-данных и запуска
deploy/               — примеры Caddy и systemd
Dockerfile            — сборка контейнера
amvera.yml            — конфигурация Amvera
railway.json          — конфигурация Railway
```
