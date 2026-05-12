from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
OUTPUT_DIR = ROOT / "Rex615OfflineConfigurator" / "Data" / "TerminalDiagrams"

PCL1_SOURCE = "ABB REX615 技术手册 / PCL1 / 后部通讯模块"
PCL2_SOURCE = "ABB REX615 技术手册 / PCL2 / 后部通讯模块"


@dataclass(frozen=True)
class Port:
    kind: str
    y: int
    label: str
    note: str = ""


@dataclass(frozen=True)
class ModuleColumn:
    codes: tuple[str, ...]
    abb_codes: tuple[str, ...]
    short_text: str
    ports: tuple[Port, ...]
    led_y: int = 610
    source: str = PCL1_SOURCE
    source_image: str = ""
    highlight_note: str = ""


@dataclass(frozen=True)
class FigureGroup:
    title: str
    subtitle: str
    columns: tuple[ModuleColumn, ...]
    width: int
    source: str


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        "C:/Windows/Fonts/msyhbd.ttc" if bold else "C:/Windows/Fonts/msyh.ttc",
        "C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf",
        "C:/Windows/Fonts/segoeuib.ttf" if bold else "C:/Windows/Fonts/segoeui.ttf",
    ]
    for candidate in candidates:
        path = Path(candidate)
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


TITLE = font(30, True)
SUBTITLE = font(18)
BODY = font(17)
BODY_BOLD = font(17, True)
SMALL = font(13)
SMALL_BOLD = font(13, True)
TINY = font(11)
TINY_BOLD = font(11, True)


def text_size(draw: ImageDraw.ImageDraw, text: str, fnt: ImageFont.ImageFont) -> tuple[int, int]:
    box = draw.textbbox((0, 0), text, font=fnt)
    return box[2] - box[0], box[3] - box[1]


def draw_text(
    draw: ImageDraw.ImageDraw,
    xy: tuple[int, int],
    text: str,
    fnt: ImageFont.ImageFont,
    fill: str = "#111827",
    max_width: int | None = None,
    line_gap: int = 5,
) -> int:
    x, y = xy
    lines: list[str] = []
    if max_width is None:
        lines = text.splitlines()
    else:
        for raw_line in text.splitlines():
            if " " in raw_line:
                parts = raw_line.split(" ")
                separator = " "
            else:
                parts = list(raw_line)
                separator = ""
            current = ""
            for part in parts:
                candidate = part if not current else f"{current}{separator}{part}"
                if text_size(draw, candidate, fnt)[0] <= max_width:
                    current = candidate
                    continue
                if current:
                    lines.append(current)
                    current = ""
                if text_size(draw, part, fnt)[0] <= max_width:
                    current = part
                    continue
                piece = ""
                for char in part:
                    char_candidate = f"{piece}{char}"
                    if text_size(draw, char_candidate, fnt)[0] <= max_width:
                        piece = char_candidate
                    else:
                        if piece:
                            lines.append(piece)
                        piece = char
                current = piece
            if current:
                lines.append(current)

    for line in lines:
        draw.text((x, y), line, font=fnt, fill=fill)
        y += text_size(draw, line, fnt)[1] + line_gap
    return y


def draw_centered(
    draw: ImageDraw.ImageDraw,
    rect: tuple[int, int, int, int],
    text: str,
    fnt: ImageFont.ImageFont,
    fill: str = "#111827",
) -> None:
    x1, y1, x2, y2 = rect
    w, h = text_size(draw, text, fnt)
    draw.text((x1 + (x2 - x1 - w) / 2, y1 + (y2 - y1 - h) / 2), text, font=fnt, fill=fill)


def draw_centered_wrapped(
    draw: ImageDraw.ImageDraw,
    rect: tuple[int, int, int, int],
    text: str,
    fnt: ImageFont.ImageFont,
    fill: str = "#111827",
    line_gap: int = 3,
) -> None:
    x1, y1, x2, y2 = rect
    max_width = x2 - x1
    parts = text.replace(" + ", "\n+ ").splitlines()
    lines: list[str] = []
    for part in parts:
        current = ""
        for token in part.split(" "):
            candidate = token if not current else f"{current} {token}"
            if text_size(draw, candidate, fnt)[0] <= max_width:
                current = candidate
            else:
                if current:
                    lines.append(current)
                current = token
        if current:
            lines.append(current)
    line_height = text_size(draw, "Ag", fnt)[1]
    total_height = len(lines) * line_height + max(0, len(lines) - 1) * line_gap
    y = y1 + (y2 - y1 - total_height) / 2
    for line in lines:
        w, _ = text_size(draw, line, fnt)
        draw.text((x1 + (max_width - w) / 2, y), line, font=fnt, fill=fill)
        y += line_height + line_gap


