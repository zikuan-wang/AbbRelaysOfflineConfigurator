import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "AbbRelaysOfflineConfigurator" / "Data" / "CnLegacySelectionRules.json"
SOURCE_DIRS = {
    "615": ROOT / "RE_615",
    "620": ROOT / "RE_620",
}

DEVICE_ORDER = {
    "REF615": 10,
    "RED615": 20,
    "REM615": 30,
    "REG615": 40,
    "RET615": 50,
    "REU615": 60,
    "REV615": 70,
    "REF620": 80,
    "REM620": 90,
    "RET620": 100,
}

GROUP_NAMES = {
    "Mountings": "装置",
    "Standards": "标准",
    "MainApps": "主要应用",
    "FunctionalApps": "标准配置",
    "Aios": "模拟量输入输出",
    "Bios": "开关量输入输出 / 可选板卡",
    "CommSerials": "串口选项",
    "CommEthernets": "网口选项",
    "CommProtocols": "通信协议",
    "Languages": "语言",
    "FrontPanels": "前面板",
    "Options_1": "选项 1",
    "Options_2": "选项 2",
    "PowerSupplies": "电源",
    "Versions": "版本",
}

GROUP_NAME_OVERRIDES = {
    "615": {
        "Aios": "模拟量输入",
        "Bios": "开关量输入/输出",
        "CommProtocols": "协议选项",
        "Options_1": "选项1",
        "Options_2": "选项2",
    },
    "620": {
        "FunctionalApps": "功能应用",
        "Aios": "模拟量输入/输出",
        "Bios": "可选板卡",
        "CommSerials": "通讯模块（串口）",
        "CommEthernets": "通讯模块（以太网）",
        "Options_1": "选项1",
        "Options_2": "选项2",
        "Versions": "保留位 / 版本",
    },
}

BLOCK_NAMES = {
    "FunctionalApplication": "功能与硬件",
    "Communication": "通信",
    "Software": "软件选项",
    "Language": "语言",
    "HMI": "前面板",
}

SERIES_INFO = {
    "615": {
        "id": "615_CN_5_1",
        "name": "615 CN 5.1",
        "description": "615 系列 CN 5.1 装置订货号选型",
        "sources": ["RE_615 XML 5.1", "615选型指南5.1中文版.pdf"],
    },
    "620": {
        "id": "620_CN_2_1",
        "name": "620 CN 2.1",
        "description": "620 系列 CN 2.1 装置订货号选型",
        "sources": [
            "RE_620 XML 2.1",
            "REF620_pg_757844_CNe.pdf",
            "REM620_pg_757845_CNe.pdf",
            "RET620_pg_757846_CNe.pdf",
        ],
    },
}

BLOCK_POSITIONS = {
    ("615", "FunctionalApplication"): ["4", "5-6", "7-8", "14", "15"],
    ("615", "Communication"): ["1", "9", "10", "11"],
    ("615", "Software"): ["4", "9", "10", "14"],
    ("615", "Language"): ["2", "12"],
    ("615", "HMI"): ["13", "12"],
    ("620", "FunctionalApplication"): ["4", "5-6", "7-8", "14", "15"],
    ("620", "Communication"): ["1", "9", "10", "11"],
    ("620", "Software"): ["1", "9", "10", "14"],
    ("620", "Language"): ["2", "12", "13"],
}

LANGUAGE_CODES = {
    "615": {"Z"},
    "620": {"1", "2"},
}

