#!/usr/bin/env python3
"""Import Legion 12 traits and professions from the maintained XLSX sheet.

Only the first worksheet is read. Expected columns are:
名称, 特征_1, 特征_2, 职介.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from zipfile import ZipFile
import xml.etree.ElementTree as ET


NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}


def column_index(reference: str) -> int:
    letters = "".join(character for character in reference if character.isalpha())
    value = 0
    for character in letters.upper():
        value = value * 26 + ord(character) - ord("A") + 1
    return value - 1


def read_first_sheet(path: Path) -> list[list[str | None]]:
    with ZipFile(path) as archive:
        shared: list[str] = []
        if "xl/sharedStrings.xml" in archive.namelist():
            root = ET.fromstring(archive.read("xl/sharedStrings.xml"))
            for entry in root.findall("m:si", NS):
                shared.append("".join(node.text or "" for node in entry.findall(".//m:t", NS)))

        workbook = ET.fromstring(archive.read("xl/workbook.xml"))
        relationships = ET.fromstring(archive.read("xl/_rels/workbook.xml.rels"))
        relationship_targets = {
            relation.attrib["Id"]: relation.attrib["Target"]
            for relation in relationships
        }
        first_sheet = workbook.find("m:sheets/m:sheet", NS)
        if first_sheet is None:
            raise ValueError("工作簿没有工作表")
        relation_id = first_sheet.attrib["{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id"]
        target = relationship_targets[relation_id].lstrip("/")
        if not target.startswith("xl/"):
            target = f"xl/{target}"
        sheet = ET.fromstring(archive.read(target))

        rows: list[list[str | None]] = []
        for row_node in sheet.findall("m:sheetData/m:row", NS):
            values: list[str | None] = [None, None, None, None]
            for cell in row_node.findall("m:c", NS):
                index = column_index(cell.attrib.get("r", "A1"))
                if index >= len(values):
                    continue
                value_node = cell.find("m:v", NS)
                if value_node is None:
                    inline = cell.find("m:is/m:t", NS)
                    values[index] = inline.text if inline is not None else None
                elif cell.attrib.get("t") == "s":
                    values[index] = shared[int(value_node.text or "0")]
                else:
                    values[index] = value_node.text
            rows.append(values)
        return rows


def load_metadata(path: Path) -> dict[str, tuple[list[str], str | None]]:
    rows = read_first_sheet(path)
    if not rows or rows[0] != ["名称", "特征_1", "特征_2", "职介"]:
        raise ValueError(f"表头不符合预期：{rows[0] if rows else '空表'}")
    result: dict[str, tuple[list[str], str | None]] = {}
    for name, first_trait, second_trait, profession in rows[1:]:
        if not name:
            continue
        traits = [value for value in (first_trait, second_trait) if value]
        if name in result:
            raise ValueError(f"工作簿存在重复名称：{name}")
        result[name] = (traits, profession)
    return result


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def update_catalog(path: Path, metadata: dict[str, tuple[list[str], str | None]]) -> set[str]:
    cards = json.loads(path.read_text(encoding="utf-8"))
    matched: set[str] = set()
    for card in cards:
        values = metadata.get(card["nameZh"])
        if values is None:
            continue
        traits, profession = values
        card["traits"] = traits
        card["profession"] = profession
        matched.add(card["nameZh"])
    write_json(path, cards)
    return matched


def update_lookup(path: Path, metadata: dict[str, tuple[list[str], str | None]]) -> set[str]:
    cards = json.loads(path.read_text(encoding="utf-8"))
    matched: set[str] = set()
    for card in cards:
        values = metadata.get(card["name"])
        if values is None:
            continue
        traits, profession = values
        card["tags"] = traits
        card["subType"] = profession or ""
        search_parts = [card["name"], card.get("faction", ""), card.get("type", ""), *traits]
        if profession:
            search_parts.append(profession)
        if card.get("effectText"):
            search_parts.append(card["effectText"])
        card["searchText"] = " ".join(part for part in search_parts if part)
        matched.add(card["name"])
    write_json(path, cards)
    return matched


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("workbook", type=Path)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    metadata = load_metadata(args.workbook)
    root = args.root.resolve()

    server_s1 = root / "服务端WebSocket" / "TwelveLegions" / "Data" / "cards.s1.json"
    server_s2 = root / "服务端WebSocket" / "TwelveLegions" / "Data" / "cards.s2.json"
    web_s1 = root / "opcgpro-vue" / "public" / "data" / "l12" / "cards.s1.json"
    lookup = root / "opcgpro-vue" / "public" / "data" / "l12" / "cards.lookup.json"

    matched = set()
    matched |= update_catalog(server_s1, metadata)
    matched |= update_catalog(server_s2, metadata)
    update_catalog(web_s1, metadata)
    update_lookup(lookup, metadata)

    missing = sorted(set(metadata) - matched)
    if missing:
        raise SystemExit(f"以下名称未匹配服务端卡牌：{', '.join(missing)}")
    print(f"已同步 {len(metadata)} 张卡牌的特征与职介。")


if __name__ == "__main__":
    main()
