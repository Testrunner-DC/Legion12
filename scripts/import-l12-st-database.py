#!/usr/bin/env python3
"""Import the ST01-ST06 product workbook into the L12 authoritative catalogs.

The reader intentionally uses only Python's standard library so the import can be
repeated on a clean developer machine without installing an Excel package.
"""

from __future__ import annotations

import argparse
import json
import re
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET


NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
REL_NS = {"r": "http://schemas.openxmlformats.org/package/2006/relationships"}

TYPE_MAP = {
    "军团": "legion",
    "主动战术": "tactic",
    "反击战术": "counter-tactic",
    "战术": "tactic",
    "圣物": "artifact",
    "主宰": "master",
    "试炼": "trial",
    "天灾": "destruction",
    "士气": "rune",
}

FACTION_MAP = {
    "天廷": "tianting",
    "太阳城": "taiyangcheng",
    "阿斯加德": "asgard",
    "高天原": "gaotianyuan",
    "奥林匹斯": "olympus",
    "彼界": "otherworld",
}

MORALE_CARDS = [
    {
        "id": "ST01-C1", "number": "ST01-C1", "nameZh": "士气·天廷", "cardType": "rune",
        "product": "ST01", "faction": "tianting", "traits": ["天廷"],
        "effect": "我方 回合1次 可消耗2士气：从士气牌库追加1张活跃的士气。\n我方 回合1次 我方士气为0张时，可从士气牌库追加2张休整的士气。",
        "atomicReference": "Ability 1\n我方 回合1次 可消耗2士气：从士气牌库追加1张活跃的士气。\nAbility 2\n我方 回合1次 我方士气为0张时，可从士气牌库追加2张休整的士气。",
    },
    {
        "id": "ST02-C1", "number": "ST02-C1", "nameZh": "士气·太阳城", "cardType": "rune",
        "product": "ST02", "faction": "taiyangcheng", "traits": ["太阳城"],
        "effect": "我方 回合1次 可消耗2士气：将1张<陵墓守卫>从我方墓地活跃登场。\n我方 回合1次 若我方手牌不高于3张，可消耗1士气：抽取1张牌。",
        "atomicReference": "Ability 1\n我方 回合1次 可消耗2士气：将1张<陵墓守卫>从我方墓地活跃登场。\nAbility 2\n我方 回合1次 若我方手牌不高于3张，可消耗1士气：抽取1张牌。",
    },
    {
        "id": "ST03-C1", "number": "ST03-C1", "nameZh": "士气·阿斯加德", "cardType": "rune",
        "product": "ST03", "faction": "asgard", "traits": ["阿斯加德"],
        "effect": "我方 回合1次 可消耗2士气：抽取1张牌；若我方主宰血量不高于5，可额外消耗1士气，我方主宰增加1点血量。",
        "atomicReference": "Ability 1\n我方 回合1次 可消耗2士气：抽取1张牌；若我方主宰血量不高于5，可额外消耗1士气，我方主宰增加1点血量。",
    },
    {
        "id": "ST04-C1", "number": "ST04-C1", "nameZh": "士气·高天原", "cardType": "rune",
        "product": "ST04", "faction": "gaotianyuan", "traits": ["高天原"],
        "effect": "我方 回合1次 可消耗2士气：抽取1张牌。随后可选择我方1张活跃的军团进行1格位移。",
        "atomicReference": "Ability 1\n我方 回合1次 可消耗2士气：抽取1张牌。随后可选择我方1张活跃的军团进行1格位移。",
    },
    {
        "id": "ST05-C1", "number": "ST05-C1", "nameZh": "士气·奥林匹斯", "cardType": "rune",
        "product": "ST05", "faction": "olympus", "traits": ["奥林匹斯"],
        "effect": "我方 回合1次 可消耗1士气：翻转1张士气。",
        "atomicReference": "Ability 1\n我方 回合1次 可消耗1士气：翻转1张士气。",
    },
    {
        "id": "ST06-C1", "number": "ST06-C1", "nameZh": "士气·彼界", "cardType": "rune",
        "product": "ST06", "faction": "otherworld", "traits": ["彼界"],
        "effect": "我方 回合1次 可消耗2士气：获得1符文。",
        "atomicReference": "Ability 1\n我方 回合1次 可消耗2士气：获得1符文。",
    },
]

ATOMIC_REFERENCE_OVERRIDES = {
    "ST-DS01": "Ability 1\n触发 将所有前排兵力不高于4000的军团置入所有者墓地。",
    "ST-DS02": "Ability 1\n持续 带有天灾等级的军团兵力+1000，且发动进攻需要弃置1张手牌。",
    "ST-DS03": "Ability 1\n触发 双方弃置各自战场上1张军团。",
}


def column_index(reference: str) -> int:
    letters = re.match(r"[A-Z]+", reference).group(0)
    value = 0
    for letter in letters:
        value = value * 26 + ord(letter) - 64
    return value - 1