# The supplied 615 CN 5.1 guide is the authoritative source for page choices,
# ordering, defaults, and the applicability notes printed beside each option.
# Every one of the guide's 7 x 15 device/position groups is listed here so a
# future XML change cannot silently add a customer-visible option.
PDF_615_OPTION_CODES = {
    "REF615": {
        "1": ["H", "1"],
        "2": ["C"],
        "3": ["F"],
        "4": ["C", "D", "J", "N", "Z"],
        "5-6": ["AC", "AD", "FC", "FD", "AE", "AF", "FE", "FF"],
        "7-8": ["AB", "AD", "FE", "AF", "FB", "AG", "FC"],
        "9": ["A", "B", "C", "N"],
        "10": ["A", "B", "C", "D", "E", "F", "G", "H", "N"],
        "11": ["A", "B", "C", "D", "G"],
        # The table lists 2 and Z, but its domestic 5.1 note restricts this
        # CN page to Z.
        "12": ["Z"],
        "13": ["A", "B", "C", "D"],
        "14": ["A", "B", "C", "D", "E", "F", "G", "H", "J", "K", "L", "M", "P", "Q", "N"],
        "15": ["A", "B", "C", "D", "E", "G", "H", "J", "K", "N"],
        "16": ["1", "2"],
        "17-18": ["1G"],
    },
    "RED615": {
        "1": ["H", "1"],
        "2": ["C"],
        "3": ["D"],
        "4": ["C", "D"],
        "5-6": ["AC", "AE", "AF", "FE", "FF"],
        "7-8": ["AD", "AF", "AG"],
        "9": ["A", "B", "N"],
        "10": ["A", "B", "G", "H", "J", "K", "L", "M", "P", "Q"],
        "11": ["A", "B", "C", "D", "G"],
        "12": ["Z"],
        "13": ["A", "B", "C", "D"],
        "14": ["A", "D", "E", "H", "L", "M", "N"],
        "15": ["A", "B", "C", "D", "E", "N"],
        "16": ["1", "2"],
        "17-18": ["1G"],
    },
    "REM615": {
        "1": ["H", "1"],
        "2": ["C"],
        "3": ["M"],
        "4": ["A", "B", "C", "Z"],
        "5-6": ["AC", "AD", "AE", "AF", "AG", "AH", "CA", "CB", "CC", "CD"],
        "7-8": ["AB", "AD", "FE", "AG", "FC", "AH", "AJ", "FD", "FF"],
        "9": ["A", "B", "C", "N"],
        "10": ["A", "B", "C", "D", "E", "F", "G", "H", "N"],
        "11": ["A", "B", "C", "D", "G"],
        "12": ["Z"],
        "13": ["A", "B", "C", "D"],
        "14": ["B", "N"],
        "15": ["N"],
        "16": ["1", "2"],
        "17-18": ["1G"],
    },
    "REG615": {
        "1": ["H", "1"],
        "2": ["C"],
        "3": ["G"],
        "4": ["A", "C", "D"],
        "5-6": ["AE", "AF", "FE", "FF", "BC", "BD", "BE", "BF"],
        "7-8": ["AD", "FE", "AG", "FC", "BA", "FD"],
        "9": ["A", "B", "C", "N"],
        "10": ["A", "B", "C", "D", "E", "F", "G", "H", "N"],
        "11": ["A", "B", "C", "D", "G"],
        "12": ["Z"],
        "13": ["A", "B", "C", "D"],
        "14": ["B", "D", "F", "N"],
        "15": ["N"],
        "16": ["1", "2"],
        "17-18": ["1G"],
    },
    "RET615": {
        "1": ["H", "1"],
        "2": ["C"],
        "3": ["T"],
        "4": ["A", "B", "E", "F", "Z"],
        "5-6": ["BA", "BC", "BG", "BE"],
        "7-8": ["BA", "BB", "FD", "FF", "AD", "FE"],
        "9": ["A", "B", "C", "N"],
        "10": ["A", "B", "C", "D", "E", "F", "G", "H", "N"],
        "11": ["A", "B", "C", "D", "G"],
        "12": ["Z"],
        "13": ["A", "B", "C", "D"],
        "14": ["B", "N"],
        "15": ["N"],
        "16": ["1", "2"],
        "17-18": ["1G"],
    },
    "REU615": {
        "1": ["H", "1"],
        "2": ["C"],
        "3": ["U"],
        "4": ["A", "B"],
        "5-6": ["CA", "CC", "EA"],
        "7-8": ["AD", "FE", "AH", "BB"],
        "9": ["A", "B", "C", "N"],
        "10": ["A", "B", "C", "D", "E", "F", "G", "H", "N"],
        "11": ["A", "B", "C", "D", "G"],
        "12": ["Z"],
        "13": ["A", "B", "C", "D"],
        "14": ["B", "N"],
        "15": ["N"],
        "16": ["1", "2"],
        "17-18": ["1G"],
    },
    "REV615": {
        "1": ["H", "1"],
        "2": ["C"],
        "3": ["V"],
        "4": ["B"],
        "5-6": ["BC", "BD", "BE", "BF"],
        "7-8": ["BA", "FD", "AD", "FE"],
        "9": ["A", "B", "C", "N"],
        "10": ["A", "B", "C", "D", "E", "F", "G", "H", "N"],
        "11": ["A", "B", "C", "D", "G"],
        "12": ["Z"],
        "13": ["A", "B", "C", "D"],
        "14": ["B", "D", "F", "N"],
        "15": ["N"],
        "16": ["1", "2"],
        "17-18": ["1G"],
    },
}

PDF_615_DEFAULT_ORDER_CODES = {
    # The guide's sample codes use language 2 / English panel A.  Its domestic
    # note and highlighted CN rows replace those two defaults with Z / C.
    "REF615": "HCFCACABNBCZCCN11G",
    "RED615": "HCDCACADABBZCAN11G",
    "REM615": "HCMAACABNBAZCBN11G",
    "REG615": "HCGDBDADNBAZCFN11G",
    "RET615": "HCTABABANBAZCNN11G",
    "REU615": "HCUAEAADNBAZCBN11G",
    "REV615": "HCVBBCADNBAZCNN11G",
}


def pdf_requirement(
    position: str,
    codes: list[str],
    *,
    mode: str = "AnyOf",
    when_position: str | None = None,
    when_codes: list[str] | None = None,
) -> dict:
    requirement = {
        "position": position,
        "codes": codes,
        "mode": mode,
        "message": "根据 615 CN 5.1 选型指南",
        "whenSelections": [],
    }
    if when_position and when_codes:
        requirement["whenSelections"].append(
            {
                "position": when_position,
                "codes": when_codes,
                "mode": "AnyOf",
            }
        )
    return requirement


PDF_615_SELECTION_REQUIREMENTS: dict[tuple[str, str, str], list[dict]] = {}