def draw_vertical_text(
    base: Image.Image,
    center: tuple[int, int],
    text: str,
    fnt: ImageFont.ImageFont,
    fill: str = "#111827",
) -> None:
    temp = Image.new("RGBA", (420, 38), (255, 255, 255, 0))
    d = ImageDraw.Draw(temp)
    d.text((0, 7), text, font=fnt, fill=fill)
    box = temp.getbbox()
    if box is None:
        return
    temp = temp.crop(box).rotate(90, expand=True)
    x = int(center[0] - temp.width / 2)
    y = int(center[1] - temp.height / 2)
    base.alpha_composite(temp, (x, y))


def rounded(draw: ImageDraw.ImageDraw, rect: tuple[int, int, int, int], fill: str, outline: str, width: int = 2, radius: int = 4) -> None:
    draw.rounded_rectangle(rect, radius=radius, fill=fill, outline=outline, width=width)


def draw_rj45(draw: ImageDraw.ImageDraw, x: int, y: int, label: str) -> None:
    rounded(draw, (x, y, x + 70, y + 54), "#FFFFFF", "#1F2937", 2)
    rounded(draw, (x + 13, y + 8, x + 57, y + 42), "#F8FAFC", "#1F2937", 2)
    for i in range(8):
        px = x + 17 + i * 5
        draw.line((px, y + 13, px, y + 37), fill="#64748B", width=1)
    draw_centered(draw, (x - 18, y + 58, x + 88, y + 78), label, TINY_BOLD, "#334155")


def draw_lc(draw: ImageDraw.ImageDraw, x: int, y: int, label: str, line_diff: bool = False) -> None:
    rounded(draw, (x, y, x + 76, y + 54), "#FFFFFF", "#1F2937", 2)
    for cx in (25, 51):
        draw.ellipse((x + cx - 13, y + 14, x + cx + 13, y + 40), fill="#F8FAFC", outline="#1F2937", width=2)
        draw.ellipse((x + cx - 5, y + 22, x + cx + 5, y + 32), fill="#1F2937")
    if line_diff:
        draw.rectangle((x + 3, y + 3, x + 23, y + 17), fill="#E0F2FE", outline="#38BDF8", width=1)
        draw.text((x + 6, y + 3), "LD", font=TINY_BOLD, fill="#0369A1")
    draw_centered(draw, (x - 22, y + 58, x + 98, y + 78), label, TINY_BOLD, "#334155")


def draw_terminal(draw: ImageDraw.ImageDraw, x: int, y: int, label: str) -> None:
    rounded(draw, (x, y, x + 48, y + 150), "#FFFFFF", "#1F2937", 2)
    for i in range(9):
        py = y + 13 + i * 14
        draw.rectangle((x + 9, py, x + 39, py + 10), fill="#F8FAFC", outline="#64748B", width=1)
        draw.ellipse((x + 20, py + 3, x + 28, py + 9), fill="#334155")


def draw_st(draw: ImageDraw.ImageDraw, x: int, y: int, label: str) -> None:
    rounded(draw, (x, y, x + 68, y + 54), "#FFFFFF", "#1F2937", 2)
    draw.ellipse((x + 15, y + 8, x + 53, y + 46), fill="#F8FAFC", outline="#1F2937", width=2)
    draw.ellipse((x + 27, y + 20, x + 41, y + 34), fill="#FFFFFF", outline="#1F2937", width=2)


