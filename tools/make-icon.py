from PIL import Image, ImageDraw, ImageFilter
import math, os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "FusionNet.App", "Assets")
os.makedirs(OUT, exist_ok=True)
S = 1024

def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))

def vgrad(size, top, bottom):
    img = Image.new("RGB", (size, size))
    d = ImageDraw.Draw(img)
    for y in range(size):
        d.line([(0, y), (size, y)], fill=lerp(top, bottom, y / (size - 1)))
    return img

# ---- background -------------------------------------------------------------
bg = vgrad(S, (13, 20, 34), (7, 11, 20)).convert("RGBA")
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=232, fill=255)
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
img.paste(bg, (0, 0), mask)

d = ImageDraw.Draw(img)

# subtle grid glow
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
for i in range(7):
    r = 250 + i * 95
    gd.ellipse([S // 2 - r, S // 2 - r, S // 2 + r, S // 2 + r],
               outline=(34, 211, 238, 16 - i * 2), width=3)
glow = glow.filter(ImageFilter.GaussianBlur(6))
img.alpha_composite(glow)

# border
d.rounded_rectangle([14, 14, S - 15, S - 15], radius=222,
                    outline=(30, 58, 95, 255), width=10)
d.rounded_rectangle([34, 34, S - 35, S - 35], radius=206,
                    outline=(34, 211, 238, 26), width=3)

# ---- shield ----------------------------------------------------------------
cx, cy = S // 2, S // 2 + 6
top, bottom = cy - 330, cy + 338
half_w = 300

shield = [
    (cx, top),
    (cx + half_w, top + 92),
    (cx + half_w, cy + 60),
    (cx + 208, cy + 250),
    (cx, bottom),
    (cx - 208, cy + 250),
    (cx - half_w, cy + 60),
    (cx - half_w, top + 92),
]

# glow behind shield
shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(shadow).polygon(shield, fill=(34, 211, 238, 130))
shadow = shadow.filter(ImageFilter.GaussianBlur(60))
img.alpha_composite(shadow)

# shield gradient clipped to polygon
sh_mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(sh_mask).polygon(shield, fill=255)
shield_grad = vgrad(S, (103, 232, 249), (14, 116, 233)).convert("RGBA")
img.paste(shield_grad, (0, 0), sh_mask)
d.polygon(shield, outline=(186, 245, 255, 255), width=8)

# inner shield darker inset
inner = []
for (x, y) in shield:
    inner.append((cx + (x - cx) * 0.82, cy + (y - cy) * 0.82))
in_mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(in_mask).polygon(inner, fill=255)
inner_grad = vgrad(S, (10, 25, 45), (5, 12, 24)).convert("RGBA")
img.paste(inner_grad, (0, 0), in_mask)
d.polygon(inner, outline=(34, 211, 238, 140), width=4)

# ---- lightning bolt --------------------------------------------------------
bolt = [
    (cx + 34, cy - 250),
    (cx - 132, cy + 26),
    (cx - 12, cy + 26),
    (cx - 58, cy + 256),
    (cx + 142, cy - 46),
    (cx + 12, cy - 46),
]
bolt_glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(bolt_glow).polygon(bolt, fill=(34, 211, 238, 200))
bolt_glow = bolt_glow.filter(ImageFilter.GaussianBlur(46))
img.alpha_composite(bolt_glow)
d.polygon(bolt, fill=(240, 253, 255, 255))
d.polygon(bolt, outline=(14, 116, 233, 255), width=5)

# ---- nodes / spark accents -------------------------------------------------
for angle in range(0, 360, 45):
    rad = math.radians(angle)
    x = cx + math.cos(rad) * 392
    y = cy + math.sin(rad) * 392
    rr = 11
    d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=(34, 211, 238, 150))

img = img.resize((512, 512), Image.LANCZOS)
img.save(os.path.join(OUT, "logo.png"))

sizes = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)]
img.save(os.path.join(OUT, "app.ico"), format="ICO", sizes=sizes)
print("written", os.path.join(OUT, "app.ico"))