def add_pdf_requirements(
    device_id: str,
    position: str,
    option_codes: list[str],
    *requirements: dict,
) -> None:
    for option_code in option_codes:
        PDF_615_SELECTION_REQUIREMENTS[(device_id, position, option_code)] = list(requirements)


# REF615, guide page 1.
add_pdf_requirements("REF615", "5-6", ["AC", "AD"], pdf_requirement("4", ["C", "D"]))
add_pdf_requirements("REF615", "5-6", ["FC", "FD"], pdf_requirement("4", ["D"]))
add_pdf_requirements("REF615", "5-6", ["AE", "AF"], pdf_requirement("4", ["J", "N", "Z"]))
add_pdf_requirements("REF615", "5-6", ["FE", "FF"], pdf_requirement("4", ["J", "N"]))
add_pdf_requirements("REF615", "7-8", ["AB"], pdf_requirement("4", ["C"]))
add_pdf_requirements(
    "REF615",
    "7-8",
    ["AD"],
    pdf_requirement("4", ["D", "J", "N", "Z"]),
    pdf_requirement("5-6", ["FE", "FF"], when_position="4", when_codes=["J", "N"]),
)
add_pdf_requirements(
    "REF615",
    "7-8",
    ["FE"],
    pdf_requirement("4", ["D", "J", "N"]),
    pdf_requirement("5-6", ["FE", "FF"], when_position="4", when_codes=["J", "N"]),
)
add_pdf_requirements(
    "REF615",
    "7-8",
    ["AF", "FB"],
    pdf_requirement("4", ["D"]),
    pdf_requirement("5-6", ["AC", "AD"]),
)
add_pdf_requirements(
    "REF615",
    "7-8",
    ["AG", "FC"],
    pdf_requirement("4", ["J", "N"]),
    pdf_requirement("5-6", ["AE", "AF"]),
)
add_pdf_requirements("REF615", "10", ["F", "G", "H"], pdf_requirement("4", ["J", "N", "Z"]))
add_pdf_requirements("REF615", "14", ["D", "E", "F", "G"], pdf_requirement("4", ["J", "N", "Z"]))
add_pdf_requirements("REF615", "14", ["H", "J", "K", "L", "M", "P", "Q"], pdf_requirement("4", ["N"]))
add_pdf_requirements("REF615", "15", ["A", "B", "C", "D"], pdf_requirement("4", ["J", "N", "Z"]))
add_pdf_requirements("REF615", "15", ["E"], pdf_requirement("4", ["D"]))
add_pdf_requirements("REF615", "15", ["G", "H", "J", "K"], pdf_requirement("4", ["N"]))
add_pdf_requirements("REF615", "15", ["N"], pdf_requirement("4", ["C", "D"]))

# RED615, guide page 2.
add_pdf_requirements("RED615", "5-6", ["AC"], pdf_requirement("4", ["C"]))
add_pdf_requirements("RED615", "5-6", ["AE", "AF", "FE", "FF"], pdf_requirement("4", ["D"]))
add_pdf_requirements(
    "RED615",
    "7-8",
    ["AD"],
    pdf_requirement("4", ["C", "D"]),
    pdf_requirement("5-6", ["FE", "FF"], when_position="4", when_codes=["D"]),
)
add_pdf_requirements("RED615", "7-8", ["AF"], pdf_requirement("4", ["C"]))
add_pdf_requirements(
    "RED615",
    "7-8",
    ["AG"],
    pdf_requirement("4", ["D"]),
    pdf_requirement("5-6", ["AE", "AF"]),
)
add_pdf_requirements("RED615", "10", ["L", "M"], pdf_requirement("9", ["N"]))
add_pdf_requirements(
    "RED615",
    "10",
    ["P", "Q"],
    pdf_requirement("4", ["D"]),
    pdf_requirement("9", ["N"]),
)
add_pdf_requirements("RED615", "11", ["D", "G"], pdf_requirement("9", ["N"], mode="NoneOf"))
add_pdf_requirements("RED615", "14", ["A"], pdf_requirement("4", ["C", "D"]))
add_pdf_requirements("RED615", "14", ["D", "E", "H", "L", "M"], pdf_requirement("4", ["D"]))
add_pdf_requirements("RED615", "15", ["A", "B", "C", "D"], pdf_requirement("4", ["D"]))
add_pdf_requirements("RED615", "15", ["E", "N"], pdf_requirement("4", ["C"]))

# REM615, guide page 3.
add_pdf_requirements("REM615", "5-6", ["AC", "AD", "AG", "AH"], pdf_requirement("4", ["A"]))
add_pdf_requirements("REM615", "5-6", ["AE", "AF"], pdf_requirement("4", ["C", "Z"]))
add_pdf_requirements("REM615", "5-6", ["CA", "CB"], pdf_requirement("4", ["B"]))
add_pdf_requirements(
    "REM615",
    "5-6",
    ["CC", "CD"],
    pdf_requirement("4", ["B"]),
    pdf_requirement("7-8", ["AH", "FD"]),
)
add_pdf_requirements("REM615", "7-8", ["AB", "FE"], pdf_requirement("4", ["A"]))
add_pdf_requirements("REM615", "7-8", ["AD"], pdf_requirement("4", ["A", "Z"]))
add_pdf_requirements("REM615", "7-8", ["AG", "FC"], pdf_requirement("4", ["C"]))
add_pdf_requirements("REM615", "7-8", ["AH", "FD"], pdf_requirement("4", ["B"]))
add_pdf_requirements(
    "REM615",
    "7-8",
    ["AJ", "FF"],
    pdf_requirement("4", ["B"]),
    pdf_requirement("5-6", ["CA", "CB"]),
)
add_pdf_requirements("REM615", "10", ["F", "G", "H"], pdf_requirement("4", ["B", "C", "Z"]))

