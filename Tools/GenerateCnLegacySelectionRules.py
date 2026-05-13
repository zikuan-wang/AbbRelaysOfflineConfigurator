import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TEXT_DIR = ROOT / "Generated" / "PdfText"
OUTPUT = ROOT / "AbbRelaysOfflineConfigurator" / "Data" / "CnLegacySelectionRules.json"


GROUP_NAMES = {
    "1": "装置",
    "2": "标准",
    "3": "主要应用",
    "4": "标准配置",
    "5-6": "模拟量输入输出",
    "7-8": "开关量输入输出 / 可选板卡",
    "9": "串口选项",
    "10": "网口选项",
    "9-10": "通讯模块",
    "11": "通信协议",
    "12": "语言",
    "13": "前面板",
    "14": "选项 1",
    "15": "选项 2",
    "16": "电源",
    "17-18": "版本",
}


def code_len(position: str) -> int:
    return 2 if position in {"5-6", "7-8", "9-10", "17-18"} else 1


def parse_615_pdf() -> list[dict]:
    path = next(TEXT_DIR.glob("pdf_1_*.txt"))
    text = path.read_text(encoding="utf-8")
    pages = re.split(r"===== PAGE \d+ =====", text)
    devices: list[dict] = []

    for page in pages:
        header = re.search(r"([A-Z]{3}615) Order Code", page)
        if not header:
            continue

        device_id = header.group(1)
        example_match = re.search(r"Order Code:\s*([A-Z0-9 ]+)", page)
        example_code = re.sub(r"\s+", "", example_match.group(1)) if example_match else ""
        groups = parse_615_groups(page)
        apply_defaults(groups, example_code)

        devices.append(
            {
                "id": device_id,
                "name": f"{device_id} {device_title(device_id)}",
                "description": f"{device_title(device_id)}，615 系列 5.0 FP1 CN 订货号",
                "groups": groups,
            }
        )

    return devices


def parse_615_groups(page: str) -> list[dict]:
    header_re = re.compile(r"^(\d+(?:-\d+)?)\)\s*(.*)")
    groups: list[dict] = []
    current: dict | None = None
    current_option: dict | None = None

    for raw in page.splitlines():
        line = raw.strip()
        if not line:
            continue
        if line == "Key" or line == "Range" or line.startswith("Key ") or line.startswith("Range "):
            if groups:
                break
            continue

        header = header_re.match(line)
        if header:
            position = header.group(1)
            name = clean_group_name(position, header.group(2))
            current = {"position": position, "name": name, "isRequired": True, "options": []}
            groups.append(current)
            current_option = None
            continue

        if current is None:
            continue

        length = code_len(current["position"])
        option = re.match(rf"^([A-Z0-9]{{{length}}})\s+(.+)$", line)
        if option:
            current_option = build_option(current["position"], option.group(1), option.group(2))
            current["options"].append(current_option)
            continue

        if length == 1 and re.match(r"^[A-Z0-9]$", line):
            current_option = build_option(current["position"], line, "")
            current["options"].append(current_option)
            continue

        if current_option is not None:
            current_option["description"] = " ".join(
                part for part in [current_option["description"], line] if part
            )

    for group in groups:
        if group["position"] == "17-18":
            group["options"] = [option for option in group["options"] if option["code"] == "1G"] or group["options"][:1]
            for option in group["options"]:
                option["description"] = "Product Version 5.0 FP1"
                option["shortDescription"] = "5.0 FP1"

    add_615_rules(groups)
    return groups


def clean_group_name(position: str, raw_name: str) -> str:
    cleaned = re.sub(r"\s+[A-Z]-\d$|\s+1-Z$", "", raw_name).strip()
    return GROUP_NAMES.get(position, cleaned or position)


def build_option(position: str, code: str, description: str) -> dict:
    description = normalize_description(description)
    return {
        "code": code,
        "description": description,
        "shortDescription": build_short_description(code, description),
        "requiredSelections": [],
        "excludedCombinedSelections": [],
    }


def normalize_description(value: str) -> str:
    return re.sub(r"\s+", " ", value).strip()