def workbook_rows(path: Path) -> list[list[object | None]]:
    with zipfile.ZipFile(path) as archive:
        shared = []
        if "xl/sharedStrings.xml" in archive.namelist():
            root = ET.fromstring(archive.read("xl/sharedStrings.xml"))
            for item in root.findall("m:si", NS):
                shared.append("".join(node.text or "" for node in item.findall(".//m:t", NS)))

        book = ET.fromstring(archive.read("xl/workbook.xml"))
        first = book.find("m:sheets/m:sheet", NS)
        relationship_id = first.attrib["{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id"]
        relationships = ET.fromstring(archive.read("xl/_rels/workbook.xml.rels"))
        target = next(rel.attrib["Target"] for rel in relationships.findall("r:Relationship", REL_NS)
                      if rel.attrib["Id"] == relationship_id)
        sheet_name = target.lstrip("/") if target.startswith("xl/") else f"xl/{target.lstrip('/')}"
        sheet = ET.fromstring(archive.read(sheet_name))

        result: list[list[object | None]] = []
        for row in sheet.findall(".//m:sheetData/m:row", NS):
            values: dict[int, object | None] = {}
            for cell in row.findall("m:c", NS):
                index = column_index(cell.attrib["r"])
                cell_type = cell.attrib.get("t")
                value_node = cell.find("m:v", NS)
                if cell_type == "inlineStr":
                    value = "".join(node.text or "" for node in cell.findall(".//m:t", NS))
                elif value_node is None:
                    value = None
                elif cell_type == "s":
                    value = shared[int(value_node.text or "0")]
                else:
                    raw = value_node.text or ""
                    try:
                        number = float(raw)
                        value = int(number) if number.is_integer() else number
                    except ValueError:
                        value = raw
                values[index] = value
            width = max(values, default=-1) + 1
            result.append([values.get(index) for index in range(width)])
        return result


def normalized_text(value: object | None) -> str:
    return str(value or "").replace("\r\n", "\n").replace("\r", "\n").strip()


def build_card(headers: list[str], raw: list[object | None]) -> dict[str, object]:
    row = {header: raw[index] if index < len(raw) else None for index, header in enumerate(headers)}
    card_id = normalized_text(row["编号"]).upper()
    source_type = normalized_text(row["卡牌种类"])
    if source_type not in TYPE_MAP:
        raise ValueError(f"{card_id}: 未知卡牌种类 {source_type!r}")
    trait_text = normalized_text(row["阵营/词条"])
    traits = [part.strip() for part in trait_text.split("/") if part.strip()]
    faction = "universal" if source_type == "天灾" else FACTION_MAP.get(traits[0] if traits else "", "universal")
    product = normalized_text(row["产品"])
    card: dict[str, object] = {
        "id": card_id,
        "number": card_id,
        "nameZh": normalized_text(row["名称"]),
        "cardType": TYPE_MAP[source_type],
        "product": product,
        "faction": faction,
    }
    stat = row["血量/兵力"]
    if source_type == "主宰" and stat is not None:
        card["hp"] = int(stat)
    elif source_type == "军团" and stat is not None:
        card["troops"] = int(stat)
    for source, target in (("费用", "cost"), ("天灾等级", "disasterLevel")):
        if row[source] is not None and normalized_text(row[source]):
            card[target] = int(row[source])
    profession = normalized_text(row["职介"])
    if profession:
        card["profession"] = profession
    if traits:
        card["traits"] = traits
    effect = normalized_text(row["效果文本"])
    if effect:
        card["effect"] = effect
        # 试炼军团把卡面独立行“试炼 N”同时写入结构化数值。该转换仅发生在
        # 权威数据库导入阶段，实战不得再从可变 EffectText 推断规则数值。
        trial_match = re.search(r"(?m)^试炼\s*(\d+)\s*$", effect)
        if source_type == "军团" and trial_match:
            card["trialValue"] = int(trial_match.group(1))
    atomic_reference = normalized_text(row["原子化参考"])
    if atomic_reference:
        card["atomicReference"] = atomic_reference
    elif card_id in ATOMIC_REFERENCE_OVERRIDES:
        # ST天灾没有单独维护的人工原子列时，仍以数据库效果原文建立稳定能力边界。
        # 仅补结构，不从可变展示文本推断实战规则。
        card["atomicReference"] = ATOMIC_REFERENCE_OVERRIDES[card_id]
    return card


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("database", type=Path)
    parser.add_argument("--server-output", type=Path, required=True)
    parser.add_argument("--web-output", type=Path, required=True)
    args = parser.parse_args()

    rows = workbook_rows(args.database)
    headers = [normalized_text(value) for value in rows[0]]
    expected = ["编号", "名称", "卡牌种类", "产品", "阵营/词条", "职介", "血量/兵力", "费用", "天灾等级", "效果文本", "原子化参考"]
    if headers[: len(expected)] != expected:
        raise ValueError(f"数据库列结构不匹配：{headers}")
    cards = [build_card(headers, row) for row in rows[1:] if row and normalized_text(row[0])]
    cards.extend(MORALE_CARDS)
    ids = [card["id"] for card in cards]
    if len(cards) != 76 or len(set(ids)) != 76:
        raise ValueError(f"ST 产品必须生成 76 张唯一卡牌，当前 total={len(cards)} unique={len(set(ids))}")
    authoritative = {"ST03-01", "ST04-10", "ST05-09", "ST06-01"}
    if not authoritative.issubset(ids):
        raise ValueError("数据库优先卡牌缺失")
    trial_values = {card["id"]: card.get("trialValue") for card in cards if "trialValue" in card}
    if trial_values != {"ST06-06": 1, "ST06-07": 1, "ST06-08": 2}:
        raise ValueError(f"ST 试炼值导入不完整：{trial_values}")

    content = json.dumps(cards, ensure_ascii=False, indent=2) + "\n"
    for output in (args.server_output, args.web_output):
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(content, encoding="utf-8")
    print(f"ST catalog written: 76 cards -> {args.server_output} / {args.web_output}")


if __name__ == "__main__":
    main()