# REG615, guide page 4.
add_pdf_requirements("REG615", "5-6", ["AE", "AF", "FE", "FF"], pdf_requirement("4", ["A", "C"]))
add_pdf_requirements("REG615", "5-6", ["BC", "BD", "BE", "BF"], pdf_requirement("4", ["D"]))
add_pdf_requirements(
    "REG615",
    "7-8",
    ["AD", "FE"],
    pdf_requirement("4", ["A", "C", "D"]),
    pdf_requirement("5-6", ["FE", "FF"], when_position="4", when_codes=["A", "C"]),
    pdf_requirement("5-6", ["BC", "BD"], when_position="4", when_codes=["D"]),
)
add_pdf_requirements(
    "REG615",
    "7-8",
    ["AG", "FC"],
    pdf_requirement("4", ["A", "C"]),
    pdf_requirement("5-6", ["AE", "AF"]),
)
add_pdf_requirements(
    "REG615",
    "7-8",
    ["BA", "FD"],
    pdf_requirement("4", ["D"]),
    pdf_requirement("5-6", ["BE", "BF"]),
)

# RET615, guide page 5.
add_pdf_requirements("RET615", "5-6", ["BA"], pdf_requirement("4", ["A", "B", "Z"]))
add_pdf_requirements("RET615", "5-6", ["BC", "BE"], pdf_requirement("4", ["E", "F"]))
add_pdf_requirements(
    "RET615",
    "5-6",
    ["BG"],
    pdf_requirement("4", ["A", "B", "Z"]),
    pdf_requirement("7-8", ["BA"]),
)
add_pdf_requirements(
    "RET615",
    "7-8",
    ["BA"],
    pdf_requirement("4", ["A", "B", "E", "F", "Z"]),
    pdf_requirement("5-6", ["BE"], when_position="4", when_codes=["E", "F"]),
)
add_pdf_requirements(
    "RET615",
    "7-8",
    ["BB"],
    pdf_requirement("4", ["A", "B", "Z"]),
    pdf_requirement("5-6", ["BA"]),
)
add_pdf_requirements(
    "RET615",
    "7-8",
    ["FD"],
    pdf_requirement("4", ["A", "B", "E", "F"]),
    pdf_requirement("5-6", ["BE"], when_position="4", when_codes=["E", "F"]),
)
add_pdf_requirements(
    "RET615",
    "7-8",
    ["FF"],
    pdf_requirement("4", ["A", "B"]),
    pdf_requirement("5-6", ["BA"]),
)
add_pdf_requirements(
    "RET615",
    "7-8",
    ["AD", "FE"],
    pdf_requirement("4", ["E", "F"]),
    pdf_requirement("5-6", ["BC"]),
)
add_pdf_requirements("RET615", "10", ["F", "G", "H"], pdf_requirement("4", ["E", "F"]))

# REU615, guide page 6.
add_pdf_requirements("REU615", "5-6", ["CA"], pdf_requirement("4", ["B"]))
add_pdf_requirements(
    "REU615",
    "5-6",
    ["CC"],
    pdf_requirement("4", ["B"]),
    pdf_requirement("7-8", ["AH"]),
)
add_pdf_requirements("REU615", "5-6", ["EA"], pdf_requirement("4", ["A"]))
add_pdf_requirements("REU615", "7-8", ["AD", "FE"], pdf_requirement("4", ["A"]))
add_pdf_requirements("REU615", "7-8", ["AH"], pdf_requirement("4", ["B"]))
add_pdf_requirements(
    "REU615",
    "7-8",
    ["BB"],
    pdf_requirement("4", ["B"]),
    pdf_requirement("5-6", ["CA"]),
)
add_pdf_requirements("REU615", "10", ["F", "G", "H"], pdf_requirement("4", ["A", "B"]))
add_pdf_requirements("REU615", "14", ["B"], pdf_requirement("4", ["A"]))

# REV615, guide page 7.
add_pdf_requirements("REV615", "5-6", ["BC", "BD", "BE", "BF"], pdf_requirement("4", ["B"]))
add_pdf_requirements(
    "REV615",
    "7-8",
    ["BA", "FD"],
    pdf_requirement("4", ["B"]),
    pdf_requirement("5-6", ["BE", "BF"]),
)
add_pdf_requirements(
    "REV615",
    "7-8",
    ["AD", "FE"],
    pdf_requirement("4", ["B"]),
    pdf_requirement("5-6", ["BC", "BD"]),
)

