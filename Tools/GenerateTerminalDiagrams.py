from pathlib import Path
import os

import ezdxf
from ezdxf.addons import odafc
from ezdxf.addons.drawing import Frontend, RenderContext
from ezdxf.addons.drawing import matplotlib as ezdxf_matplotlib
from ezdxf.addons.drawing.config import Configuration
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt


ROOT = Path(__file__).resolve().parents[1]
SOURCE_DIR = ROOT / "manual" / "REX615 Terminal diagrams"
if not SOURCE_DIR.exists():
    SOURCE_DIR = ROOT / "REX615 Terminal diagrams"
DXF_DIR = ROOT / "Generated" / "TerminalDiagramsDxf"
PNG_DIR = ROOT / "AbbRelaysOfflineConfigurator" / "Data" / "TerminalDiagrams"


def configure_oda() -> None:
    configured = os.environ.get("ODA_FILE_CONVERTER")
    if configured:
        ezdxf.options.set("odafc-addon", "win_exec_path", configured)
        return

    local = Path.home() / "AppData" / "Local" / "Programs" / "ODA" / "ODAFileConverter 27.1.0" / "ODAFileConverter.exe"
    if local.exists():
        ezdxf.options.set("odafc-addon", "win_exec_path", str(local))


def render_one(dwg: Path) -> tuple[str, int, int]:
    dxf = DXF_DIR / f"{dwg.stem}.dxf"
    png = PNG_DIR / f"{dwg.stem}.png"
    odafc.convert(dwg, dxf, version="R2018", replace=True)

    doc = ezdxf.readfile(dxf)
    fig = plt.figure(figsize=(12, 8), dpi=220)
    ax = fig.add_axes([0, 0, 1, 1])
    ax.set_facecolor("white")

    ctx = RenderContext(doc)
    backend = ezdxf_matplotlib.MatplotlibBackend(ax)
    Frontend(ctx, backend, config=Configuration(background_policy="white")).draw_layout(
        doc.modelspace(), finalize=True
    )
    ax.set_aspect("equal", adjustable="datalim")
    ax.autoscale(True)
    ax.axis("off")
    fig.savefig(png, dpi=220, facecolor="white", bbox_inches="tight", pad_inches=0.04)
    plt.close(fig)
    return dwg.name, dxf.stat().st_size, png.stat().st_size


def main() -> None:
    configure_oda()
    if not odafc.is_installed():
        raise RuntimeError("ODA File Converter is not installed or ODA_FILE_CONVERTER is not configured.")

    DXF_DIR.mkdir(parents=True, exist_ok=True)
    PNG_DIR.mkdir(parents=True, exist_ok=True)

    for dwg in sorted(SOURCE_DIR.glob("*.dwg")):
        name, dxf_size, png_size = render_one(dwg)
        print(f"{name}\tDXF={dxf_size}\tPNG={png_size}")


if __name__ == "__main__":
    main()