def build_short_description(code: str, description: str) -> str:
    text = normalize_description(description)
    if len(text) <= 52:
        return text
    return text[:49].rstrip() + "..."


def device_title(device_id: str) -> str:
    return {
        "REF615": "馈线保护测控",
        "RED615": "线路差动保护测控",
        "REM615": "电机保护测控",
        "REG615": "发电机与分布式保护测控",
        "RET615": "变压器保护测控",
        "REU615": "电压保护测控",
        "REV615": "电容器保护测控",
    }.get(device_id, "保护测控")


def group_by_position(groups: list[dict]) -> dict[str, dict]:
    return {group["position"]: group for group in groups}


def apply_defaults(groups: list[dict], example_code: str) -> None:
    if not example_code:
        return

    index = 0
    for group in groups:
        length = code_len(group["position"])
        default_code = example_code[index : index + length]
        index += length
        for option in group["options"]:
            option["isDefault"] = option["code"].upper() == default_code.upper()


def add_615_rules(groups: list[dict]) -> None:
    groups_by_pos = group_by_position(groups)
    standard_codes = {option["code"] for option in groups_by_pos.get("4", {}).get("options", [])}

    for group in groups:
        for option in group["options"]:
            desc = option["description"]
            if group["position"] in {"5-6", "7-8", "10", "14", "15"}:
                required_standard = infer_standard_config_codes(desc, standard_codes)
                if required_standard:
                    option["requiredSelections"].append(
                        requirement("4", sorted(required_standard), "需匹配标准配置")
                    )

            aim_required = infer_codes_after(desc, r"if\s+AIM\s+([A-Z/]+)")
            if aim_required:
                option["requiredSelections"].append(requirement("5-6", aim_required, "需匹配模拟量输入"))

            bio_required = infer_codes_after(desc, r"if\s+BIO\s+([A-Z/]+)")
            if bio_required:
                option["requiredSelections"].append(requirement("7-8", bio_required, "需匹配开关量输入输出"))

            if "if 9) is N" in desc:
                option["requiredSelections"].append(requirement("9", ["N"], "串口选项需为 N"))
            if "if 9) is not N" in desc:
                option["requiredSelections"].append(requirement("9", ["N"], "串口选项不能为 N", mode="NoneOf"))

            if "不可为BB/BN" in desc or "不可为 BB/BN" in desc:
                option["excludedCombinedSelections"].append(
                    {
                        "positions": ["9", "10"],
                        "codes": ["BB", "BN"],
                        "message": "弧光保护不能与 BB 或 BN 通讯组合同时选择",
                    }
                )


def infer_standard_config_codes(description: str, standard_codes: set[str]) -> set[str]:
    if not standard_codes:
        return set()

    tail = description[-90:]
    found: set[str] = set()
    for code in standard_codes:
        if re.search(rf"(^|[\s,(]){re.escape(code)}($|[\s,),])", tail):
            found.add(code)
    return found


def infer_codes_after(description: str, pattern: str) -> list[str]:
    match = re.search(pattern, description, flags=re.IGNORECASE)
    if not match:
        return []
    return [item for item in re.split(r"[/,\s]+", match.group(1).upper()) if item]


def requirement(position: str, codes: list[str], message: str, mode: str = "AnyOf") -> dict:
    return {"position": position, "codes": codes, "mode": mode, "message": message}