PDF_615_SELECTION_EXCLUSIONS = {
    (device_id, "14", "B"): [
        {
            "positions": ["9", "10"],
            "codes": ["BB", "BN"],
            "message": "根据 615 CN 5.1 选型指南，选择弧光保护时，9-10 位不可为 BB/BN。",
        }
    ]
    for device_id in ["REM615", "RET615", "REU615"]
}

# The 620 lists remain based on the three previously supplied product guides.
# This turn supplied no new 620 selection source, so only the existing
# customer-visible limits are retained and its validation data is untouched.
PDF_OPTION_CODE_LIMITS = {
    ("REF620", "5-6"): {"AA", "AB", "AC"},
    ("REF620", "17-18"): {"1G"},
    ("REM620", "5-6"): {"AA", "AB", "AC", "AD", "DA"},
    ("REM620", "17-18"): {"1G"},
    ("RET620", "17-18"): {"1G"},
}

# The XML contains identifiers rather than customer-facing text for several
# fields. These overrides are transcribed from the CN ordering-code tables.
# Keys beginning with a series number apply to every device in that series;
# device-specific keys take precedence.
SERIES_OPTION_TEXT_OVERRIDES = {
    ("615", "10", "F"): {
        "shortDescription": "100Base-FX（1xLC，2xRJ45）+ HSR/PRP + IEC 61850-9-2LE",
    },
    ("615", "10", "G"): {
        "shortDescription": "100Base-TX（3xRJ45）+ HSR/PRP + IEC 61850-9-2LE",
    },
    ("615", "10", "H"): {
        "shortDescription": "100Base-FX（2xLC，1xRJ45）+ HSR/PRP + IEC 61850-9-2LE",
    },
    ("620", "9", "A"): {
        "description": "串口 RS-485（包括 IRIG-B 输入）",
        "shortDescription": "RS-485（包括 IRIG-B）",
    },
    ("620", "9", "B"): {
        "description": "串口玻璃光纤（ST）",
        "shortDescription": "玻璃光纤 ST",
    },
    ("620", "9", "C"): {
        "description": "串口 RS-232/485（包括 IRIG-B 输入）",
        "shortDescription": "RS-232/485（包括 IRIG-B）",
    },
    ("620", "9", "N"): {
        "description": "无串口通信模块",
        "shortDescription": "无",
    },
    ("620", "10", "A"): {
        "description": "以太网 100Base-FX（1xLC）",
        "shortDescription": "100Base-FX（1xLC）",
    },
    ("620", "10", "B"): {
        "description": "以太网 100Base-TX（1xRJ-45）",
        "shortDescription": "100Base-TX（1xRJ-45）",
    },
    ("620", "10", "C"): {
        "description": "以太网 100Base-TX/FX（1xLC，2xRJ-45），带 HSR/PRP",
        "shortDescription": "TX/FX（1xLC，2xRJ-45）+ HSR/PRP",
    },
    ("620", "10", "D"): {
        "description": "以太网 100Base-TX（3xRJ-45），带 HSR/PRP",
        "shortDescription": "TX（3xRJ-45）+ HSR/PRP",
    },
    ("620", "10", "E"): {
        "description": "以太网 100Base-TX/FX（2xLC，1xRJ-45），带 HSR/PRP",
        "shortDescription": "TX/FX（2xLC，1xRJ-45）+ HSR/PRP",
    },
    ("620", "10", "F"): {
        "description": "以太网 100Base-TX/FX（1xLC，2xRJ-45），带 HSR/PRP 和 IEC 61850-9-2LE",
        "shortDescription": "TX/FX（1xLC，2xRJ-45）+ HSR/PRP + 9-2LE",
    },
    ("620", "10", "G"): {
        "description": "以太网 100Base-TX（3xRJ-45），带 HSR/PRP 和 IEC 61850-9-2LE",
        "shortDescription": "TX（3xRJ-45）+ HSR/PRP + 9-2LE",
    },
    ("620", "10", "H"): {
        "description": "以太网 100Base-TX/FX（2xLC，1xRJ-45），带 HSR/PRP 和 IEC 61850-9-2LE",
        "shortDescription": "TX/FX（2xLC，1xRJ-45）+ HSR/PRP + 9-2LE",
    },
    ("620", "10", "N"): {
        "description": "无以太网通信模块",
        "shortDescription": "无",
    },
    ("620", "11", "A"): {"shortDescription": "IEC 61850"},
    ("620", "11", "B"): {"shortDescription": "Modbus"},
    ("620", "11", "C"): {"shortDescription": "IEC 61850 + Modbus"},
    ("620", "11", "D"): {"shortDescription": "IEC 60870-5-103"},
    ("620", "11", "E"): {"shortDescription": "DNP3"},
    ("620", "11", "G"): {"shortDescription": "IEC 61850 + IEC 60870-5-103"},
    ("620", "11", "H"): {"shortDescription": "IEC 61850 + DNP3"},
    ("620", "14", "B"): {"shortDescription": "弧光保护"},
    ("620", "17-18", "1G"): {"shortDescription": "2.0 FP1"},
}

