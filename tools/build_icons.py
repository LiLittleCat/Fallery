"""生成 Hugeicons 免费 Stroke Rounded 子集；不要手改生成字体或 Icons.xaml。

用法：uv run --python 3.11 --with fonttools tools/build_icons.py
"""
import hashlib
import pathlib
import re
import urllib.request
from fontTools import subset
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont

ROOT = pathlib.Path(__file__).resolve().parent.parent
CACHE = ROOT / "work" / "hugeicons"
# 固定校验值，避免上游变更后图标名称和字形不匹配。
SOURCES = {
    "hgi-stroke-rounded.ttf": ("https://use.hugeicons.com/font/hgi-stroke-rounded.ttf", "5b6b2fcb68e07374e601e1389ecd294182cf15ea65903d66a8a2484125db060f"),
    "icons.css": ("https://use.hugeicons.com/font/icons.css", "11c954c5cdb07676d462f9a2cda2dbb252fb9f4cf7372c0d0ee02611ff9049a8"),
    "LICENSE": ("https://raw.githubusercontent.com/hugeicons/hugeicons/main/LICENSE", "1658d8213209df7b9b86dfc05d724ede48d00dbc27abc15976ec7adec9601cde"),
}

# 资源键与 Hugeicons 功能名称；Filled 旧键仅保留兼容。
ICONS = [
    ("IconSidebar", "sidebar-left"),
    ("IconBack", "arrow-left-02"),
    ("IconForward", "arrow-right-02"),
    ("IconUp", "arrow-up-02"),
    ("IconRefresh", "refresh"),
    ("IconOpenFolder", "folder-open"),
    ("IconNewFolder", "folder-add"),
    ("IconFolder", "folder-01"),
    ("IconFolderFilled", "folder-01"),
    ("IconFolderHidden", "folder-block"),
    ("IconDrive", "hard-drive"),
    ("IconPictures", "image-02"),
    ("IconDesktop", "computer"),
    ("IconSort", "arrow-up-down"),
    ("IconSortTaken", "camera-01"),
    ("IconSortCreated", "calendar-add-01"),
    ("IconSortModified", "calendar-clock"),
    ("IconSortName", "sorting-a-z-01"),
    ("IconSortSize", "sorting-02"),
    ("IconAscending", "arrow-up-02"),
    ("IconDescending", "arrow-down-02"),
    ("IconFilter", "filter"),
    ("IconAllMedia", "image-play"),
    ("IconImage", "image-01"),
    ("IconVideo", "video-01"),
    ("IconAudio", "music-note-01"),
    ("IconHiddenItems", "view"),
    ("IconView", "dashboard-square-01"),
    ("IconJustified", "layout-07"),
    ("IconMasonry", "dashboard-square-03"),
    ("IconLargeIcons", "dashboard-square-01"),
    ("IconFilmstrip", "fallery-gallery"),
    ("IconFileNames", "text-font"),
    ("IconCards", "square"),
    ("IconFullScreen", "full-screen"),
    ("IconExitFullScreen", "arrow-shrink"),
    ("IconSettings", "settings-01"),
    ("IconChevronRight", "arrow-right-01"),
    ("IconPrevious", "arrow-left-01"),
    ("IconNext", "arrow-right-01"),
    ("IconPlay", "play"),
    ("IconOpen", "link-square-02"),
    ("IconExpand", "arrow-down-01"),
    ("IconCollapse", "arrow-up-01"),
    ("IconCopy", "copy-01"),
    ("IconSearch", "search-01"),
    ("IconPaste", "clipboard-paste"),
    ("IconCopyFile", "files-01"),
    ("IconTheme", "sun-moon"),
    ("IconPlayMedia", "play"),
    ("IconPauseMedia", "pause"),
    ("IconPreviousMedia", "previous"),
    ("IconNextMedia", "next"),
    ("IconVolume", "volume-high"),
    ("IconMute", "volume-mute-02"),
    ("IconPlaylist", "list"),
    ("IconPanelRight", "sidebar-right"),
    ("IconDelete", "delete-02"),
    ("IconHistory", "history"),
    ("IconBookmark", "bookmark-02"),
    ("IconBookmarkFilled", "bookmark-check-02"),
    ("IconMore", "more-horizontal"),
    ("IconScreenshot", "camera-01"),
    ("IconLoop", "repeat"),
    ("IconInfo", "information-circle"),
    ("IconZoomIn", "zoom-in"),
    ("IconZoomOut", "zoom-out"),
    ("IconFit", "fit-to-screen"),
    ("IconRotate", "image-rotation-clockwise"),
    ("IconRotateLeft", "rotate-ccw"),
    ("IconRotateRight", "rotate-cw"),
    ("IconFlipHorizontal", "image-flip-horizontal"),
    ("IconResolution", "resize-field-rectangle"),
    ("IconFileSize", "file-empty-01"),
]


