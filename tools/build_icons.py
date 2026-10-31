"""从 Fluent UI System Icons 抽取 Fallery 用到的图标，生成精简字体和 Themes/Icons.xaml。

字体随程序分发，Win10 / Win11 显示一致；系统的 Segoe Fluent Icons 不允许再分发。
用法（需要 uv）：uv run --with fonttools tools/build_icons.py
新增图标时在 ICONS 中加一行后重新运行，不要手动编辑生成的文件。
"""

import json
import pathlib
import urllib.request

from fontTools import subset
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont

# 固定提交，保证重新生成时字形不变。
COMMIT = "9eb184e96268d3293462f9d9d19225b4c7b07774"
BASE = f"https://raw.githubusercontent.com/microsoft/fluentui-system-icons/{COMMIT}/fonts/"
ROOT = pathlib.Path(__file__).resolve().parent.parent
CACHE = ROOT / "work" / "fluent-icons"

# 资源键, Fluent 图标名（不含 ic_fluent_ 前缀和样式后缀）, 样式
ICONS = [
    ("IconSidebar", "panel_left_20", "regular"),
    ("IconBack", "arrow_left_20", "regular"),
    ("IconForward", "arrow_right_20", "regular"),
    ("IconUp", "arrow_up_20", "regular"),
    ("IconRefresh", "arrow_clockwise_20", "regular"),
    ("IconOpenFolder", "folder_open_20", "regular"),
    ("IconFolder", "folder_20", "regular"),
    ("IconFolderFilled", "folder_20", "filled"),
    ("IconFolderHidden", "folder_prohibited_20", "regular"),
    ("IconDrive", "hard_drive_20", "regular"),
    ("IconPictures", "image_multiple_20", "regular"),
    ("IconSort", "arrow_sort_20", "regular"),
    ("IconSortTaken", "camera_20", "regular"),
    ("IconSortCreated", "calendar_add_20", "regular"),
    ("IconSortModified", "calendar_edit_20", "regular"),
    ("IconSortName", "text_sort_ascending_20", "regular"),
    ("IconSortSize", "data_histogram_20", "regular"),
    ("IconAscending", "arrow_sort_up_20", "regular"),
    ("IconDescending", "arrow_sort_down_20", "regular"),
    ("IconFilter", "filter_20", "regular"),
    ("IconAllMedia", "image_multiple_20", "regular"),
    ("IconImage", "image_20", "regular"),
    ("IconVideo", "video_20", "regular"),
    ("IconHiddenItems", "eye_20", "regular"),
    ("IconView", "grid_20", "regular"),
    ("IconJustified", "layout_row_two_split_top_20", "regular"),
    ("IconMasonry", "board_20", "regular"),
    ("IconLargeIcons", "layout_cell_four_20", "regular"),
    ("IconFilmstrip", "fallery_gallery_20", "regular"),
    ("IconFileNames", "text_case_title_20", "regular"),
    ("IconCards", "card_ui_20", "regular"),
    ("IconFullScreen", "full_screen_maximize_20", "regular"),
    ("IconExitFullScreen", "full_screen_minimize_20", "regular"),
    ("IconSettings", "settings_20", "regular"),
    ("IconChevronRight", "chevron_right_12", "regular"),
    ("IconPrevious", "chevron_left_24", "regular"),
    ("IconNext", "chevron_right_24", "regular"),
    ("IconPlay", "play_16", "filled"),
    ("IconOpen", "open_20", "regular"),
    ("IconExpand", "chevron_down_20", "regular"),
    ("IconCollapse", "chevron_up_20", "regular"),
    ("IconCopy", "copy_20", "regular"),
    ("IconCopyFile", "document_copy_20", "regular"),
    ("IconTheme", "dark_theme_20", "regular"),
    ("IconPlayMedia", "play_20", "filled"),
    ("IconPauseMedia", "pause_20", "filled"),
    ("IconPreviousMedia", "previous_20", "filled"),
    ("IconNextMedia", "next_20", "filled"),
    ("IconVolume", "speaker_2_20", "regular"),
    ("IconMute", "speaker_mute_20", "regular"),
    ("IconPlaylist", "text_bullet_list_ltr_20", "regular"),
    ("IconPanelRight", "panel_right_20", "regular"),
    ("IconDelete", "delete_20", "regular"),
    ("IconHistory", "history_20", "regular"),
    ("IconBookmark", "bookmark_20", "regular"),
    ("IconBookmarkFilled", "bookmark_20", "filled"),
    ("IconMore", "more_horizontal_20", "regular"),
    ("IconScreenshot", "screenshot_20", "regular"),
    ("IconLoop", "arrow_repeat_all_20", "regular"),
    ("IconInfo", "info_20", "regular"),
    ("IconZoomIn", "zoom_in_20", "regular"),
    ("IconZoomOut", "zoom_out_20", "regular"),
    ("IconFit", "scale_fit_20", "regular"),
    ("IconRotate", "rotate_right_20", "regular"),
    ("IconResolution", "resize_image_20", "regular"),
    ("IconFileSize", "document_20", "regular"),
]