DEVICE_OPTION_TEXT_OVERRIDES = {
    ("REF615", "4", "J"): {
        "description": "三相方向过流保护和方向接地保护、电压和频率的测量与保护、检同期和CB状态检测（电能质量可选选项、RTD可选选项）",
        "shortDescription": "方向过流/接地 + 电压/频率 + 检同期/CB状态",
    },
    ("REF615", "4", "N"): {
        "description": "三相方向过流保护和方向接地保护、多频导纳保护、电压和频率的测量与保护、差动保护、检同期和CB状态检测（电能质量可选选项、故障定位和分布式保护）",
        "shortDescription": "方向过流/接地 + 多频导纳 + 差动 + 电压/频率",
    },
    ("REF615", "4", "Z"): {
        "description": "三相方向过流保护和方向接地保护、电压和频率的测量与保护、检同期和CB状态检测（电能质量可选选项，固定12BI+10BO）",
        "shortDescription": "方向过流/接地 + 电压/频率 + 固定12BI+10BO",
    },
    ("RED615", "4", "D"): {
        "description": "带方向过流和接地保护、电压和频率的保护与测量、同期检测和断路器状态监测的线路差动保护（RTD选项、电能质量选项和故障定位选项）",
        "shortDescription": "线路差动 + 方向过流/接地 + 电压/频率 + 同期检测",
    },
    ("RED615", "10", "G"): {
        "shortDescription": "以太网 100Base-TX（RJ45）+ 线路差动（多模）",
    },
    ("RED615", "10", "H"): {
        "shortDescription": "以太网 100Base-TX（RJ45）+ 线路差动（单模）",
    },
    ("RED615", "10", "L"): {
        "shortDescription": "100Base-FX（2xLC）+ HSR/PRP + 线路差动（多模）",
    },
    ("RED615", "10", "M"): {
        "shortDescription": "100Base-FX（2xLC）+ HSR/PRP + 线路差动（单模）",
    },
    ("RED615", "10", "P"): {
        "shortDescription": "100Base-FX（2xLC）+ HSR/PRP + 9-2LE + 线路差动（多模）",
    },
    ("RED615", "10", "Q"): {
        "shortDescription": "100Base-FX（2xLC）+ HSR/PRP + 9-2LE + 线路差动（单模）",
    },
    ("RED615", "11", "D"): {
        "description": "IEC 103",
        "shortDescription": "IEC 103",
    },
    ("RED615", "11", "G"): {
        "description": "IEC 61850 + IEC103",
        "shortDescription": "IEC 61850 + IEC103",
    },
    ("REM615", "4", "Z"): {
        "description": "带频率和电压测量保护功能的电机保护（12BI+10BO）",
        "shortDescription": "带频率和电压测量保护功能的电机保护（12BI+10BO）",
    },
    ("REM615", "14", "B"): {
        "description": "弧光保护",
        "shortDescription": "弧光保护",
    },
    ("RET615", "4", "E"): {
        "description": "双绕组变压器差动保护，高压侧采用数值限制的接地保护，带有电压保护和测量功能",
        "shortDescription": "双绕组差动 + 高压侧数值限制接地 + 电压保护/测量",
    },
    ("RET615", "4", "F"): {
        "description": "双绕组变压器差动保护，低压侧采用数值限制的接地保护，带有电压保护和测量功能",
        "shortDescription": "双绕组差动 + 低压侧数值限制接地 + 电压保护/测量",
    },
    ("RET615", "14", "B"): {
        "description": "弧光保护",
        "shortDescription": "弧光保护",
    },
    ("REU615", "14", "B"): {
        "description": "弧光保护",
        "shortDescription": "弧光保护",
    },
    ("REV615", "4", "B"): {
        "description": "电容器组过负荷和不平衡保护、无方向过流和方向接地保护、电压和频率的保护和测量、以及断路器状态监视",
        "shortDescription": "电容器组过负荷/不平衡 + 过流/接地 + 电压/频率",
    },
    ("REF620", "15", "L"): {
        "shortDescription": "全部保护包（故障定位、电容器组、分布式发电、功率）",
    },
}


def load_previous_descriptions() -> dict[tuple[str, str, str], dict[str, str]]:
    if not OUTPUT.exists():
        return {}

    data = json.loads(OUTPUT.read_text(encoding="utf-8-sig"))
    descriptions: dict[tuple[str, str, str], dict[str, str]] = {}
    for series in data.get("series", []):
        for device in series.get("devices", []):
            device_id = device.get("id", "")
            for group in device.get("groups", []):
                position = group.get("position", "")
                for option in group.get("options", []):
                    code = option.get("code", "")
                    if not device_id or not position or not code:
                        continue
                    descriptions[(device_id, position, code)] = {
                        "description": option.get("description", ""),
                        "shortDescription": option.get("shortDescription", ""),
                    }
    return descriptions


def normalize_position(location: str) -> str:
    return location.replace("+", "-").strip()


def code_length(position: str) -> int:
    return 2 if position in {"5-6", "7-8", "9-10", "17-18"} else 1


def segment_for_position(order_code: str, position: str) -> str:
    if "-" in position:
        start_text, end_text = position.split("-", 1)
        start = int(start_text)
        end = int(end_text)
        return order_code[start - 1 : end]

    index = int(position)
    return order_code[index - 1 : index]