def draw_db9(draw: ImageDraw.ImageDraw, x: int, y: int, label: str) -> None:
    draw.rounded_rectangle((x, y, x + 86, y + 44), radius=18, fill="#FFFFFF", outline="#1F2937", width=2)
    for row, count in enumerate((5, 4)):
        for i in range(count):
            cx = x + 20 + i * 11 + (0 if row == 0 else 5)
            cy = y + 15 + row * 14
            draw.ellipse((cx - 2, cy - 2, cx + 2, cy + 2), fill="#1F2937")
    draw_centered(draw, (x - 22, y + 50, x + 108, y + 70), label, TINY_BOLD, "#334155")


def draw_arc(draw: ImageDraw.ImageDraw, x: int, y: int, label: str) -> None:
    rounded(draw, (x, y, x + 78, y + 112), "#FFFFFF", "#1F2937", 2)
    for i in range(3):
        cy = y + 20 + i * 31
        draw.ellipse((x + 17, cy - 10, x + 37, cy + 10), fill="#F8FAFC", outline="#1F2937", width=2)
        draw.text((x + 49, cy - 8), f"ARC{i + 1}", font=TINY_BOLD, fill="#334155")


def draw_leds(draw: ImageDraw.ImageDraw, x: int, y: int) -> None:
    for i, label in enumerate(("X1", "X2", "X3", "H")):
        cy = y + i * 24
        draw.ellipse((x, cy, x + 10, cy + 10), fill="#FFFFFF", outline="#1F2937", width=2)
        draw.text((x + 17, cy - 3), label, font=TINY_BOLD, fill="#334155")


def draw_port(draw: ImageDraw.ImageDraw, port: Port, module_x: int, module_y: int) -> None:
    center_x = module_x + 64
    y = module_y + port.y
    if port.kind == "rj45":
        draw_rj45(draw, center_x - 35, y, port.label)
    elif port.kind == "lc":
        draw_lc(draw, center_x - 38, y, port.label)
    elif port.kind == "ldlc":
        draw_lc(draw, center_x - 38, y, port.label, line_diff=True)
    elif port.kind == "terminal":
        draw_terminal(draw, center_x - 24, y, port.label)
    elif port.kind == "st":
        draw_st(draw, center_x - 34, y, port.label)
    elif port.kind == "db9":
        draw_db9(draw, center_x - 43, y, port.label)
    elif port.kind == "arc":
        draw_arc(draw, center_x - 39, y, port.label)


def draw_module(base: Image.Image, draw: ImageDraw.ImageDraw, x: int, y: int, column: ModuleColumn, selected: str) -> None:
    module_w = 128
    module_h = 700
    selected_here = selected in column.codes
    fill = "#F8FAFC" if not selected_here else "#EEF6FF"
    border = "#CBD5E1" if not selected_here else "#0EA5E9"
    width = 2 if not selected_here else 4

    draw.rectangle((x, y, x + module_w, y + module_h), fill=fill, outline=border, width=width)
    draw.rectangle((x + 28, y - 40, x + module_w - 28, y + 10), fill="#FFFFFF", outline="#334155", width=2)
    draw.ellipse((x + 45, y - 34, x + 83, y + 4), fill="#FFFFFF", outline="#334155", width=2)
    draw.line((x + 50, y - 15, x + 78, y - 15), fill="#334155", width=2)
    draw.rectangle((x + 28, y + module_h - 10, x + module_w - 28, y + module_h + 40), fill="#FFFFFF", outline="#334155", width=2)
    draw.ellipse((x + 45, y + module_h - 4, x + 83, y + module_h + 34), fill="#FFFFFF", outline="#334155", width=2)
    draw.line((x + 50, y + module_h + 15, x + 78, y + module_h + 15), fill="#334155", width=2)

    draw_vertical_text(base, (x + module_w - 18, y + 92), "HMI/TX", TINY_BOLD, "#334155")
    draw_vertical_text(base, (x + module_w - 18, y + 170), "HMI/RX", TINY_BOLD, "#334155")

    for port in column.ports:
        draw_port(draw, port, x, y)
        if port.note:
            draw_vertical_text(base, (x + module_w - 18, y + port.y + 26), port.note, TINY_BOLD, "#334155")

    draw_leds(draw, x + 18, y + column.led_y)

    label_y = y + module_h + 58
    label_color = "#0369A1" if selected_here else "#111827"
    abb_text = "\n".join(column.abb_codes) if len(column.abb_codes) > 1 else column.abb_codes[0]
    code_text = "\n".join(column.codes) if len(column.codes) > 1 else column.codes[0]
    draw_centered_wrapped(draw, (x - 20, label_y, x + module_w + 20, label_y + 44), abb_text, BODY_BOLD, label_color)
    draw_centered_wrapped(draw, (x - 20, label_y + 46, x + module_w + 20, label_y + 84), code_text, BODY_BOLD if selected_here else BODY, label_color)
    draw_centered_wrapped(draw, (x - 20, label_y + 88, x + module_w + 20, label_y + 154), column.short_text, SMALL_BOLD, "#334155")

    if selected_here:
        draw.rounded_rectangle((x - 12, y - 58, x + module_w + 12, y + module_h + 220), radius=12, outline="#0EA5E9", width=4)
        draw.rounded_rectangle((x - 6, y - 94, x + module_w + 6, y - 64), radius=6, fill="#E0F2FE", outline="#38BDF8", width=1)
        draw_centered(draw, (x - 6, y - 94, x + module_w + 6, y - 64), "当前选中", SMALL_BOLD, "#0369A1")