def fetch(name: str) -> pathlib.Path:
    CACHE.mkdir(parents=True, exist_ok=True)
    target = CACHE / name
    url, expected = SOURCES[name]
    if target.exists():
        data = target.read_bytes()
    else:
        with urllib.request.urlopen(url, timeout=30) as response:
            data = response.read()
    if hashlib.sha256(data).hexdigest() != expected:
        raise ValueError(f"{name} 校验不匹配；请先核对上游版本和图标映射。")
    if not target.exists():
        target.write_bytes(data)
    return target


def add_gallery_glyph(font: TTFont, codes: dict[str, int]) -> None:
    # 保留大图＋缩略图的矩形和三点语义，采用同系列字形组合。
    glyph_set, cmap = font.getGlyphSet(), font.getBestCmap()
    pen = TTGlyphPen(glyph_set)
    for source, transform in (
        ("rectangle-horizontal", (1, 0, 0, 0.9, 0, 125)),
        ("more-horizontal", (0.6, 0, 0, 0.6, 200, -175)),
    ):
        glyph_set[cmap[codes[source]]].draw(TransformPen(pen, transform))
    name, code = "fallery_gallery", 0xE000
    font["glyf"][name] = pen.glyph()
    font["hmtx"][name] = (1000, 0)
    if name not in font.getGlyphOrder():
        font.setGlyphOrder([*font.getGlyphOrder(), name])
    for table in font["cmap"].tables:
        if table.isUnicode():
            table.cmap[code] = name
    codes["fallery-gallery"] = code


def main() -> None:
    codes = {name: int(code, 16) for name, code in re.findall(
        r'\.hgi-stroke\.hgi-([\w-]+)::before\s*\{\s*content:\s*"\\([0-9a-fA-F]+)"',
        fetch("icons.css").read_text(encoding="utf-8"))}
    font = TTFont(fetch("hgi-stroke-rounded.ttf"))
    add_gallery_glyph(font, codes)
    picked = {codes[name] for _, name in ICONS}
    options = subset.Options()
    options.layout_features = []
    options.name_IDs = ["*"]
    options.notdef_outline = True
    subsetter = subset.Subsetter(options)
    subsetter.populate(unicodes=sorted(picked))
    subsetter.subset(font)
    assert picked <= font.getBestCmap().keys(), "缺少图标字形"
    fonts_dir = ROOT / "Assets" / "Fonts"
    fonts_dir.mkdir(parents=True, exist_ok=True)
    output = fonts_dir / "FalleryIcons-Hugeicons.ttf"
    font.save(output)
    family = font["name"].getDebugName(1)
    (fonts_dir / "LICENSE-Hugeicons.txt").write_bytes(fetch("LICENSE").read_bytes())
    lines = [
        '<?xml version="1.0" encoding="utf-8"?>',
        '<!-- 由 tools/build_icons.py 生成，Hugeicons Stroke Rounded；旧资源名保留兼容。 -->',
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
    ]
    for key in ("FluentIconFont", "FluentIconFilledFont"):
        lines.append(f'    <FontFamily x:Key="{key}">ms-appx:///Assets/Fonts/{output.name}#{family}</FontFamily>')
    lines += [
        '    <Style x:Key="FluentIcon" TargetType="FontIcon">',
        '        <Setter Property="FontFamily" Value="{StaticResource FluentIconFont}" />',
        '        <Setter Property="FontSize" Value="16" />',
        '    </Style>',
        '    <Style x:Key="FluentIconFilled" TargetType="FontIcon" BasedOn="{StaticResource FluentIcon}" />',
    ]
    for key, name in ICONS:
        lines.append(f'    <x:String x:Key="{key}">&#x{codes[name]:X};</x:String> <!-- {name} -->')
    lines.append('</ResourceDictionary>')
    (ROOT / "Themes" / "Icons.xaml").write_text("\n".join(lines) + "\n", encoding="utf-8")
    # 精确移除旧生成文件，避免继续分发不再使用的字体。
    for name in ("FalleryIcons-Regular.ttf", "FalleryIcons-Filled.ttf", "LICENSE-FluentSystemIcons.txt"):
        (fonts_dir / name).unlink(missing_ok=True)
    print(f"Generated {len(ICONS)} resources, {len(picked)} glyphs, {output.stat().st_size} bytes")


if __name__ == "__main__":
    main()