def short_description(value: str) -> str:
    text = re.sub(r"\s+", " ", value or "").strip()
    return text if len(text) <= 52 else text[:49].rstrip() + "..."


def humanize_token(token: str) -> str:
    text = re.sub(r"[_\-]+", " ", token or "").strip()
    text = re.sub(r"\s+", " ", text)
    return text


def display_group_name(series_key: str, group_name: str) -> str:
    return GROUP_NAME_OVERRIDES.get(series_key, {}).get(
        group_name,
        GROUP_NAMES.get(group_name, humanize_token(group_name)),
    )


def device_title(device_id: str) -> str:
    return {
        "REF615": "馈线保护测控",
        "RED615": "线路差动保护测控",
        "REM615": "电机保护测控",
        "REG615": "发电机与分布式保护测控",
        "RET615": "变压器保护测控",
        "REU615": "电压保护测控",
        "REV615": "电容器保护测控",
        "REF620": "馈线保护测控",
        "REM620": "电机保护测控",
        "RET620": "变压器保护测控",
    }.get(device_id, "保护测控")


def device_display_title(device_id: str) -> str:
    return {
        "REF620": "馈线保护测控装置",
        "REM620": "电机保护测控装置",
        "RET620": "变压器保护测控装置",
    }.get(device_id, device_title(device_id))


def fallback_description(
    series_key: str,
    group_name: str,
    code: str,
    token: str,
    versions: dict[str, str],
    device_id: str,
) -> str:
    if group_name == "MainApps":
        return device_title(device_id)
    if group_name == "Versions" and code in versions:
        return f"产品版本 {versions[code]}"
    if group_name == "Mountings":
        return {
            "H": "615系列保护测控装置（包括外壳）",
            "1": "615系列保护测控装置（带保护涂层）",
            "N": "620系列保护测控装置（包括外壳）",
            "5": "620系列保护测控装置（带保护涂层）",
        }.get(code, f"{display_group_name(series_key, group_name)} {code}")
    if group_name == "Standards":
        return {"B": "全球通用", "C": "中文版"}.get(code, f"标准 {code}")
    if group_name == "Languages":
        return {
            "1": "英文",
            "2": "中文",
            "Z": "中文",
        }.get(code, f"语言 {code}")
    if group_name == "PowerSupplies":
        return {"1": "高压电源", "2": "低压电源"}.get(code, f"电源 {code}")

    label = display_group_name(series_key, group_name)
    suffix = humanize_token(token)
    return f"{label} {code}" if not suffix else f"{label} {code}（{suffix}）"


def option_description(
    previous: dict[tuple[str, str, str], dict[str, str]],
    series_key: str,
    device_id: str,
    position: str,
    group_name: str,
    code: str,
    token: str,
    versions: dict[str, str],
) -> tuple[str, str]:
    prior = previous.get((device_id, position, code))
    if prior and prior.get("description"):
        description = prior["description"]
        short = prior.get("shortDescription") or short_description(description)
    else:
        description = fallback_description(series_key, group_name, code, token, versions, device_id)
        short = short_description(description)

    override = dict(SERIES_OPTION_TEXT_OVERRIDES.get((series_key, position, code), {}))
    override.update(DEVICE_OPTION_TEXT_OVERRIDES.get((device_id, position, code), {}))
    return override.get("description", description), override.get("shortDescription", short)


