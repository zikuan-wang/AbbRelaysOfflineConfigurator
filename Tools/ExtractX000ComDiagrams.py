from __future__ import annotations

import re
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCE_DIR = ROOT / "Tools" / "SourceX000Diagrams"
RENDER_DIR = ROOT / "Generated" / "SvgRender"
OUTPUT_DIR = ROOT / "AbbRelaysOfflineConfigurator" / "Data" / "TerminalDiagrams"


@dataclass(frozen=True)
class SourceSvg:
    key: str
    file_name: str


@dataclass(frozen=True)
class CropRule:
    source_key: str
    output_codes: tuple[str, ...]
    box: tuple[int, int, int, int]


SOURCES: tuple[SourceSvg, ...] = (
    SourceSvg("pcl1_common", "PCL1_00139533.svg"),
    SourceSvg("pcl1_line_diff", "PCL1_00135287.svg"),
    SourceSvg("pcl1_com31", "PCL1_00132316.svg"),
    SourceSvg("pcl2_common", "PCL2_00139533.svg"),
    SourceSvg("pcl2_line_diff", "PCL2_00135287.svg"),
    SourceSvg("pcl2_com27", "PCL2_00234431.svg"),
)

# Crop boxes are in pixels after 4x SVG rendering. They follow the original
# column order in the uploaded SVG drawings and keep the original bottom labels.
CROPS: tuple[CropRule, ...] = (
    CropRule("pcl1_common", ("COM1",), (0, 0, 245, 1689)),
    CropRule("pcl1_common", ("COM11",), (350, 0, 615, 1689)),
    CropRule("pcl1_common", ("COM12",), (720, 0, 985, 1689)),
    CropRule("pcl1_common", ("COM13",), (1090, 0, 1355, 1689)),
    CropRule("pcl1_common", ("COM14",), (1460, 0, 1712, 1689)),
    CropRule("pcl1_line_diff", ("COM8", "COM10"), (0, 0, 278, 1778)),
    CropRule("pcl1_line_diff", ("COM9", "COM15"), (420, 0, 705, 1778)),
    CropRule("pcl1_line_diff", ("COM16",), (870, 0, 1160, 1778)),
    CropRule("pcl1_line_diff", ("COM17",), (1310, 0, 1600, 1778)),
    CropRule("pcl1_line_diff", ("COM18",), (1755, 0, 2033, 1778)),
    CropRule("pcl1_com31", ("COM31",), (0, 0, 245, 1684)),
    CropRule("pcl1_com31", ("COM32",), (370, 0, 620, 1684)),
    CropRule("pcl1_com31", ("COM33",), (750, 0, 1000, 1684)),
    CropRule("pcl1_com31", ("COM34",), (1120, 0, 1370, 1684)),
    CropRule("pcl1_com31", ("COM37",), (1490, 0, 1728, 1684)),
    CropRule("pcl2_com27", ("COM27",), (0, 0, 180, 1268)),
)


def find_chrome() -> Path:
    candidates = [
        Path(r"C:\Program Files\Google\Chrome\Application\chrome.exe"),
        Path(r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"),
        Path(r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
        Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate

    resolved = shutil.which("chrome") or shutil.which("msedge")
    if resolved:
        return Path(resolved)

    raise FileNotFoundError("未找到 Chrome 或 Edge，无法把 SVG 渲染为 PNG。")


def read_viewbox(svg_path: Path) -> tuple[float, float]:
    text = svg_path.read_text(encoding="utf-8", errors="ignore")
    match = re.search(r'viewBox="([^"]+)"', text)
    if not match:
        raise ValueError(f"{svg_path} 缺少 viewBox。")

    values = [float(value) for value in match.group(1).split()]
    return values[2], values[3]


def render_svg(chrome: Path, source: SourceSvg) -> Path:
    svg_path = SOURCE_DIR / source.file_name
    if not svg_path.exists():
        raise FileNotFoundError(svg_path)

    width, height = read_viewbox(svg_path)
    scale = 4
    render_width = round(width * scale)
    render_height = round(height * scale)

    RENDER_DIR.mkdir(parents=True, exist_ok=True)
    html_path = RENDER_DIR / f"{source.key}.html"
    png_path = RENDER_DIR / f"{source.key}.png"
    html_path.write_text(
        "<!doctype html><html><head><meta charset=\"utf-8\">"
        f"<style>html,body{{margin:0;background:white;width:{render_width}px;height:{render_height}px;overflow:hidden}}"
        f"img{{display:block;width:{render_width}px;height:{render_height}px}}</style></head>"
        f"<body><img src=\"{svg_path.as_uri()}\" /></body></html>",
        encoding="utf-8",
    )

    subprocess.run(
        [
            str(chrome),
            "--headless",
            "--disable-gpu",
            "--hide-scrollbars",
            f"--window-size={render_width},{render_height}",
            f"--screenshot={png_path}",
            str(html_path),
        ],
        check=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )

    return png_path


def add_padding(image: Image.Image, padding: int = 36) -> Image.Image:
    output = Image.new("RGB", (image.width + padding * 2, image.height + padding * 2), "white")
    output.paste(image.convert("RGB"), (padding, padding))
    return output


def main() -> None:
    chrome = find_chrome()
    rendered = {source.key: render_svg(chrome, source) for source in SOURCES}
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    for crop in CROPS:
        source_image = Image.open(rendered[crop.source_key]).convert("RGB")
        column = source_image.crop(crop.box)
        column = add_padding(column)
        for code in crop.output_codes:
            column.save(OUTPUT_DIR / f"REX615_X000_{code}.png", quality=96)

    count = sum(len(crop.output_codes) for crop in CROPS)
    print(f"Extracted {count} X000 communication diagrams into {OUTPUT_DIR}")


if __name__ == "__main__":
    main()
