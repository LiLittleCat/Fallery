# 给 Fallery 图标加 Fluent 风格的光影：光从左上方来。
# 几何来自 fallery_logo.py 导出的 out/layers.json，这里只负责材质。
# 运行：uv run --with resvg-py --with pillow python fluent.py
import io
import json

import resvg_py
from PIL import Image, ImageDraw

LAYERS = json.load(open("out/layers.json"))
S = LAYERS["size"]

DEFS = f"""
<defs>
  <!-- 底板：左上亮、右下暗的蓝 -->
  <linearGradient id="plate" x1="0" y1="0" x2="1" y2="1">
    <stop offset="0" stop-color="#3D9BEB"/>
    <stop offset="0.55" stop-color="#1172CC"/>
    <stop offset="1" stop-color="#0A4E9A"/>
  </linearGradient>
  <!-- 左上角的环境高光 -->
  <radialGradient id="sheen" cx="0.22" cy="0.12" r="0.8">
    <stop offset="0" stop-color="#FFFFFF" stop-opacity="0.30"/>
    <stop offset="1" stop-color="#FFFFFF" stop-opacity="0"/>
  </radialGradient>
  <!-- 底板内描边：上沿受光，下沿压暗 -->
  <linearGradient id="rim" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#FFFFFF" stop-opacity="0.55"/>
    <stop offset="0.3" stop-color="#FFFFFF" stop-opacity="0"/>
    <stop offset="0.75" stop-color="#021A36" stop-opacity="0"/>
    <stop offset="1" stop-color="#021A36" stop-opacity="0.35"/>
  </linearGradient>
  <!-- 卡片：近白，越往右下越带一点蓝 -->
  <linearGradient id="card" x1="0" y1="0" x2="0.55" y2="1">
    <stop offset="0" stop-color="#FFFFFF"/>
    <stop offset="0.5" stop-color="#F3F8FE"/>
    <stop offset="1" stop-color="#D5E7FA"/>
  </linearGradient>
  <!-- 卡片上沿的一线反光（描边裁到卡片内部，避免外侧出现白边） -->
  <linearGradient id="cardRim" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#FFFFFF" stop-opacity="1"/>
    <stop offset="0.15" stop-color="#FFFFFF" stop-opacity="0"/>
  </linearGradient>
  <!-- 播放三角：比底板更亮的青蓝，作为视觉焦点 -->
  <linearGradient id="play" x1="0" y1="0" x2="1" y2="1">
    <stop offset="0" stop-color="#5FD0FF"/>
    <stop offset="1" stop-color="#0F6CBD"/>
  </linearGradient>
  <!-- 两层投影：贴地的硬影 + 散开的软影 -->
  <filter id="lift" x="-20%" y="-20%" width="140%" height="150%">
    <feDropShadow dx="0" dy="4" stdDeviation="4" flood-color="#03264D" flood-opacity="0.30"/>
    <feDropShadow dx="0" dy="16" stdDeviation="18" flood-color="#03264D" flood-opacity="0.35"/>
  </filter>
</defs>
"""


def compose(layer):
    plate, cards, play = layer["plate"], layer["cards"], layer["play"]
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {S} {S}" width="{S}" height="{S}">
{DEFS}
  <clipPath id="clip"><path d="{plate}"/></clipPath>
  <clipPath id="cardClip"><path d="{cards}"/></clipPath>
  <path d="{plate}" fill="url(#plate)"/>
  <path d="{plate}" fill="url(#sheen)"/>
  <g clip-path="url(#clip)">
    <path d="{cards}" fill="url(#card)" filter="url(#lift)"/>
    <path d="{cards}" fill="none" stroke="url(#cardRim)" stroke-width="8" clip-path="url(#cardClip)"/>
    <path d="{play}" fill="url(#play)"/>
  </g>
  <path d="{plate}" fill="none" stroke="url(#rim)" stroke-width="10" clip-path="url(#clip)"/>
</svg>
"""


def render(svg, px):
    return Image.open(io.BytesIO(bytes(resvg_py.svg_to_bytes(svg_string=svg, width=px, height=px)))).convert("RGBA")


icon_svg = compose(LAYERS["icon"])
small_svg = compose(LAYERS["small"])
open("out/fallery-icon-fluent.svg", "w").write(icon_svg)
open("out/fallery-small-fluent.svg", "w").write(small_svg)

# 预览：浅色和深色背景下的大图 + 各尺寸（≤24px 用小尺寸版）
sizes = [256, 64, 48, 32, 24, 16]
sheet = Image.new("RGBA", (1600, 860), "#F3F3F3")
dark = Image.new("RGBA", (800, 560), "#202020")
sheet.paste(dark, (800, 0))
big = render(icon_svg, 480)
sheet.alpha_composite(big, (160, 40))
sheet.alpha_composite(big, (960, 40))
draw = ImageDraw.Draw(sheet)
x = 40
for px in sizes:
    img = render(small_svg if px <= 24 else icon_svg, px)
    sheet.alpha_composite(img, (x, 600 + (256 - px) // 2))
    draw.text((x, 840), f"{px}px", fill="#666")
    x += px + 60
# 深色任务栏上的小尺寸
x = 1100
for px in [32, 24, 16]:
    img = render(small_svg if px <= 24 else icon_svg, px)
    dark_strip = Image.new("RGBA", (px + 24, px + 24), "#202020")
    sheet.alpha_composite(dark_strip, (x - 12, 720 - 12))
    sheet.alpha_composite(img, (x, 720))
    x += px + 50
sheet.convert("RGB").save("out/fallery-fluent-preview.png")

for px in [1024, 256]:
    render(icon_svg, px).save(f"out/fallery-icon-fluent-{px}.png")
print("ok")