def common_620_groups(application_code: str, application_name: str, analog_options: list[tuple[str, str]], option2: list[tuple[str, str]]) -> list[dict]:
    groups = [
        group("1", "装置", [("N", "620系列保护测控装置（包括外壳）"), ("5", "具备保形涂层的完整装置")], "N"),
        group("2", "标准", [("B", "IEC"), ("C", "CN")], "C"),
        group("3", "主要应用", [(application_code, application_name)], application_code),
        group("4", "功能应用", [("N", "配置实例")], "N"),
        group("5-6", "模拟量输入输出", analog_options, analog_options[0][0]),
        group(
            "7-8",
            "可选板卡",
            [
                ("AA", "可选 I/O：8BI + 4BO"),
                ("AB", "可选 RTD：6RTD输入 + 2mA输入"),
                ("AC", "可选高速 I/O：8BI + 3HSO"),
                ("NN", "无可选板卡"),
            ],
            "NN",
        ),
        group("9-10", "通讯模块", common_620_communication_options(), "AB"),
        group(
            "11",
            "通信协议",
            [
                ("A", "IEC 61850（以太网通信模块和无通信模块）"),
                ("B", "Modbus（以太网/串行或以太网+串行通信模块）"),
                ("C", "IEC 61850 + Modbus"),
                ("D", "IEC 60870-5-103"),
                ("E", "DNP3"),
                ("G", "IEC 61850 + IEC 60870-5-103"),
                ("H", "IEC 61850 + DNP3"),
            ],
            "C",
        ),
        group("12", "语言", [("1", "英文"), ("2", "英文和中文")], "2"),
        group("13", "前面板", [("B", "大屏幕LCD，带单线图显示，英文面板"), ("D", "大屏幕LCD，带单线图显示，中文面板")], "D"),
        group(
            "14",
            "选项 1",
            [
                option("B", "弧光保护（需要特定通信模块，不能选 BN、BB、CB 和 CN）", excluded_position=("9-10", ["BN", "BB", "CB", "CN"])),
                option("N", "无"),
            ],
            "N",
        ),
        group("15", "选项 2", option2, "N"),
        group("16", "电源", [("1", "48-250 VDC, 100-240 VAC"), ("2", "24-60 VDC")], "1"),
        group("17-18", "版本", [("1G", "Product Version 2.0 FP1")], "1G"),
    ]
    return groups


def common_620_communication_options() -> list[tuple[str, str]]:
    return [
        ("AA", "串口 RS-485（包括 IRIG-B 输入）+ 以太网 100Base-FX (1xLC)"),
        ("AB", "串口 RS-485（包括 IRIG-B 输入）+ 以太网 100Base-TX (1xRJ45)"),
        ("AN", "串口 RS-485（包括 IRIG-B 输入）"),
        ("BB", "串口玻璃光纤 ST + 以太网 100Base-TX + RS-485/RS-232/485 + IRIG-B"),
        ("BC", "串口玻璃光纤 ST + 以太网 100Base-TX/FX (1xLC, 2xRJ45) HSR/PRP"),
        ("BD", "串口玻璃光纤 ST + 以太网 100Base-TX (3xRJ45) HSR/PRP"),
        ("BE", "串口玻璃光纤 ST + 以太网 100Base-TX/FX (2xLC, 1xRJ45) HSR/PRP"),
        ("BF", "串口玻璃光纤 ST + 以太网 100Base-TX/FX HSR/PRP + IEC61850-9-2LE"),
        ("BG", "串口玻璃光纤 ST + 以太网 100Base-TX (3xRJ45) HSR/PRP + IEC61850-9-2LE"),
        ("BH", "串口玻璃光纤 ST + 以太网 100Base-TX/FX HSR/PRP + IEC61850-9-2LE"),
        ("BN", "串口玻璃光纤 ST + RS-485/RS-232/485 + IRIG-B"),
        ("CB", "RS232/485（包括 IRIG-B 输入）+ 以太网 100Base-TX (1xRJ45)"),
        ("CN", "RS232/485 + RS-485/玻璃光纤 ST（包括 IRIG-B 输入）"),
        ("NA", "以太网 100Base-FX (1xLC)"),
        ("NB", "以太网 100Base-TX (1xRJ45)"),
        ("NC", "以太网 100Base-TX/FX (1xLC, 2xRJ45) HSR/PRP"),
        ("ND", "以太网 100Base-TX (3xRJ45) HSR/PRP"),
        ("NE", "以太网 100Base-TX/FX (2xLC, 1xRJ45) HSR/PRP"),
        ("NF", "以太网 100Base-TX/FX HSR/PRP + IEC61850-9-2LE"),
        ("NG", "以太网 100Base-TX (3xRJ45) HSR/PRP + IEC61850-9-2LE"),
        ("NH", "以太网 100Base-TX/FX HSR/PRP + IEC61850-9-2LE"),
        ("NN", "无通讯模块"),
    ]