def draw_legend(draw: ImageDraw.ImageDraw, x: int, y: int, selected_column: ModuleColumn) -> None:
    draw_text(draw, (x, y), "端口说明", BODY_BOLD)
    y += 36
    legend = [
        ("RJ-45", "以太网电口"),
        ("LC", "以太网光口"),
        ("LD LC", "线路差动光口"),
        ("EIA-485", "RS-485 / IRIG-B 串口端子"),
        ("DB9", "RS-232/485 串口"),
        ("ST", "玻璃光纤 ST 串口"),
        ("ARC", "弧光传感器输入"),
    ]
    used_kinds = {p.kind for p in selected_column.ports}
    for name, desc in legend:
        include = (
            (name == "RJ-45" and "rj45" in used_kinds)
            or (name == "LC" and "lc" in used_kinds)
            or (name == "LD LC" and "ldlc" in used_kinds)
            or (name == "EIA-485" and "terminal" in used_kinds)
            or (name == "DB9" and "db9" in used_kinds)
            or (name == "ST" and "st" in used_kinds)
            or (name == "ARC" and "arc" in used_kinds)
        )
        if not include:
            continue
        draw.rounded_rectangle((x, y, x + 470, y + 34), radius=6, fill="#F8FAFC", outline="#E2E8F0", width=1)
        draw.text((x + 14, y + 8), name, font=SMALL_BOLD, fill="#111827")
        draw.text((x + 98, y + 8), desc, font=SMALL, fill="#475569")
        y += 42

    y += 14
    draw_text(draw, (x, y), "重绘说明", BODY_BOLD)
    y += 34
    note = (
        "本图依据 ABB REX615 技术手册中 X000 后部通讯模块图的列位和端口上下顺序重新绘制，"
        "用于离线选型时识别接口配置；未复制、裁剪或嵌入 ABB 原始图纸。"
    )
    y = draw_text(draw, (x, y), note, SMALL, fill="#475569", max_width=470, line_gap=7)
    y += 12
    draw_text(draw, (x, y), f"来源：{selected_column.source}", TINY, fill="#64748B", max_width=470, line_gap=5)
    if selected_column.source_image:
        draw_text(draw, (x, y + 40), f"参考图：{selected_column.source_image}", TINY, fill="#64748B", max_width=470, line_gap=5)