def fetch(name: str, url: str | None = None) -> pathlib.Path:
    CACHE.mkdir(parents=True, exist_ok=True)
    target = CACHE / name
    if not target.exists():
        urllib.request.urlretrieve(url or BASE + name, target)
    return target


def add_gallery_glyph(font: TTFont, codes: dict[str, int]) -> None:
    """上方大图与下方缩略图没有完全匹配的上游字形，组合两个 Fluent 图标保持视觉一致。"""
    glyph_set = font.getGlyphSet()
    cmap = font.getBestCmap()
    pen = TTGlyphPen(glyph_set)
    for source, transform in (
        ("rectangle_landscape", (1, 0, 0, 0.9, 0, 90)),
        ("more_horizontal", (0.8, 0, 0, 0.8, 50, -125)),
    ):
        glyph = cmap[codes[f"ic_fluent_{source}_20_regular"]]
        glyph_set[glyph].draw(TransformPen(pen, transform))
    name, code = "fallery_gallery", 0xE000
    font["glyf"][name] = pen.glyph()
    font["hmtx"][name] = (500, 50)
    if name not in font.getGlyphOrder():
        font.setGlyphOrder([*font.getGlyphOrder(), name])
    for table in font["cmap"].tables:
        if table.isUnicode():
            table.cmap[code] = name
    codes["ic_fluent_fallery_gallery_20_regular"] = code


def center_glyphs(font: TTFont) -> None:
    """上游字体的字形都贴在占位框左缘，右侧留空；FontIcon 按占位宽度居中，图标会整体偏左。"""
    glyf, hmtx = font["glyf"], font["hmtx"]
    glyph_set = font.getGlyphSet()
    centered = {}
    # 复合字形会共享组件，逐个原地平移会互相叠加；先全部按原字体展平重绘，再统一替换。
    for name in set(font.getBestCmap().values()):
        pen = BoundsPen(glyph_set)
        glyph_set[name].draw(pen)
        if pen.bounds is None:
            continue
        advance, _ = hmtx[name]
        # 用实际绘制的外沿居中；控制点包围盒会被曲线手柄撑大。
        shift = round((advance - (pen.bounds[0] + pen.bounds[2])) / 2)
        out = TTGlyphPen(glyph_set)
        glyph_set[name].draw(TransformPen(out, (1, 0, 0, 1, shift, 0)))
        centered[name] = (out.glyph(), advance)
    for name, (glyph, advance) in centered.items():
        glyph.recalcBounds(glyf)
        glyf[name] = glyph
        hmtx[name] = (advance, glyph.xMin)


def main() -> None:
    fonts_dir = ROOT / "Assets" / "Fonts"
    fonts_dir.mkdir(parents=True, exist_ok=True)
    resources = []
    families = {}
    for style in ("regular", "filled"):
        title = style.capitalize()
        source = fetch(f"FluentSystemIcons-{title}.ttf")
        codes = json.loads(fetch(f"FluentSystemIcons-{title}.json").read_text(encoding="utf-8"))
        font = TTFont(source)
        if style == "regular":
            add_gallery_glyph(font, codes)
        picked = []
        for key, name, icon_style in ICONS:
            if icon_style != style:
                continue
            code = codes[f"ic_fluent_{name}_{style}"]
            picked.append(code)
            resources.append((key, code))
        output = fonts_dir / f"FalleryIcons-{title}.ttf"
        options = subset.Options()
        options.layout_features = []
        options.name_IDs = ["*"]
        options.notdef_outline = True
        subsetter = subset.Subsetter(options)
        subsetter.populate(unicodes=sorted(set(picked)))
        subsetter.subset(font)
        center_glyphs(font)
        font.save(output)
        families[style] = TTFont(output)["name"].getDebugName(1)
    license = fetch("LICENSE", f"https://raw.githubusercontent.com/microsoft/fluentui-system-icons/{COMMIT}/LICENSE")
    (fonts_dir / "LICENSE-FluentSystemIcons.txt").write_bytes(license.read_bytes())

    lines = [
        '<?xml version="1.0" encoding="utf-8"?>',
        "<!-- 由 tools/build_icons.py 生成，不要手动编辑。 -->",
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        f'    <FontFamily x:Key="FluentIconFont">ms-appx:///Assets/Fonts/FalleryIcons-Regular.ttf#{families["regular"]}</FontFamily>',
        f'    <FontFamily x:Key="FluentIconFilledFont">ms-appx:///Assets/Fonts/FalleryIcons-Filled.ttf#{families["filled"]}</FontFamily>',
        '    <Style x:Key="FluentIcon" TargetType="FontIcon">',
        '        <Setter Property="FontFamily" Value="{StaticResource FluentIconFont}" />',
        '        <Setter Property="FontSize" Value="16" />',
        "    </Style>",
        '    <Style x:Key="FluentIconFilled" TargetType="FontIcon" BasedOn="{StaticResource FluentIcon}">',
        '        <Setter Property="FontFamily" Value="{StaticResource FluentIconFilledFont}" />',
        "    </Style>",
    ]
    lines += [f'    <x:String x:Key="{key}">&#x{code:X};</x:String>' for key, code in resources]
    lines.append("</ResourceDictionary>")
    (ROOT / "Themes").mkdir(exist_ok=True)
    (ROOT / "Themes" / "Icons.xaml").write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
