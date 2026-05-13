import json
import re
from datetime import datetime, timezone
from pathlib import Path

import pdfplumber


CAT_ZH = {
    "Protection": "保护",
    "Control": "控制",
    "Condition Monitoring and Supervision": "状态监测与监视",
    "Measurement": "测量",
    "Power Quality": "电能质量",
    "Traditional LED indication": "传统 LED 指示",
    "Logging functions": "记录功能",
    "Other functionality": "其他功能",
}

COLUMNS = [
    ("Base", 151.8189),
    ("APP1", 178.7480),
    ("APP2", 205.6772),
    ("APP3", 232.6063),
    ("APP4", 259.5354),
    ("APP5", 286.4646),
    ("APP6", 313.3937),
    ("APP7", 340.3229),
    ("APP8", 367.2520),
    ("APP9", 394.1811),
    ("ADD1", 421.1103),
    ("ADD2", 446.6221),
    ("APP10", 472.1339),
    ("APP11", 496.2284),
    ("APP12", 520.3229),
]

SOURCES = {
    "PCL1": {
        "pdf": "REX615_pg_001867_Enb.pdf",
        "pages": range(157, 167),
    },
    "PCL2": {
        "pdf": "REX615_pg_001867_ENc.pdf",
        "pages": range(165, 175),
    },
}


def normalize_code(value: str) -> str:
    return value.strip().upper().replace("-", "")


def extract_matrix(pdf_path: Path, page_indexes: range, codes: set[str]) -> dict[str, list[str]]:
    matrix: dict[str, list[str]] = {}
    with pdfplumber.open(pdf_path) as pdf:
        for page_index in page_indexes:
            page = pdf.pages[page_index]
            words = page.extract_words(x_tolerance=1, y_tolerance=2, keep_blank_chars=False)

            first_column_words = []
            dots = []
            for word in words:
                text = word["text"].strip()
                if len(text) == 1 and ord(text) == 9679 and 140 <= word["x0"] <= 530:
                    dots.append(word)
                if (
                    54 <= word["x0"] <= 96
                    and 120 <= word["top"] <= 760
                    and re.match(r"^[A-Za-z0-9_\-]+$", text)
                ):
                    first_column_words.append(word)

            first_column_words.sort(key=lambda item: item["top"])
            index = 0
            while index < len(first_column_words):
                code, consumed = resolve_wrapped_code(first_column_words, index, codes)
                if code is not None:
                    row_top = first_column_words[index]["top"]
                    columns = columns_for_row(dots, row_top)
                    if columns:
                        matrix[code] = columns
                index += consumed
    return matrix


def resolve_wrapped_code(words: list[dict], index: int, codes: set[str]) -> tuple[str | None, int]:
    text = normalize_code(words[index]["text"])
    if text in codes:
        return text, 1

    for count in (2, 3):
        if index + count - 1 >= len(words):
            continue
        parts = [words[index]["text"]]
        is_wrapped = True
        for offset in range(1, count):
            current = words[index + offset]
            previous = words[index + offset - 1]
            if abs(current["x0"] - words[index]["x0"]) > 2 or current["top"] - previous["top"] > 13:
                is_wrapped = False
                break
            parts.append(current["text"])
        candidate = normalize_code("".join(parts))
        if is_wrapped and candidate in codes:
            return candidate, count

    return None, 1


def columns_for_row(dots: list[dict], row_top: float) -> list[str]:
    columns: list[str] = []
    for dot in dots:
        if abs(dot["top"] - row_top) > 3.5:
            continue
        name, x_position = min(COLUMNS, key=lambda column: abs(column[1] - dot["x0"]))
        if abs(x_position - dot["x0"]) <= 4 and name not in columns:
            columns.append(name)
    return columns


def summary_for(function: dict, version: str) -> str:
    zh_name = function.get("ChineseName") or function.get("EnglishName") or function.get("Code", "")
    category = CAT_ZH.get(function.get("Category", ""), function.get("Category", ""))
    apps = function.get("Apps", [])

    if function.get("IsBase") and not apps:
        provided_by = f"在 {version} 中作为基础功能提供"
    elif function.get("IsBase"):
        provided_by = f"在 {version} 中作为基础功能提供，也可随 {', '.join(apps)} 应用包出现"
    else:
        provided_by = f"在 {version} 中需通过 {', '.join(apps)} 应用包提供"

    return f"功能说明：{zh_name}。该功能属于{category}，{provided_by}。"


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    catalog_path = root / "AbbRelaysOfflineConfigurator" / "Data" / "AppFunctionCatalog.json"
    data = json.loads(catalog_path.read_text(encoding="utf-8-sig"))

    total_changed = 0
    for version_catalog in data["Versions"]:
        version = version_catalog["Version"]
        source = SOURCES[version]
        codes = {function["Code"] for function in version_catalog["Functions"]}
        matrix = extract_matrix(root / source["pdf"], source["pages"], codes)
        missing = sorted(codes - set(matrix))
        if missing:
            raise RuntimeError(f"{version} matrix missing {len(missing)} functions: {', '.join(missing)}")

        changed = 0
        for function in version_catalog["Functions"]:
            columns = matrix[function["Code"]]
            new_is_base = "Base" in columns
            new_apps = [column for column in columns if column != "Base"]
            old_is_base = function.get("IsBase")
            old_apps = function.get("Apps", [])
            old_state = (old_is_base, tuple(old_apps))
            new_state = (new_is_base, tuple(new_apps))
            if old_state == new_state:
                continue

            function["IsBase"] = new_is_base
            function["Apps"] = new_apps
            if old_is_base != new_is_base or set(old_apps) != set(new_apps):
                function["PrincipleSummary"] = summary_for(function, version)
            changed += 1

        total_changed += changed
        print(f"{version}: extracted {len(matrix)} rows, changed {changed} functions")

    if total_changed > 0:
        data["GeneratedAt"] = datetime.now(timezone.utc).isoformat()
        catalog_path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Updated {total_changed} function matrix entries.")


if __name__ == "__main__":
    main()
