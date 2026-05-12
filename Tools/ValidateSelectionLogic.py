import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
REX_XML = ROOT / "Rex615OfflineConfigurator" / "Data" / "REX615_ROL.xml"
CN_JSON = ROOT / "Rex615OfflineConfigurator" / "Data" / "CnLegacySelectionRules.json"


def code_length(position: str) -> int:
    return 2 if position in {"5-6", "7-8", "9-10", "17-18"} else 1


class Report:
    def __init__(self) -> None:
        self.errors: list[str] = []
        self.warnings: list[str] = []
        self.infos: list[str] = []

    def error(self, message: str) -> None:
        self.errors.append(message)

    def warning(self, message: str) -> None:
        self.warnings.append(message)

    def info(self, message: str) -> None:
        self.infos.append(message)

    def print(self) -> None:
        for title, items in [("ERROR", self.errors), ("WARNING", self.warnings), ("INFO", self.infos)]:
            print(f"\n[{title}] {len(items)}")
            for item in items:
                print(f"- {item}")


def split_csv(value: str | None) -> list[str]:
    if not value:
        return []
    return [item.strip() for item in value.split(",") if item.strip()]


def validate_rex615(report: Report) -> None:
    tree = ET.parse(REX_XML)
    root = tree.getroot()
    groups: dict[str, set[str]] = {}
    option_locations: dict[tuple[str, str], str] = {}
    module_options: dict[str, list[tuple[str, str]]] = {}
    expressions: list[tuple[str, str, str, str]] = []

    for container_name in ["MainCodes", "OptionCodes"]:
        container = root.find(container_name)
        if container is None:
            report.error(f"REX615 XML 缺少 {container_name}")
            continue

        elements = container.findall("Digit") if container_name == "MainCodes" else container.findall("Category")
        for group_element in elements:
            group_name = group_element.attrib.get("Group") or group_element.attrib.get("Name") or ""
            if not group_name:
                report.error(f"{container_name} 存在空分组名称")
                continue

            codes = groups.setdefault(group_name, set())
            for option in group_element.findall("Option"):
                code = option.attrib.get("Id", "")
                if not code:
                    report.error(f"REX615 {group_name} 存在空选项 Id")
                    continue
                if code in codes:
                    report.error(f"REX615 {group_name} 重复选项 {code}")
                codes.add(code)
                option_locations[(group_name, code)] = container_name

                module_type = option.attrib.get("ModuleType")
                if module_type:
                    module_options.setdefault(module_type, []).append((group_name, code))
                    try:
                        count = int(option.attrib.get("ModuleCount", "0"))
                    except ValueError:
                        count = 0
                    if count <= 0:
                        report.error(f"REX615 {group_name}/{code} 设置了 ModuleType={module_type} 但 ModuleCount 无效")

                for attr_name in ["Validity", "Requires"]:
                    expr = option.attrib.get(attr_name)
                    if expr:
                        expressions.append((group_name, code, attr_name, expr))

    for group_name, code, attr_name, expression in expressions:
        validate_rex_expression(report, groups, group_name, code, attr_name, expression)
    validate_rex_slot_constraints(report, root, groups, module_options)
    report.info(f"REX615 XML 分组 {len(groups)} 个，选项 {sum(len(codes) for codes in groups.values())} 个。")


def validate_rex_expression(
    report: Report,
    groups: dict[str, set[str]],
    owner_group: str,
    owner_code: str,
    attr_name: str,
    expression: str,
) -> None:
    for condition in [part.strip() for part in expression.split("&") if part.strip()]:
        if "=" not in condition:
            report.error(f"REX615 {owner_group}/{owner_code} {attr_name} 条件缺少 '='：{condition}")
            continue

        group_name, values = [part.strip() for part in condition.split("=", 1)]
        if group_name not in groups:
            report.error(f"REX615 {owner_group}/{owner_code} {attr_name} 引用未知分组：{group_name}")
            continue

        for raw_value in split_csv(values):
            value = raw_value[1:] if raw_value.startswith("!") else raw_value
            if not value:
                report.error(f"REX615 {owner_group}/{owner_code} {attr_name} 存在空条件值：{condition}")
            elif value not in groups[group_name]:
                report.error(f"REX615 {owner_group}/{owner_code} {attr_name} 引用 {group_name} 未知代码：{value}")