def group(position: str, name: str, options_data, default_code: str) -> dict:
    options = []
    for item in options_data:
        if isinstance(item, dict):
            opt = item
        else:
            code, description = item
            opt = option(code, description)
        opt["isDefault"] = opt["code"] == default_code
        options.append(opt)
    return {"position": position, "name": name, "isRequired": True, "options": options}


def option(code: str, description: str, excluded_position: tuple[str, list[str]] | None = None) -> dict:
    opt = {
        "code": code,
        "description": description,
        "shortDescription": build_short_description(code, description),
        "requiredSelections": [],
        "excludedCombinedSelections": [],
    }
    if excluded_position:
        position, codes = excluded_position
        opt["requiredSelections"].append(
            {
                "position": position,
                "codes": codes,
                "mode": "NoneOf",
                "message": f"不能与 {position}={','.join(codes)} 同时选择",
            }
        )
    return opt


def build_620_devices() -> list[dict]:
    return [
        {
            "id": "REF620",
            "name": "REF620 馈线保护测控装置",
            "description": "馈线保护测控装置，620 系列 2.0 FP1 CN 订货号",
            "groups": common_620_groups(
                "F",
                "馈线保护测控",
                [
                    ("AA", "4I (Io 1/5A) + 5U + 24BI + 14BO"),
                    ("AB", "4I (Io 0.2/1A) + 5U + 24BI + 14BO"),
                    ("AC", "传感器 (3I + 3U) + 1CT + 16BI + 14BO"),
                ],
                [
                    ("F", "故障定位"),
                    ("C", "电容器组保护包"),
                    ("D", "联锁/互连/分布式发电保护包"),
                    ("P", "功率保护包"),
                    ("L", "所有选项：故障定位 + 电容器组保护 + 联锁/互连/分布式发电保护 + 功率保护"),
                    ("N", "无"),
                ],
            ),
        },
        {
            "id": "REM620",
            "name": "REM620 电机保护测控装置",
            "description": "电机保护测控装置，620 系列 2.0 FP1 CN 订货号",
            "groups": common_620_groups(
                "M",
                "电机保护测控装置",
                [
                    ("AA", "7I (Io 1/5A) + 5U + 12BI + 10BO + 6RTD + 2mA"),
                    ("AB", "7I (Io 0.2/1A) + 5U + 12BI + 10BO + 6RTD + 2mA"),
                    ("AC", "7I (Io 1/5A) + 5U + 20BI + 14BO"),
                    ("AD", "7I (Io 0.2/1A) + 5U + 20BI + 14BO"),
                    ("DA", "传感器 (3I + 3U) + 1CT + 16BI + 14BO"),
                ],
                [("S", "同步电机保护包"), ("N", "无")],
            ),
        },
        {
            "id": "RET620",
            "name": "RET620 变压器保护测控装置",
            "description": "变压器保护测控装置，620 系列 2.0 FP1 CN 订货号",
            "groups": common_620_groups(
                "T",
                "变压器保护测控装置",
                [("AA", "8I (Io 1/5A) + 6U + 8BI + 13BO + 2RTD + 1mA")],
                [("A", "自动电压调节器"), ("N", "无")],
            ),
        },
    ]


def main() -> None:
    data = {
        "formatVersion": 1,
        "series": [
            {
                "id": "615_CN_5_0_FP1",
                "name": "615 CN 5.0 FP1",
                "description": "615 系列 CN 5.0 FP1 装置订货号选型",
                "sourceDocuments": [],
                "devices": parse_615_pdf(),
            },
            {
                "id": "620_CN_2_0_FP1",
                "name": "620 CN 2.0 FP1",
                "description": "620 系列 CN 2.0 FP1 装置订货号选型",
                "sourceDocuments": [
                    "REF620_pg_757844_CNe.pdf",
                    "REM620_pg_757845_CNe.pdf",
                    "RET620_pg_757846_CNe.pdf",
                ],
                "devices": build_620_devices(),
            },
        ],
    }

    OUTPUT.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Wrote {OUTPUT}")


if __name__ == "__main__":
    main()
