from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "Wedding.Api" / "wwwroot" / "assets"
ASSETS.mkdir(parents=True, exist_ok=True)

CREAM = (251, 247, 239)
PAPER = (255, 253, 248)
INK = (74, 64, 52)
MUTED = (111, 99, 80)
GOLD = (185, 138, 61)
GOLD_DARK = (143, 106, 42)
GOLD_LIGHT = (224, 201, 138)
SAGE = (122, 140, 115)
SAGE_LIGHT = (190, 203, 178)
DARK_GREEN = (47, 74, 58)
BLUSH = (217, 167, 160)
ROSE = (179, 87, 77)
WHITE = (255, 255, 255)

FONT_DIR = Path(r"C:\Windows\Fonts")
GEORGIA = FONT_DIR / "georgia.ttf"
GEORGIA_BOLD = FONT_DIR / "georgiab.ttf"
GEORGIA_ITALIC = FONT_DIR / "georgiai.ttf"
SEGOE_LIGHT = FONT_DIR / "segoeuil.ttf"


def font(size: int, bold: bool = False, italic: bool = False) -> ImageFont.FreeTypeFont:
    path = GEORGIA_BOLD if bold else GEORGIA_ITALIC if italic else GEORGIA
    return ImageFont.truetype(str(path), size)


def light_font(size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(SEGOE_LIGHT), size)