def validate_rex_slot_constraints(
    report: Report,
    root: ET.Element,
    groups: dict[str, set[str]],
    module_options: dict[str, list[tuple[str, str]]],
) -> None:
    housing_codes = groups.get("机箱") or groups.get("鏈虹") or set()
    versions = groups.get("版本") or groups.get("產品版本") or groups.get("浜у搧鐗堟湰") or set()

    for constraints in root.findall("SlotConstraints"):
        version = constraints.attrib.get("Version", "")
        if version and versions and version not in versions:
            report.error(f"REX615 SlotConstraints Version={version} 不在版本选项中")

        for housing in constraints.findall("Housing"):
            housing_id = housing.attrib.get("Id", "")
            if housing_codes and housing_id not in housing_codes:
                report.error(f"REX615 SlotConstraints {version} Housing={housing_id} 不在机箱选项中")

            slots = {slot.attrib.get("Id", ""): slot for slot in housing.findall("Slot")}
            for slot_id, slot in slots.items():
                if not slot_id:
                    report.error(f"REX615 SlotConstraints {version}/{housing_id} 存在空槽位 Id")
                try:
                    capacity = int(slot.attrib.get("Capacity", "1"))
                except ValueError:
                    capacity = 0
                if capacity <= 0:
                    report.error(f"REX615 SlotConstraints {version}/{housing_id}/{slot_id} Capacity 无效")
                for module in split_csv(slot.attrib.get("Modules")):
                    if module not in module_options:
                        report.warning(f"REX615 SlotConstraints {version}/{housing_id}/{slot_id} 引用了没有选项的模块 {module}")

            for req in housing.findall("Requirement"):
                req_type = req.attrib.get("Type", "")
                if req_type not in {"AtLeastOne", "SlotMustContain"}:
                    report.error(f"REX615 SlotConstraints {version}/{housing_id} Requirement 类型未知：{req_type}")
                slot = req.attrib.get("Slot")
                if slot and slot not in slots:
                    report.error(f"REX615 SlotConstraints {version}/{housing_id} Requirement 引用未知槽位 {slot}")
                for req_slot in split_csv(req.attrib.get("Slots")):
                    if req_slot not in slots:
                        report.error(f"REX615 SlotConstraints {version}/{housing_id} Requirement 引用未知槽位 {req_slot}")
                for module in split_csv(req.attrib.get("Modules")):
                    if module not in module_options:
                        report.warning(f"REX615 SlotConstraints {version}/{housing_id} Requirement 引用了没有选项的模块 {module}")


def validate_cn(report: Report) -> None:
    data = json.loads(CN_JSON.read_text(encoding="utf-8"))
    series_list = data.get("series", [])
    if len(series_list) != 2:
        report.error(f"CN 规则应包含 2 个系列，当前 {len(series_list)} 个")

    for series in series_list:
        series_id = series.get("id", "")
        devices = series.get("devices", [])
        expected_count = 7 if series_id == "615_CN_5_0_FP1" else 3 if series_id == "620_CN_2_0_FP1" else None
        if expected_count is not None and len(devices) != expected_count:
            report.error(f"{series_id} 装置数量应为 {expected_count}，当前 {len(devices)}")

        for device in devices:
            validate_cn_device(report, series, device)

    validate_cn_import_detection(report, data)
    report.info(f"CN JSON 系列 {len(series_list)} 个，装置 {sum(len(s.get('devices', [])) for s in series_list)} 个。")


def validate_cn_device(report: Report, series: dict, device: dict) -> None:
    series_id = series.get("id", "")
    device_id = device.get("id", "")
    groups = device.get("groups", [])
    group_by_position = {group.get("position", ""): group for group in groups}
    total_len = sum(code_length(group.get("position", "")) for group in groups)
    if total_len != 18:
        report.error(f"{series_id}/{device_id} 位号长度合计应为 18，当前 {total_len}")

    required_positions = (
        {"1", "2", "3", "4", "5-6", "7-8", "9", "10", "11", "12", "13", "14", "15", "16", "17-18"}
        if series_id == "615_CN_5_0_FP1"
        else {"1", "2", "3", "4", "5-6", "7-8", "9-10", "11", "12", "13", "14", "15", "16", "17-18"}
    )
    missing = required_positions - set(group_by_position)
    extra = set(group_by_position) - required_positions
    if missing:
        report.error(f"{series_id}/{device_id} 缺少位号：{','.join(sorted(missing))}")
    if extra:
        report.error(f"{series_id}/{device_id} 存在未知位号：{','.join(sorted(extra))}")

    for group in groups:
        position = group.get("position", "")
        expected_len = code_length(position)
        options = group.get("options", [])
        codes = [option.get("code", "") for option in options]
        if len(codes) != len(set(codes)):
            report.error(f"{series_id}/{device_id}/{position} 存在重复代码")
        if not any(option.get("isDefault") for option in options):
            report.warning(f"{series_id}/{device_id}/{position} 未设置默认代码")
        for code in codes:
            if len(code) != expected_len:
                report.error(f"{series_id}/{device_id}/{position} 代码 {code} 长度应为 {expected_len}")

        for option in options:
            for req in option.get("requiredSelections", []):
                validate_cn_requirement(report, series_id, device_id, group_by_position, position, option.get("code", ""), req)
            for exclusion in option.get("excludedCombinedSelections", []):
                validate_cn_exclusion(report, series_id, device_id, group_by_position, position, option.get("code", ""), exclusion)

    default_code = "".join(
        next((option.get("code", "") for option in group.get("options", []) if option.get("isDefault")), group.get("options", [{}])[0].get("code", ""))
        for group in groups
    )
    if len(default_code) != 18:
        report.error(f"{series_id}/{device_id} 默认订货号长度应为 18，当前 {len(default_code)}：{default_code}")
    if device_id.endswith("620") and default_code[2] != device_id[2]:
        report.error(f"{series_id}/{device_id} 默认订货号主要应用位与型号不一致：{default_code}")