def render_group(group: FigureGroup, selected: str) -> Image.Image:
    image = Image.new("RGBA", (group.width, 1220), "#FFFFFF")
    draw = ImageDraw.Draw(image)

    draw.rectangle((0, 0, group.width - 1, 1219), fill="#FFFFFF", outline="#CBD5E1", width=2)
    draw.rectangle((0, 0, group.width, 108), fill="#F1F5F9")
    selected_column = next(column for column in group.columns if selected in column.codes)
    selected_abb = selected_column.abb_codes[selected_column.codes.index(selected)] if selected in selected_column.codes else selected_column.abb_codes[0]
    draw_text(draw, (34, 22), f"{selected}（{selected_abb}）X000 通讯模块图", TITLE, "#0F172A")
    draw_text(draw, (34, 66), group.subtitle, SUBTITLE, "#475569")

    left = 42
    module_y = 190
    column_gap = 42 if len(group.columns) <= 5 else 30
    module_w = 128
    for index, column in enumerate(group.columns):
        x = left + index * (module_w + column_gap)
        draw_module(image, draw, x, module_y, column, selected)

    legend_x = left + len(group.columns) * (module_w + column_gap) + 28
    draw_legend(draw, legend_x, 175, selected_column)

    footer_y = 1138
    draw.rectangle((0, footer_y, group.width, 1220), fill="#F8FAFC", outline="#E2E8F0", width=1)
    draw_text(
        draw,
        (34, footer_y + 20),
        "端口位置采用源图对应模块的相对上下顺序重绘；同一列出现两个模块编号时，表示 ABB 源图将这两个硬件变体绘在同一机械布局列中。",
        SMALL,
        fill="#475569",
        max_width=group.width - 68,
    )

    return image.convert("RGB")


COMMON_GROUP = FigureGroup(
    title="COM0001、COM0011-COM0014",
    subtitle="PCL1/PCL2 通用后部通讯模块：COM0001、COM0011、COM0012、COM0013、COM0014",
    width=1420,
    source=PCL1_SOURCE,
    columns=(
        ModuleColumn(("COM1",), ("COM0001",), "RJ-45", (Port("rj45", 72, "RJ-45", "100BASE-TX"),), 620, PCL1_SOURCE, "00139533.svg"),
        ModuleColumn(("COM11",), ("COM0011",), "RJ-45 + RS485 + IRIG-B", (Port("rj45", 72, "RJ-45", "100BASE-TX"), Port("terminal", 250, "RS485/IRIG-B", "EIA-485/IRIG-B")), 620, PCL1_SOURCE, "00139533.svg"),
        ModuleColumn(("COM12",), ("COM0012",), "LC + RS485 + IRIG-B", (Port("lc", 72, "LC", "100BASE-FX"), Port("terminal", 250, "RS485/IRIG-B", "EIA-485/IRIG-B")), 620, PCL1_SOURCE, "00139533.svg"),
        ModuleColumn(("COM13",), ("COM0013",), "RJ-45 + RS485 + IRIG-B + ARC", (Port("rj45", 72, "RJ-45", "100BASE-TX"), Port("terminal", 250, "RS485/IRIG-B", "EIA-485/IRIG-B"), Port("arc", 450, "ARC x3", "弧光输入")), 620, PCL1_SOURCE, "00139533.svg"),
        ModuleColumn(("COM14",), ("COM0014",), "LC + RS485 + IRIG-B + ARC", (Port("lc", 72, "LC", "100BASE-FX"), Port("terminal", 250, "RS485/IRIG-B", "EIA-485/IRIG-B"), Port("arc", 450, "ARC x3", "弧光输入")), 620, PCL1_SOURCE, "00139533.svg"),
    ),
)

