#!/usr/bin/env python3
"""Synchronize the confirmed S2 legion disaster levels across both catalogs."""

from __future__ import annotations

import json
from pathlib import Path


DISASTER_LEVELS = {
    "S02-0004": 1,
    "S02-0101": 3,
    "S02-0102": 2,
    "S02-0202": 2,
    "S02-0302": 3,
    "S02-0303": 2,
    "S02-0401": 3,
    "S02-0402": 1,
    "S02-0501": 3,
    "S02-0503": 3,
    "S02-0505": 2,
    "S02-0509": 2,
    "S02-0510": 2,
    "S02-0511": 1,
    "S02-0601": 2,
    "S02-0602": 3,
    "S02-0603": 1,
    "S02-0605": 2,
    "S02-0607": 2,
    "S02-0608": 3,
    "S02-0611": 1,
    "S02-0612": 1,
    "S02-0613": 2,
}

NAME_CORRECTIONS = {"S02-0511": "珀洛特埃"}


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def synchronize(root: Path) -> None:
    server_path = root / "服务端WebSocket" / "TwelveLegions" / "Data" / "cards.s2.json"
    lookup_path = root / "opcgpro-vue" / "public" / "data" / "l12" / "cards.lookup.json"

    cards = json.loads(server_path.read_text(encoding="utf-8"))
    server_by_id = {card["id"]: card for card in cards}
    missing_server = sorted(set(DISASTER_LEVELS) - set(server_by_id))
    if missing_server:
        raise ValueError(f"服务端第二季卡表缺少卡号：{', '.join(missing_server)}")
    for card_id, level in DISASTER_LEVELS.items():
        card = server_by_id[card_id]
        if card.get("cardType") != "legion":
            raise ValueError(f"{card_id} 不是军团，不能登记军团天灾等级")
        card["disasterLevel"] = level
        if card_id in NAME_CORRECTIONS:
            card["nameZh"] = NAME_CORRECTIONS[card_id]
    write_json(server_path, cards)

    lookup = json.loads(lookup_path.read_text(encoding="utf-8"))
    lookup_by_id = {card.get("cardNo"): card for card in lookup}
    missing_lookup = sorted(set(DISASTER_LEVELS) - set(lookup_by_id))
    if missing_lookup:
        raise ValueError(f"前端资料库缺少卡号：{', '.join(missing_lookup)}")
    for card_id, level in DISASTER_LEVELS.items():
        card = lookup_by_id[card_id]
        card["disasterLevel"] = level
        if card_id in NAME_CORRECTIONS:
            old_name = card.get("name", "")
            new_name = NAME_CORRECTIONS[card_id]
            card["name"] = new_name
            if isinstance(card.get("searchText"), str):
                card["searchText"] = card["searchText"].replace(old_name, new_name)
    write_json(lookup_path, lookup)


if __name__ == "__main__":
    synchronize(Path(__file__).resolve().parents[1])
    print(f"已同步 {len(DISASTER_LEVELS)} 张第二季军团的天灾等级。")
