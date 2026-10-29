"""Снимает скриншоты демонстрационного стенда для отчёта.

Использование:
    py samples/take_screenshots.py [BASE_URL]
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path

from playwright.sync_api import sync_playwright
from PIL import Image, ImageOps

ROOT = Path(__file__).resolve().parent.parent
GUESTS = ROOT / "samples" / "demo_guests.json"
OUT = ROOT / "docs" / "report" / "images"
CHROME = Path(r"C:\Program Files\Google\Chrome\Application\chrome.exe")
ADMIN_USER = os.environ.get("ADMIN_USERNAME", "admin")
ADMIN_PASSWORD = os.environ.get("ADMIN_PASSWORD", "admin")


def border(path: Path) -> None:
    img = Image.open(path).convert("RGB")
    img = ImageOps.expand(img, border=1, fill=(214, 206, 193))
    img.save(path, quality=95, optimize=True)


def take(base: str, out: Path) -> None:
    out.mkdir(parents=True, exist_ok=True)
    guests = json.loads(GUESTS.read_text(encoding="utf-8"))
    by_name = {g["name"]: g for g in guests}
    anna = by_name["Анна Смирнова"]
    elena = by_name["Елена Кузнецова"]

    with sync_playwright() as p:
        browser = p.chromium.launch(executable_path=str(CHROME) if CHROME.exists() else None)
        desktop = browser.new_context(
            viewport={"width": 1440, "height": 1000},
            device_scale_factor=1.5,
            locale="ru-RU",
            timezone_id="Europe/Moscow",
            reduced_motion="reduce",
        )
        page = desktop.new_page()

        def fonts():
            try:
                page.evaluate("async () => { if (document.fonts && document.fonts.ready) await document.fonts.ready; }")
            except Exception:
                pass

        page.goto(f"{base}/admin/login.html", wait_until="networkidle")
        fonts()
        page.screenshot(path=out / "screen-admin-login.png")
        border(out / "screen-admin-login.png")

        page.fill("#username", ADMIN_USER)
        page.fill("#password", ADMIN_PASSWORD)
        page.click("#login-btn")
        page.wait_for_url("**/admin/index.html", timeout=15000)
        page.wait_for_selector("#cards .card", timeout=15000)
        fonts()
        page.screenshot(path=out / "screen-admin-dashboard.png", full_page=True)
        border(out / "screen-admin-dashboard.png")

        for name, path, selector in [
            ("screen-admin-guests.png", "/admin/guests.html", "#rows tr"),
            ("screen-admin-events.png", "/admin/events.html", "main table"),
            ("screen-admin-questions.png", "/admin/questions.html", "main table"),
            ("screen-admin-answers.png", "/admin/answers.html", "#content .panel"),
            ("screen-admin-template.png", "/admin/template.html", "#html"),
        ]:
            page.goto(base + path, wait_until="networkidle")
            page.wait_for_selector(selector, timeout=15000)
            fonts()
            page.screenshot(path=out / name, full_page=True)
            border(out / name)

        answers = out / "screen-admin-answers.png"
        if answers.exists():
            img = Image.open(answers)
            img.crop((0, 0, img.width, min(img.height, 1900))).save(
                out / "screen-admin-answers-top.png", optimize=True)
            border(out / "screen-admin-answers-top.png")

        page.goto(f"{base}/invite/{anna['token']}", wait_until="networkidle")
        fonts()
        page.screenshot(path=out / "screen-invite-top.png")
        border(out / "screen-invite-top.png")

        page.goto(f"{base}/invite/{elena['token']}", wait_until="networkidle")
        fonts()
        page.locator("#events").scroll_into_view_if_needed()
        page.screenshot(path=out / "screen-invite-form.png")
        border(out / "screen-invite-form.png")

        page.locator("#questions-title").scroll_into_view_if_needed()
        page.screenshot(path=out / "screen-invite-questions.png")
        border(out / "screen-invite-questions.png")

        page.goto(f"{base}/confirm/{anna['token']}", wait_until="networkidle")
        fonts()
        page.screenshot(path=out / "screen-confirm.png", full_page=True)
        border(out / "screen-confirm.png")

        mobile = browser.new_context(
            viewport={"width": 390, "height": 844},
            device_scale_factor=2,
            is_mobile=True,
            has_touch=True,
            locale="ru-RU",
            timezone_id="Europe/Moscow",
            reduced_motion="reduce",
        )
        mpage = mobile.new_page()
        mpage.goto(f"{base}/invite/{anna['token']}", wait_until="networkidle")
        try:
            mpage.evaluate("async () => { if (document.fonts && document.fonts.ready) await document.fonts.ready; }")
        except Exception:
            pass
        mpage.screenshot(path=out / "screen-invite-mobile.png")
        border(out / "screen-invite-mobile.png")

        browser.close()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("base", nargs="?", default="http://127.0.0.1:5199")
    parser.add_argument("--out", type=Path, default=OUT)
    args = parser.parse_args()
    take(args.base.rstrip("/"), args.out)
    print("screenshots:", args.out)


if __name__ == "__main__":
    main()