def parse_device_xml(path: Path, series_key: str, previous: dict[tuple[str, str, str], dict[str, str]]) -> dict:
    root = ET.parse(path).getroot()
    device_prefix = path.stem.split()[0].upper()
    device_id = f"{device_prefix}{series_key}"
    xml_default_code = root.find("Default").attrib.get("OrderCode", "")
    default_code = PDF_615_DEFAULT_ORDER_CODES.get(device_id, xml_default_code)
    pdf_615_groups = PDF_615_OPTION_CODES.get(device_id) if series_key == "615" else None
    if series_key == "615" and pdf_615_groups is None:
        raise ValueError(f"{device_id}: no complete 615 PDF selection catalog was defined")

    versions = {
        version.attrib.get("Id", ""): version.attrib.get("IED_version", "")
        for version in root.findall("./OrderCodeVersions/Version")
    }

    groups = []
    parsed_positions: set[str] = set()
    for digit in root.findall("./OrderCodes/Digit"):
        location = digit.attrib.get("Location", "")
        if location == "1+2+3":
            continue

        position = normalize_position(location)
        if not position or not re.fullmatch(r"\d+(?:-\d+)?", position):
            continue

        expected_pdf_codes = pdf_615_groups.get(position) if pdf_615_groups is not None else None
        if pdf_615_groups is not None and expected_pdf_codes is None:
            raise ValueError(f"{device_id} position {position}: group is not present in the 615 PDF catalog")

        group_name = digit.attrib.get("Group", "")
        seen_codes: set[str] = set()
        options = []
        default_segment = segment_for_position(default_code, position) if default_code else ""
        if (
            expected_pdf_codes is not None
            and default_segment.upper() not in {code.upper() for code in expected_pdf_codes}
        ):
            raise ValueError(
                f"{device_id} position {position}: PDF default {default_segment!r} "
                f"is not present in {expected_pdf_codes}"
            )
        for option in digit.findall("Option"):
            code = option.attrib.get("Id", "").strip()
            normalized_code = code.upper()
            if not code or normalized_code in seen_codes:
                continue
            if group_name == "Languages" and code not in LANGUAGE_CODES.get(series_key, {code}):
                continue
            allowed_pdf_codes = (
                set(expected_pdf_codes)
                if expected_pdf_codes is not None
                else PDF_OPTION_CODE_LIMITS.get((device_id, position))
            )
            if allowed_pdf_codes is not None and normalized_code not in allowed_pdf_codes:
                continue
            seen_codes.add(normalized_code)
            description, short = option_description(
                previous,
                series_key,
                device_id,
                position,
                group_name,
                code,
                option.attrib.get("Description", ""),
                versions,
            )
            options.append(
                {
                    "code": code,
                    "version": option.attrib.get("Version", "*").strip() or "*",
                    "description": description,
                    "shortDescription": short,
                    "isDefault": code.upper() == default_segment.upper(),
                    "requiredSelections": PDF_615_SELECTION_REQUIREMENTS.get(
                        (device_id, position, normalized_code),
                        [],
                    ),
                    "excludedCombinedSelections": PDF_615_SELECTION_EXCLUSIONS.get(
                        (device_id, position, normalized_code),
                        [],
                    ),
                }
            )

        if expected_pdf_codes is not None:
            missing_codes = [
                code
                for code in expected_pdf_codes
                if code.upper() not in seen_codes
            ]
            if missing_codes:
                raise ValueError(
                    f"{device_id} position {position}: PDF codes missing from XML metadata: {missing_codes}"
                )
            order = {code.upper(): index for index, code in enumerate(expected_pdf_codes)}
            options.sort(key=lambda option: order[option["code"].upper()])

        if not any(item["isDefault"] for item in options) and options:
            options[0]["isDefault"] = True

        groups.append(
            {
                "position": position,
                "name": display_group_name(series_key, group_name) or position,
                "isRequired": True,
                "options": options,
            }
        )
        parsed_positions.add(position)

    if pdf_615_groups is not None:
        missing_positions = [
            position
            for position in pdf_615_groups
            if position not in parsed_positions
        ]
        if missing_positions:
            raise ValueError(f"{device_id}: PDF positions missing from XML metadata: {missing_positions}")

    return {
        "id": device_id,
        "name": f"{device_id} {device_display_title(device_id)}",
        "description": f"{device_display_title(device_id)}，{series_key} 系列 CN {SERIES_INFO[series_key]['name'].split()[-1]} 订货号",
        "groups": sorted(groups, key=lambda group: int(group["position"].split("-", 1)[0])),
        "validationBlocks": parse_validation_blocks(root, series_key),
    }


def set_default_option(options: list[dict], default_code: str) -> None:
    if not any(option.get("code", "").upper() == default_code.upper() for option in options):
        return
    for option in options:
        option["isDefault"] = option.get("code", "").upper() == default_code.upper()


def parse_validation_blocks(root: ET.Element, series_key: str) -> list[dict]:
    blocks = []
    valid_order_codes = root.find("ValidOrderCodes")
    if valid_order_codes is None:
        return blocks

    for block in list(valid_order_codes):
        block_name = block.tag
        positions = BLOCK_POSITIONS.get((series_key, block_name))
        if not positions:
            continue

        rules = []
        expected_length = sum(code_length(position) for position in positions)
        for rule in block.findall("Rule"):
            pattern = (rule.text or "").strip().upper()
            if not pattern:
                continue
            if len(pattern) < expected_length:
                pattern = pattern + "#" * (expected_length - len(pattern))
            rules.append(
                {
                    "pattern": pattern,
                    "version": rule.attrib.get("Version", "*").strip() or "*",
                }
            )

        if not rules:
            continue

        blocks.append(
            {
                "name": block_name,
                "displayName": BLOCK_NAMES.get(block_name, block_name),
                "positions": positions,
                "rules": rules,
            }
        )

    return blocks


def build_series(series_key: str, previous: dict[tuple[str, str, str], dict[str, str]]) -> dict:
    source_dir = SOURCE_DIRS[series_key]
    devices = [
        parse_device_xml(path, series_key, previous)
        for path in sorted(source_dir.glob(f"* {series_key}_*.xml"))
    ]
    devices.sort(key=lambda device: DEVICE_ORDER.get(device["id"], 999))
    info = SERIES_INFO[series_key]
    return {
        "id": info["id"],
        "name": info["name"],
        "description": info["description"],
        "sourceDocuments": list(info["sources"]),
        "devices": devices,
    }


def main() -> None:
    previous = load_previous_descriptions()
    data = {
        "formatVersion": 2,
        "series": [
            build_series("615", previous),
            build_series("620", previous),
        ],
    }
    OUTPUT.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    device_count = sum(len(series["devices"]) for series in data["series"])
    block_count = sum(len(device.get("validationBlocks", [])) for series in data["series"] for device in series["devices"])
    print(f"Wrote {OUTPUT}")
    print(f"Series: {len(data['series'])}; devices: {device_count}; validation blocks: {block_count}")


if __name__ == "__main__":
    main()
