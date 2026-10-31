# 生成系统 ICO 和标题栏缩放资源，并把定稿文件整理到 logo/final/。
# 所有尺寸统一使用光影三列版，避免不同位置的图标外观不一致。
# 运行顺序：fallery_logo.py → fluent.py → 本脚本
#   uv run --with resvg-py --with pillow python build_ico.py
import io
import shutil
import struct
from pathlib import Path

import resvg_py
from PIL import Image

HERE = Path(__file__).parent
OUT = HERE / "out"
FINAL = HERE / "final"
ASSETS = HERE.parent / "Assets"

SIZES = [16, 20, 24, 32, 40, 48, 64, 256]


def render_png(svg_path, px):
    data = bytes(resvg_py.svg_to_bytes(svg_path=str(svg_path), width=px, height=px))
    # 过一遍 Pillow，统一成 RGBA PNG
    buf = io.BytesIO()
    Image.open(io.BytesIO(data)).convert("RGBA").save(buf, "PNG", optimize=True)
    return buf.getvalue()


def write_ico(path, images):
    # ICO = 6 字节头 + 每张 16 字节目录项 + PNG 数据（Vista 起支持 PNG 压缩的条目）
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, payload = b"", b""
    for px, png in images:
        dim = 0 if px >= 256 else px
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(png), offset)
        payload += png
        offset += len(png)
    path.write_bytes(header + entries + payload)


images = [(px, render_png(OUT / "fallery-icon-fluent.svg", px)) for px in SIZES]
ASSETS.mkdir(exist_ok=True)
write_ico(ASSETS / "Fallery.ico", images)

# XAML 图像控件只读取 ICO 第一帧；单独提供缩放资源，避免放大 16px 图像。
for scale in [100, 125, 150, 200, 250, 300, 400]:
    px = 16 * scale // 100
    (ASSETS / f"FalleryTitleBar.scale-{scale}.png").write_bytes(dict(images)[px])

FINAL.mkdir(exist_ok=True)
for name in [
    "fallery-icon-fluent.svg",   # 主图标（光影，三列）
    "fallery-mark.svg",          # 无底板标志，README / 网站 / 单色
    "fallery-icon.svg",          # 扁平三列带底板
    "fallery-icon-fluent-1024.png",
    "fallery-icon-construction.png",
    "fallery-fluent-preview.png",
    "compare.png",
]:
    shutil.copy(OUT / name, FINAL / name)
shutil.copy(ASSETS / "Fallery.ico", FINAL / "Fallery.ico")
print("sizes:", [px for px, _ in images], "->", ASSETS / "Fallery.ico")