def validate_cn_requirement(
    report: Report,
    series_id: str,
    device_id: str,
    groups: dict[str, dict],
    owner_position: str,
    owner_code: str,
    req: dict,
) -> None:
    position = req.get("position", "")
    if position not in groups:
        report.error(f"{series_id}/{device_id}/{owner_position}/{owner_code} 条件引用未知位号 {position}")
        return
    valid_codes = {option.get("code", "") for option in groups[position].get("options", [])}
    for code in req.get("codes", []):
        if code not in valid_codes:
            report.error(f"{series_id}/{device_id}/{owner_position}/{owner_code} 条件引用 {position} 未知代码 {code}")
    if req.get("mode", "AnyOf") not in {"AnyOf", "NoneOf"}:
        report.error(f"{series_id}/{device_id}/{owner_position}/{owner_code} 条件模式未知：{req.get('mode')}")


def validate_cn_exclusion(
    report: Report,
    series_id: str,
    device_id: str,
    groups: dict[str, dict],
    owner_position: str,
    owner_code: str,
    exclusion: dict,
) -> None:
    positions = exclusion.get("positions", [])
    for position in positions:
        if position not in groups:
            report.error(f"{series_id}/{device_id}/{owner_position}/{owner_code} 排除条件引用未知位号 {position}")
    combined_len = sum(code_length(position) for position in positions)
    for code in exclusion.get("codes", []):
        if len(code) != combined_len:
            report.error(f"{series_id}/{device_id}/{owner_position}/{owner_code} 排除组合 {code} 长度应为 {combined_len}")


def validate_cn_import_detection(report: Report, data: dict) -> None:
    series_by_id = {series.get("id", ""): series for series in data.get("series", [])}
    examples = {
        "HCFCACABNBC2ACN11G": ("615_CN_5_0_FP1", "REF615"),
        "HCDCACADABB2AAN11G": ("615_CN_5_0_FP1", "RED615"),
        "HCMAACABNBA2ABN11G": ("615_CN_5_0_FP1", "REM615"),
        "HCGDBDADNBA2AFN11G": ("615_CN_5_0_FP1", "REG615"),
        "HCTABABANBA2ANN11G": ("615_CN_5_0_FP1", "RET615"),
        "HCUAEAADNBA2ABN11G": ("615_CN_5_0_FP1", "REU615"),
        "HCVBBCADNBA2ANN11G": ("615_CN_5_0_FP1", "REV615"),
        "NBFNAANNABC1BNN11G": ("620_CN_2_0_FP1", "REF620"),
        "NBMNAANNABC1BNN11G": ("620_CN_2_0_FP1", "REM620"),
        "NBTNAANNABC1BNN11G": ("620_CN_2_0_FP1", "RET620"),
    }

    for code, expected in examples.items():
        actual = detect_cn_device(data, code)
        if actual != expected:
            report.error(f"CN 导入识别错误：{code} 期望 {expected}，实际 {actual}")

    for series in data.get("series", []):
        for device in series.get("devices", []):
            default_code = "".join(
                next((option.get("code", "") for option in group.get("options", []) if option.get("isDefault")), group.get("options", [{}])[0].get("code", ""))
                for group in device.get("groups", [])
            )
            actual = detect_cn_device(data, default_code)
            expected = (series.get("id", ""), device.get("id", ""))
            if actual != expected:
                report.error(f"CN 默认订货号识别错误：{default_code} 期望 {expected}，实际 {actual}")


def detect_cn_device(data: dict, code: str) -> tuple[str, str] | None:
    normalized = "".join(ch.upper() for ch in code if ch.isalnum())
    if len(normalized) != 18:
        return None

    first = normalized[0]
    series_id = "615_CN_5_0_FP1" if first in {"H", "1"} else "620_CN_2_0_FP1" if first in {"N", "5"} else ""
    if not series_id:
        return None

    application_code = normalized[2]
    for series in data.get("series", []):
        if series.get("id") != series_id:
            continue
        for device in series.get("devices", []):
            group = next((group for group in device.get("groups", []) if group.get("position") == "3"), None)
            if group and any(option.get("code") == application_code for option in group.get("options", [])):
                return series_id, device.get("id", "")
    return None


def main() -> int:
    report = Report()
    validate_rex615(report)
    validate_cn(report)
    report.print()
    return 1 if report.errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