def mix(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def gradient(size, top, bottom):
    w, h = size
    img = Image.new("RGB", size)
    d = ImageDraw.Draw(img)
    for y in range(h):
        d.line([(0, y), (w, y)], fill=mix(top, bottom, y / max(1, h - 1)))
    return img


def paper(size, top=CREAM, bottom=(244, 235, 220)):
    img = gradient(size, top, bottom)
    noise = Image.effect_noise(size, 16).convert("L").convert("RGB")
    img = Image.blend(img, noise, 0.035)
    return img.convert("RGBA")


def glow(img, position, radius, color, opacity=90):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    x, y = position
    d.ellipse([x - radius, y - radius, x + radius, y + radius], fill=color + (opacity,))
    layer = layer.filter(ImageFilter.GaussianBlur(radius * 0.55))
    return Image.alpha_composite(img, layer)


def watercolor(img, blobs, blur=55):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for x, y, rx, ry, color, alpha in blobs:
        d.ellipse([x - rx, y - ry, x + rx, y + ry], fill=color + (alpha,))
    layer = layer.filter(ImageFilter.GaussianBlur(blur))
    return Image.alpha_composite(img, layer)


def cubic(p0, p1, p2, p3, t):
    x = (1 - t) ** 3 * p0[0] + 3 * (1 - t) ** 2 * t * p1[0] + 3 * (1 - t) * t ** 2 * p2[0] + t ** 3 * p3[0]
    y = (1 - t) ** 3 * p0[1] + 3 * (1 - t) ** 2 * t * p1[1] + 3 * (1 - t) * t ** 2 * p2[1] + t ** 3 * p3[1]
    return x, y


def leaf_polygon(cx, cy, length, width, angle):
    pts = []
    steps = 24
    for i in range(steps + 1):
        t = i / steps
        x = length * (t - 0.5)
        y = width * math.sin(math.pi * t) * 0.5
        pts.append((x, y))
    for i in range(steps - 1, -1, -1):
        t = i / steps
        x = length * (t - 0.5)
        y = -width * math.sin(math.pi * t) * 0.5
        pts.append((x, y))
    ca, sa = math.cos(angle), math.sin(angle)
    return [(cx + x * ca - y * sa, cy + x * sa + y * ca) for x, y in pts]


def branch(img, p0, p1, p2, p3, color, width=3, leaf_color=None, leaf_scale=1.0, seed=1, opacity=255):
    rnd = random.Random(seed)
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    points = [cubic(p0, p1, p2, p3, t / 80) for t in range(81)]
    d.line(points, fill=color + (opacity,), width=width, joint="curve")
    leaf_color = leaf_color or color
    for i in range(6, 76, 7):
        t = i / 80
        x, y = cubic(p0, p1, p2, p3, t)
        x2, y2 = cubic(p0, p1, p2, p3, min(1, t + 0.01))
        angle = math.atan2(y2 - y, x2 - x)
        side = 1 if (i // 7) % 2 == 0 else -1
        length = rnd.uniform(26, 42) * leaf_scale
        w = rnd.uniform(11, 17) * leaf_scale
        lx = x + math.cos(angle + side * math.pi / 2.4) * length * 0.42
        ly = y + math.sin(angle + side * math.pi / 2.4) * length * 0.42
        pts = leaf_polygon(lx, ly, length, w, angle + side * math.pi / 2.4)
        d.polygon(pts, fill=leaf_color + (max(0, opacity - 60),))
        d.line([pts[len(pts) // 2], pts[0]], fill=color + (min(255, opacity),), width=max(1, width - 1))
    layer = layer.filter(ImageFilter.GaussianBlur(0.4))
    return Image.alpha_composite(img, layer)


def flower(img, cx, cy, radius, petal_color, center_color, petals=5, opacity=220):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for i in range(petals):
        a = 2 * math.pi * i / petals
        px = cx + math.cos(a) * radius * 0.55
        py = cy + math.sin(a) * radius * 0.55
        d.ellipse([px - radius * 0.46, py - radius * 0.36, px + radius * 0.46, py + radius * 0.36],
                  fill=petal_color + (opacity,))
    d.ellipse([cx - radius * 0.24, cy - radius * 0.24, cx + radius * 0.24, cy + radius * 0.24],
              fill=center_color + (min(255, opacity + 20),))
    layer = layer.filter(ImageFilter.GaussianBlur(0.6))
    return Image.alpha_composite(img, layer)


def panel(img, box, opacity=225, radius=18):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.rounded_rectangle(box, radius=radius, fill=PAPER + (opacity,), outline=GOLD + (90,), width=1)
    layer = layer.filter(ImageFilter.GaussianBlur(0.8))
    return Image.alpha_composite(img, layer)


def rings(img, cx, cy, radius, width=5, color=GOLD, opacity=255):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    left = [cx - radius * 0.92, cy - radius, cx + radius * 0.08, cy + radius]
    right = [cx - radius * 0.08, cy - radius, cx + radius * 0.92, cy + radius]
    d.ellipse(left, outline=color + (opacity,), width=width)
    d.ellipse(right, outline=color + (opacity,), width=width)
    dx, dy = cx + radius * 0.55, cy - radius * 0.94
    d.polygon([(dx, dy - 14), (dx + 13, dy), (dx, dy + 14), (dx - 13, dy)], fill=color + (opacity,))
    return Image.alpha_composite(img, layer)


def tracking(d, xy, text, f, fill, tracking_px=0, anchor="mm"):
    sizes = [d.textlength(ch, font=f) for ch in text]
    total = sum(sizes) + tracking_px * max(0, len(text) - 1)
    x, y = xy
    if anchor[0] == "m":
        x -= total / 2
    elif anchor[0] == "r":
        x -= total
    if anchor[1] == "m":
        pass
    elif anchor[1] == "t":
        y += f.size / 2
    elif anchor[1] == "b":
        y -= f.size / 2
    for ch, w in zip(text, sizes):
        d.text((x, y), ch, font=f, fill=fill, anchor="lm")
        x += w + tracking_px
    return total


def save(img, name, quality=93):
    img.convert("RGB").save(ASSETS / name, quality=quality, optimize=True)
    print("saved", name, img.size)


def hero():
    w, h = 1560, 975
    img = paper((w, h), (253, 249, 242), (242, 231, 213))
    img = watercolor(img, [
        (210, 180, 330, 250, SAGE_LIGHT, 70),
        (1350, 780, 360, 260, BLUSH, 45),
        (1300, 120, 260, 220, GOLD_LIGHT, 55),
        (180, 820, 300, 220, SAGE_LIGHT, 50),
    ], blur=80)
    img = branch(img, (20, 920), (120, 620), (120, 220), (300, -40), GOLD_DARK, 3, SAGE, 1.2, 2)
    img = branch(img, (1530, 60), (1430, 330), (1450, 720), (1280, 1010), GOLD_DARK, 3, SAGE, 1.15, 3)
    img = flower(img, 250, 175, 46, BLUSH, GOLD, 6, 175)
    img = flower(img, 330, 240, 32, (240, 220, 200), GOLD, 5, 160)
    img = flower(img, 1310, 800, 44, BLUSH, GOLD, 6, 170)
    img = flower(img, 1230, 735, 30, (240, 220, 200), GOLD, 5, 155)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([230, 120, w - 230, h - 120], radius=18, outline=GOLD + (150,), width=2)
    d.rounded_rectangle([245, 135, w - 245, h - 135], radius=12, outline=GOLD + (70,), width=1)
    rings(img, w // 2, 315, 52, 4, GOLD, 225)
    f_small = light_font(25)
    tracking(d, (w // 2, 405), "ПРИГЛАШЕНИЕ НА СВАДЬБУ", f_small, MUTED, 8)
    f_mono = font(156, bold=True)
    d.text((w // 2, 555), "А & Е", font=f_mono, fill=GOLD_DARK, anchor="mm")
    f_names = font(52)
    d.text((w // 2, 690), "Александр  и  Екатерина", font=f_names, fill=INK, anchor="mm")
    f_date = font(40, italic=True)
    d.text((w // 2, 770), "12 июня 2027", font=f_date, fill=GOLD_DARK, anchor="mm")
    f_place = light_font(26)
    tracking(d, (w // 2, 830), "ЛЕСНАЯ УСАДЬБА  ·  МОСКВА", f_place, MUTED, 5)
    save(img, "photo-hero.jpg")


def portrait_one():
    w, h = 900, 1200
    img = paper((w, h), (250, 242, 232), (231, 213, 194))
    img = watercolor(img, [
        (180, 220, 260, 300, BLUSH, 60),
        (720, 950, 280, 320, SAGE_LIGHT, 60),
    ], blur=75)
    img = branch(img, (80, 1140), (60, 800), (240, 420), (140, 30), GOLD_DARK, 3, SAGE, 1.0, 5)
    img = branch(img, (830, 1080), (900, 760), (760, 420), (820, 40), GOLD_DARK, 3, SAGE, 1.0, 6)
    rings(img, w // 2, 360, 95, 5, GOLD, 235)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([80, 70, w - 80, h - 70], radius=16, outline=GOLD + (120,), width=2)
    d.text((w // 2, 610), "А & Е", font=font(124, bold=True), fill=GOLD_DARK, anchor="mm")
    d.text((w // 2, 735), "12.06.2027", font=font(52, italic=True), fill=INK, anchor="mm")
    tracking(d, (w // 2, 820), "СОХРАНИТЕ ЭТОТ ДЕНЬ", light_font(23), MUTED, 6)
    img = flower(img, 190, 1010, 46, BLUSH, GOLD, 6, 190)
    img = flower(img, 715, 965, 34, (242, 222, 204), GOLD, 5, 165)
    save(img, "photo-portrait1.jpg")


def portrait_two():
    w, h = 900, 1200
    img = paper((w, h), (249, 246, 236), (225, 234, 219))
    img = watercolor(img, [
        (450, 850, 360, 280, SAGE_LIGHT, 70),
        (180, 250, 240, 250, BLUSH, 45),
    ], blur=80)
    img = branch(img, (450, 1190), (450, 900), (450, 620), (450, 390), GOLD_DARK, 3, SAGE, 1.2, 8)
    img = branch(img, (450, 880), (300, 720), (230, 570), (210, 470), GOLD_DARK, 3, SAGE, 0.9, 9)
    img = branch(img, (450, 820), (610, 680), (680, 560), (700, 450), GOLD_DARK, 3, SAGE, 0.9, 10)
    for cx, cy, r, pc in [
        (450, 470, 66, BLUSH),
        (350, 540, 54, (238, 205, 199)),
        (550, 535, 56, (245, 226, 214)),
        (450, 610, 48, (232, 190, 184)),
        (300, 450, 40, (250, 238, 228)),
        (610, 455, 38, (250, 238, 228)),
    ]:
        img = flower(img, cx, cy, r, pc, GOLD, 6, 215)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([80, 70, w - 80, h - 70], radius=16, outline=GOLD + (120,), width=2)
    img = panel(img, [190, 835, 710, 1095], 228, 18)
    d = ImageDraw.Draw(img)
    d.text((w // 2, 905), "А & Е", font=font(105, bold=True), fill=GOLD_DARK, anchor="mm")
    tracking(d, (w // 2, 1000), "АЛЕКСАНДР И ЕКАТЕРИНА", light_font(23), MUTED, 5)
    d.text((w // 2, 1060), "12 июня 2027", font=font(42, italic=True), fill=GOLD_DARK, anchor="mm")
    save(img, "photo-portrait2.jpg")


def banner_one():
    w, h = 1600, 720
    img = paper((w, h), (252, 248, 240), (227, 235, 219))
    img = watercolor(img, [
        (300, 150, 360, 200, SAGE_LIGHT, 75),
        (1300, 620, 420, 230, SAGE_LIGHT, 70),
        (800, 380, 400, 180, GOLD_LIGHT, 55),
    ], blur=80)
    d = ImageDraw.Draw(img)
    d.arc([560, 120, 1040, 560], 180, 360, fill=GOLD_DARK + (150,), width=3)
    d.line([(560, 340), (560, 620)], fill=GOLD_DARK + (150,), width=3)
    d.line([(1040, 340), (1040, 620)], fill=GOLD_DARK + (150,), width=3)
    img = branch(img, (40, 760), (180, 430), (260, 180), (500, -30), GOLD_DARK, 3, SAGE, 1.1, 12)
    img = branch(img, (1560, 760), (1440, 430), (1360, 180), (1120, -30), GOLD_DARK, 3, SAGE, 1.1, 13)
    img = panel(img, [390, 255, 1210, 585], 234, 20)
    d = ImageDraw.Draw(img)
    tracking(d, (w // 2, 330), "ЦЕРЕМОНИЯ", light_font(30), MUTED, 10)
    d.text((w // 2, 420), "Лесная усадьба", font=font(64, bold=True), fill=GOLD_DARK, anchor="mm")
    d.text((w // 2, 510), "15:30  ·  сбор гостей", font=font(32, italic=True), fill=INK, anchor="mm")
    save(img, "photo-banner1.jpg")


def banner_two():
    w, h = 1600, 720
    img = paper((w, h), (250, 243, 234), (238, 220, 203))
    img = watercolor(img, [
        (200, 620, 380, 220, BLUSH, 55),
        (1400, 120, 400, 230, SAGE_LIGHT, 60),
        (800, 360, 460, 180, GOLD_LIGHT, 50),
    ], blur=80)
    img = branch(img, (0, 250), (420, 90), (900, 120), (1600, 260), GOLD_DARK, 3, SAGE, 1.05, 15)
    img = branch(img, (0, 560), (460, 700), (1080, 640), (1600, 500), GOLD_DARK, 3, SAGE, 1.05, 16)
    for x, y, r, pc in [(330, 190, 48, BLUSH), (520, 150, 34, (242, 222, 204)), (1100, 170, 40, BLUSH),
                        (1270, 230, 30, (242, 222, 204)), (420, 540, 38, BLUSH), (1180, 540, 46, BLUSH)]:
        img = flower(img, x, y, r, pc, GOLD, 6, 210)
    img = panel(img, [300, 255, 1300, 575], 232, 20)
    d = ImageDraw.Draw(img)
    tracking(d, (w // 2, 330), "БАНКЕТ", light_font(30), MUTED, 10)
    d.text((w // 2, 420), "Ужин, танцы и поздравления", font=font(46, bold=True), fill=GOLD_DARK, anchor="mm")
    d.text((w // 2, 505), "18:00", font=font(42, italic=True), fill=INK, anchor="mm")
    save(img, "photo-banner2.jpg")


def strip():
    w, h = 1500, 1000
    img = paper((w, h), (252, 248, 240), (232, 238, 224))
    img = watercolor(img, [
        (750, 520, 500, 330, SAGE_LIGHT, 55),
        (300, 180, 300, 220, BLUSH, 40),
        (1220, 850, 320, 220, GOLD_LIGHT, 50),
    ], blur=80)
    d = ImageDraw.Draw(img)
    d.arc([390, 220, 1110, 860], 180, 360, fill=GOLD_DARK + (160,), width=4)
    d.line([(390, 540), (390, 860)], fill=GOLD_DARK + (160,), width=4)
    d.line([(1110, 540), (1110, 860)], fill=GOLD_DARK + (160,), width=4)
    for i, x in enumerate(range(430, 1080, 60)):
        d.line([(x, 300 + abs(750 - x) * 0.3), (x, 860)], fill=SAGE + (80,), width=2)
    img = branch(img, (30, 1000), (180, 620), (260, 260), (500, -40), GOLD_DARK, 3, SAGE, 1.2, 18)
    img = branch(img, (1470, 1000), (1320, 620), (1240, 260), (1000, -40), GOLD_DARK, 3, SAGE, 1.2, 19)
    for x, y, r, pc in [(250, 260, 54, BLUSH), (1250, 260, 54, BLUSH), (350, 170, 34, (242, 222, 204)),
                        (1150, 170, 34, (242, 222, 204)), (750, 860, 40, BLUSH)]:
        img = flower(img, x, y, r, pc, GOLD, 6, 210)
    img = panel(img, [330, 380, 1170, 750], 222, 22)
    d = ImageDraw.Draw(img)
    d.text((w // 2, 475), "А & Е", font=font(145, bold=True), fill=GOLD_DARK, anchor="mm")
    d.text((w // 2, 615), "Александр и Екатерина", font=font(54), fill=INK, anchor="mm")
    tracking(d, (w // 2, 690), "12 ИЮНЯ 2027  ·  МОСКВА", light_font(27), MUTED, 6)
    save(img, "photo-strip.jpg")


def confirm():
    w, h = 900, 900
    img = paper((w, h), (252, 248, 241), (236, 226, 209))
    img = watercolor(img, [
        (450, 450, 320, 300, GOLD_LIGHT, 45),
        (200, 700, 240, 200, SAGE_LIGHT, 45),
    ], blur=75)
    d = ImageDraw.Draw(img)
    d.ellipse([190, 190, 710, 710], outline=GOLD + (150,), width=2)
    d.ellipse([215, 215, 685, 685], outline=GOLD + (70,), width=1)
    img = branch(img, (170, 760), (250, 520), (300, 280), (230, 110), GOLD_DARK, 3, SAGE, 0.9, 21)
    img = branch(img, (730, 760), (650, 520), (600, 280), (670, 110), GOLD_DARK, 3, SAGE, 0.9, 22)
    d = ImageDraw.Draw(img)
    d.ellipse([355, 265, 545, 455], fill=PAPER + (240,), outline=GOLD + (210,), width=4)
    d.line([(395, 355), (437, 400), (505, 310)], fill=GOLD_DARK + (255,), width=10, joint="curve")
    img = panel(img, [225, 585, 675, 775], 228, 20)
    d = ImageDraw.Draw(img)
    d.text((450, 650), "Спасибо!", font=font(62, bold=True), fill=GOLD_DARK, anchor="mm")
    tracking(d, (450, 725), "БУДЕМ РАДЫ ВАС ВИДЕТЬ", light_font(21), MUTED, 4)
    save(img, "photo-confirm.jpg")


def leaves():
    w, h = 1000, 1600
    img = paper((w, h), (244, 241, 230), (218, 228, 211))
    img = watercolor(img, [
        (250, 250, 280, 320, SAGE_LIGHT, 80),
        (780, 620, 320, 380, SAGE, 65),
        (200, 1200, 300, 320, SAGE_LIGHT, 70),
        (760, 1400, 260, 300, GOLD_LIGHT, 45),
    ], blur=80)
    for i in range(6):
        img = branch(
            img,
            (random.Random(i).uniform(-100, 300), 1700 + i * 30),
            (random.Random(i + 10).uniform(100, 900), 1200),
            (random.Random(i + 20).uniform(0, 900), 500),
            (random.Random(i + 30).uniform(-100, 1000), -100),
            DARK_GREEN if i % 2 == 0 else GOLD_DARK,
            2 + i % 2,
            SAGE if i % 2 == 0 else GOLD,
            1.15,
            i,
            120,
        )
    for i in range(5):
        img = flower(img, 120 + i * 190, 200 + (i % 2) * 110, 34, BLUSH if i % 2 == 0 else (242, 222, 204), GOLD, 6, 120)
    save(img, "leaves.jpg", quality=90)


if __name__ == "__main__":
    random.seed(7)
    hero()
    portrait_one()
    portrait_two()
    banner_one()
    banner_two()
    strip()
    confirm()
    leaves()