LINE_DIFF_GROUP = FigureGroup(
    title="COM0008-COM0018",
    subtitle="PCL1/PCL2 通用后部通讯模块：COM0008、COM0009、COM0010、COM0015、COM0016、COM0017、COM0018",
    width=1420,
    source=PCL1_SOURCE,
    columns=(
        ModuleColumn(("COM8", "COM10"), ("COM0008", "COM0010"), "差动 LC + RJ-45 x2 + RS485 + ST", (Port("ldlc", 74, "LD LC", "线路差动"), Port("rj45", 180, "RJ-45 #1", "100BASE-TX"), Port("rj45", 278, "RJ-45 #2", "100BASE-TX"), Port("terminal", 390, "RS485/IRIG-B", "EIA-485/IRIG-B"), Port("st", 585, "ST", "串口光纤")), 620, PCL1_SOURCE, "00135287.svg", "COM8 为 MM，COM10 为 SM"),
        ModuleColumn(("COM9", "COM15"), ("COM0009", "COM0015"), "差动 LC + LC x2 + RS485 + ST", (Port("ldlc", 74, "LD LC", "线路差动"), Port("lc", 180, "LC #1", "100BASE-FX"), Port("lc", 278, "LC #2", "100BASE-FX"), Port("terminal", 390, "RS485/IRIG-B", "EIA-485/IRIG-B"), Port("st", 585, "ST", "串口光纤")), 620, PCL1_SOURCE, "00135287.svg", "COM9 为 MM，COM15 为 SM"),
        ModuleColumn(("COM16",), ("COM0016",), "RJ-45 x3 + RS485 + ST", (Port("rj45", 74, "RJ-45 #1", "100BASE-TX"), Port("rj45", 172, "RJ-45 #2", "100BASE-TX"), Port("rj45", 270, "RJ-45 #3", "100BASE-TX"), Port("terminal", 390, "RS485/IRIG-B", "EIA-485/IRIG-B"), Port("st", 585, "ST", "串口光纤")), 620, PCL1_SOURCE, "00135287.svg"),
        ModuleColumn(("COM17",), ("COM0017",), "RJ-45 x2 + LC + RS485 + ST", (Port("rj45", 74, "RJ-45 #1", "100BASE-TX"), Port("rj45", 172, "RJ-45 #2", "100BASE-TX"), Port("lc", 270, "LC", "100BASE-FX"), Port("terminal", 390, "RS485/IRIG-B", "EIA-485/IRIG-B"), Port("st", 585, "ST", "串口光纤")), 620, PCL1_SOURCE, "00135287.svg"),
        ModuleColumn(("COM18",), ("COM0018",), "LC x3 + RS485 + ST", (Port("lc", 74, "LC #1", "100BASE-FX"), Port("lc", 172, "LC #2", "100BASE-FX"), Port("lc", 270, "LC #3", "100BASE-FX"), Port("terminal", 390, "RS485/IRIG-B", "EIA-485/IRIG-B"), Port("st", 585, "ST", "串口光纤")), 620, PCL1_SOURCE, "00135287.svg"),
    ),
)

PCL1_ETHERNET_GROUP = FigureGroup(
    title="COM0031-COM0037",
    subtitle="PCL1/PCL2 通用后部通讯模块：COM0031、COM0032、COM0033、COM0034、COM0037",
    width=1420,
    source=PCL1_SOURCE,
    columns=(
        ModuleColumn(("COM31",), ("COM0031",), "RJ-45 x3", (Port("rj45", 74, "RJ-45 #1", "100BASE-TX"), Port("rj45", 172, "RJ-45 #2", "100BASE-TX"), Port("rj45", 270, "RJ-45 #3", "100BASE-TX")), 620, PCL1_SOURCE, "00132316.svg"),
        ModuleColumn(("COM32",), ("COM0032",), "LC x2 + RJ-45 + ST + ARC", (Port("lc", 74, "LC #1", "100BASE-FX"), Port("lc", 172, "LC #2", "100BASE-FX"), Port("rj45", 270, "RJ-45", "100BASE-TX"), Port("st", 412, "ST", "串口光纤"), Port("arc", 505, "ARC x3", "弧光输入")), 620, PCL1_SOURCE, "00132316.svg"),
        ModuleColumn(("COM33",), ("COM0033",), "RJ-45 x3 + ST + ARC", (Port("rj45", 74, "RJ-45 #1", "100BASE-TX"), Port("rj45", 172, "RJ-45 #2", "100BASE-TX"), Port("rj45", 270, "RJ-45 #3", "100BASE-TX"), Port("st", 412, "ST", "串口光纤"), Port("arc", 505, "ARC x3", "弧光输入")), 620, PCL1_SOURCE, "00132316.svg"),
        ModuleColumn(("COM34",), ("COM0034",), "LC + RJ-45 x2 + ST + ARC", (Port("lc", 74, "LC", "100BASE-FX"), Port("rj45", 172, "RJ-45 #1", "100BASE-TX"), Port("rj45", 270, "RJ-45 #2", "100BASE-TX"), Port("st", 412, "ST", "串口光纤"), Port("arc", 505, "ARC x3", "弧光输入")), 620, PCL1_SOURCE, "00132316.svg"),
        ModuleColumn(("COM37",), ("COM0037",), "LC x2 + RJ-45", (Port("lc", 74, "LC #1", "100BASE-FX"), Port("lc", 172, "LC #2", "100BASE-FX"), Port("rj45", 270, "RJ-45", "100BASE-TX")), 620, PCL1_SOURCE, "00132316.svg"),
    ),
)

PCL2_COM27_GROUP = FigureGroup(
    title="COM0027、COM0031-COM0037",
    subtitle="PCL2 后部通讯模块：COM0027 及同组以太网模块位置",
    width=1560,
    source=PCL2_SOURCE,
    columns=(
        ModuleColumn(("COM27",), ("COM0027",), "RJ-45 + RS232/485 + RS485 + ST", (Port("rj45", 74, "RJ-45", "100BASE-TX"), Port("db9", 190, "RS232/485", "EIA-232/485"), Port("terminal", 300, "RS485/IRIG-B", "EIA-485/IRIG-B"), Port("st", 545, "ST", "串口光纤")), 620, PCL2_SOURCE, "00234431.svg"),
        ModuleColumn(("COM31",), ("COM0031",), "RJ-45 x3", (Port("rj45", 74, "RJ-45 #1", "100BASE-TX"), Port("rj45", 172, "RJ-45 #2", "100BASE-TX"), Port("rj45", 270, "RJ-45 #3", "100BASE-TX")), 620, PCL2_SOURCE, "00234431.svg"),
        ModuleColumn(("COM32",), ("COM0032",), "LC x2 + RJ-45 + ST + ARC", (Port("lc", 74, "LC #1", "100BASE-FX"), Port("lc", 172, "LC #2", "100BASE-FX"), Port("rj45", 270, "RJ-45", "100BASE-TX"), Port("st", 412, "ST", "串口光纤"), Port("arc", 505, "ARC x3", "弧光输入")), 620, PCL2_SOURCE, "00234431.svg"),
        ModuleColumn(("COM33",), ("COM0033",), "RJ-45 x3 + ST + ARC", (Port("rj45", 74, "RJ-45 #1", "100BASE-TX"), Port("rj45", 172, "RJ-45 #2", "100BASE-TX"), Port("rj45", 270, "RJ-45 #3", "100BASE-TX"), Port("st", 412, "ST", "串口光纤"), Port("arc", 505, "ARC x3", "弧光输入")), 620, PCL2_SOURCE, "00234431.svg"),
        ModuleColumn(("COM34",), ("COM0034",), "LC + RJ-45 x2 + ST + ARC", (Port("lc", 74, "LC", "100BASE-FX"), Port("rj45", 172, "RJ-45 #1", "100BASE-TX"), Port("rj45", 270, "RJ-45 #2", "100BASE-TX"), Port("st", 412, "ST", "串口光纤"), Port("arc", 505, "ARC x3", "弧光输入")), 620, PCL2_SOURCE, "00234431.svg"),
        ModuleColumn(("COM37",), ("COM0037",), "LC x2 + RJ-45", (Port("lc", 74, "LC #1", "100BASE-FX"), Port("lc", 172, "LC #2", "100BASE-FX"), Port("rj45", 270, "RJ-45", "100BASE-TX")), 620, PCL2_SOURCE, "00234431.svg"),
    ),
)


GROUPS: tuple[FigureGroup, ...] = (
    COMMON_GROUP,
    LINE_DIFF_GROUP,
    PCL1_ETHERNET_GROUP,
    PCL2_COM27_GROUP,
)


def find_group(code: str) -> FigureGroup:
    for group in GROUPS:
        if any(code in column.codes for column in group.columns):
            if code in {"COM31", "COM32", "COM33", "COM34", "COM37"}:
                return PCL1_ETHERNET_GROUP
            return group
    raise KeyError(code)


def all_codes(groups: Iterable[FigureGroup]) -> list[str]:
    codes: list[str] = []
    for group in groups:
        for column in group.columns:
            for code in column.codes:
                if code not in codes:
                    codes.append(code)
    return codes


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    for code in all_codes(GROUPS):
        group = find_group(code)
        image = render_group(group, code)
        image.save(OUTPUT_DIR / f"REX615_X000_{code}.png", quality=96)
    print(f"Generated {len(all_codes(GROUPS))} redrawn X000 communication diagrams in {OUTPUT_DIR}")


if __name__ == "__main__":
    main()
