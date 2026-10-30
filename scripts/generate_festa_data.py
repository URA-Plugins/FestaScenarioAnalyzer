"""Generate Festa display data from the JP master database without modifying it.

Usage: python scripts/generate_festa_data.py --master <master.mdb> [--check]
--check performs the same source checks and compares outputs without writing.
Only Python's standard library is required.
"""

import argparse
import hashlib
import json
import re
import sqlite3
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path


TABLES = (
    "single_mode_15_act_info",
    "single_mode_15_advice",
    "single_mode_15_advice_effect",
    "single_mode_15_fusion_effect",
    "single_mode_15_act_effect",
    "single_mode_15_schedule",
)
TEXT_CATEGORIES = (458, 461, 462, 463, 466)
ACT_NAMES = {1: "流镝马", 2: "神旗争夺战", 3: "纳舞", 4: "大曳神舆", 5: "飞驱"}
ADVICE_NAMES = {
    101: "羁绊教诲", 201: "得意率教诲", 301: "提示教诲", 401: "助力教诲", 501: "体力教诲",
    601: "专精教诲", 701: "研钻教诲", 801: "协力教诲", 901: "不屈教诲", 1001: "两道教诲",
}
ERRORS = {
    "UnknownAct": "未知的骏大祭演目编号。",
    "UnknownAdvice": "未知的骏大祭教诲编号。",
    "UnknownFusion": "未知的骏大祭转成来源教诲编号。",
    "UnknownLevel": "演目等级必须在 1–10 范围内。",
}


def plain(text):
    return re.sub(r"</?color(?:=[^>]*)?>", "", text).replace("\\n", "\n")


def value(rows, effect_type):
    matches = [row for row in rows if row["effect_type"] == effect_type]
    assert len(matches) == 1, (effect_type, rows)
    return matches[0]["effect_value_4"]


def advice_text(advice_id, rows):
    kinds = {row["effect_type"] for row in rows}
    expected = {
        101: {16, 13}, 201: {4}, 301: {2}, 401: {203, 20}, 501: {3},
        601: {13, 206}, 701: {8}, 801: {202, 18}, 1001: {21, 203},
    }
    if advice_id != 901:
        assert kinds == expected[advice_id], (advice_id, kinds)
    if advice_id == 101:
        assert value(rows, 16) == 3
        return f"训练时全部编成支援卡的羁绊 +3（不叠加）\n演目稽古的主属性增长 +{value(rows, 13)}%"
    if advice_id == 201:
        return f"得意率 +{value(rows, 4)}"
    if advice_id == 301:
        return f"提示发生率 +{value(rows, 2)}%"
    if advice_id == 401:
        assert value(rows, 20) == 47
        return f"未发生友情训练时，训练效果 +{value(rows, 203)}%\n演目稽古获得“助力支撑”（训练效果 +5%，最多生效 3 次）"
    if advice_id == 501:
        return f"体力消耗减少 {abs(value(rows, 3))}%"
    if advice_id == 601:
        return f"主属性增长 +{value(rows, 13)}%\n基础属性单次获取上限 +{value(rows, 206)}"
    if advice_id == 701:
        return f"演目担当参加的训练中，“追加稽古”事件发生率 {value(rows, 8)}%"
    if advice_id == 801:
        assert value(rows, 18) == 1
        return f"友情加成 {value(rows, 202)}%\n该演目的编成支援卡额外出现在随机 1 处训练中"
    if advice_id == 901:
        assert kinds in ({205}, {205, 203})
        failure = next(row for row in rows if row["effect_type"] == 205)
        assert failure["condition_type"] == 2 and value(rows, 205) == 100
        threshold = failure["condition_value"]
        text = (f"体力至少 {threshold} 时，" if threshold else "") + "失败率降低 100%"
        if 203 in kinds:
            text += f"\n演目稽古的训练效果 +{value(rows, 203)}%"
        return text
    if advice_id == 1001:
        return f"副属性增长 +{value(rows, 21)}%\n演目稽古的训练效果 +{value(rows, 203)}%"
    raise ValueError(f"Unknown advice ID: {advice_id}")


def fusion_text(advice_id, rows):
    expected = {101: {13, 17}, 201: {4, 202}, 301: {2, 19}, 401: {203, 20}, 501: {3}}
    assert {row["effect_type"] for row in rows} == expected[advice_id]
    if advice_id == 101:
        return f"演目稽古的主属性增长 +{value(rows, 13)}%\n合宿中，该演目的编成支援卡额外有 {value(rows, 17)} 人参加训练"
    if advice_id == 201:
        return f"得意率 +{value(rows, 4)}\n演目稽古的友情加成 {value(rows, 202)}%"
    if advice_id == 301:
        return f"提示发生率 +{value(rows, 2)}%\n演目稽古的提示事件所获提示等级 +{value(rows, 19)}"
    if advice_id == 401:
        assert value(rows, 20) == 47
        return f"未发生友情训练时，训练效果 +{value(rows, 203)}%\n演目稽古获得“助力支撑”（训练效果 +5%，最多生效 3 次）"
    if advice_id == 501:
        return f"演目稽古的体力消耗减少 {abs(value(rows, 3))}%"
    raise ValueError(f"Unknown fusion source ID: {advice_id}")


def act_text(row):
    amount = row["effect_value_4"]
    texts = {
        0: "对应演目担当参加演目稽古",
        105: f"基础属性增长 +{amount}",
        202: f"友情加成 {amount}%",
        201: f"技能点训练效果 +{amount}%",
        106: "除友人、团队卡外，全部编成支援卡出现提示事件",
        107: "所选训练的全部提示事件效果生效",
        109: f"技能点单次获取上限 +{amount}",
        206: f"基础属性单次获取上限 +{amount}",
    }
    assert (row["condition_type"], row["condition_value"]) in ((0, 0), (1, 5))
    prefix = "支援卡类型至少 5 种：" if row["condition_type"] == 1 else ""
    return prefix + texts[row["effect_type"]]


def resx(entries):
    root = ET.Element("root")
    for name, text in (
        ("resmimetype", "text/microsoft-resx"), ("version", "2.0"),
        ("reader", "System.Resources.ResXResourceReader, System.Windows.Forms"),
        ("writer", "System.Resources.ResXResourceWriter, System.Windows.Forms"),
    ):
        ET.SubElement(ET.SubElement(root, "resheader", {"name": name}), "value").text = text
    for key, text in entries.items():
        data = ET.SubElement(root, "data", {"name": key, "{http://www.w3.org/XML/1998/namespace}space": "preserve"})
        ET.SubElement(data, "value").text = text
    ET.indent(root, space="  ")
    return ET.tostring(root, encoding="utf-8", xml_declaration=True) + b"\n"


def json_bytes(data):
    return (json.dumps(data, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def generate(master):
    with sqlite3.connect(master.as_uri() + "?mode=ro", uri=True) as connection:
        connection.row_factory = sqlite3.Row
        tables = {name: [dict(row) for row in connection.execute(f'SELECT * FROM "{name}" ORDER BY 1')] for name in TABLES}
        texts = {
            category: {row["index"]: row["text"] for row in connection.execute(
                'SELECT "index", text FROM text_data WHERE category = ? ORDER BY "index"', (category,))}
            for category in TEXT_CATEGORIES
        }
        for category in (142, 143):
            texts[category] = {row["index"]: row["text"] for row in connection.execute(
                'SELECT "index", text FROM text_data WHERE category = ? AND "index" = 47', (category,))}
    assert plain(texts[142][47]) == "助力の支え"
    assert plain(texts[143][47]) == "助力の教えを持つウマ娘から得られる不思議な力\n3回までトレーニング効果がわずかに上がる"
    advices = tables["single_mode_15_advice"]
    acts = tables["single_mode_15_act_info"]
    schedules = tables["single_mode_15_schedule"]
    assert {row["id"] for row in advices} == set(ADVICE_NAMES)
    assert {row["act_type"] for row in acts} == set(ACT_NAMES)
    assert all(row["need_act_num"] == 5 for row in schedules)
    assert [(row["turn_num"], row["need_act_level"]) for row in schedules] == [(24, 3), (48, 5), (68, 6)]

    chinese, japanese = dict(ERRORS), {}
    for act_id, name in ACT_NAMES.items():
        chinese[f"Act_{act_id}"] = name
        japanese[f"Act_{act_id}"] = plain(texts[462][act_id])
    for advice_id, name in ADVICE_NAMES.items():
        chinese[f"Advice_{advice_id}"] = name
        japanese[f"Advice_{advice_id}"] = plain(texts[463][advice_id])

    advice_groups = defaultdict(list)
    for row in tables["single_mode_15_advice_effect"]:
        advice_groups[(row["advice_id"], row["level"])].append(row)
    assert set(advice_groups) == {(advice_id, level) for advice_id in ADVICE_NAMES for level in range(1, 11)}
    advice_ids = defaultdict(list)
    for (advice_id, level), rows in sorted(advice_groups.items()):
        text_ids = [row["id"] for row in rows if row["id"] in texts[461]]
        assert text_ids == [min(row["id"] for row in rows)]
        text_id = text_ids[0]
        advice_ids[advice_id].append(text_id)
        chinese[f"AdviceEffect_{text_id}"] = advice_text(advice_id, rows)
        japanese[f"AdviceEffect_{text_id}"] = plain(texts[461][text_id])
        if advice_id == 401:
            assert "3回までトレーニング効果アップ 5%" in japanese[f"AdviceEffect_{text_id}"]

    fusion_groups = defaultdict(list)
    for row in tables["single_mode_15_fusion_effect"]:
        fusion_groups[row["advice_id"]].append(row)
    assert set(fusion_groups) == {101, 201, 301, 401, 501}
    fusion_ids = {}
    for advice_id, rows in sorted(fusion_groups.items()):
        text_ids = [row["id"] for row in rows if row["id"] in texts[466]]
        assert text_ids == [min(row["id"] for row in rows)]
        text_id = text_ids[0]
        fusion_ids[advice_id] = text_id
        chinese[f"FusionEffect_{text_id}"] = fusion_text(advice_id, rows)
        japanese[f"FusionEffect_{text_id}"] = plain(texts[466][text_id])
        if advice_id == 401:
            assert "3回までトレーニング効果アップ 5%" in japanese[f"FusionEffect_{text_id}"]

    act_effects = tables["single_mode_15_act_effect"]
    assert set(texts[458]) == {row["id"] for row in act_effects}
    for row in act_effects:
        key = f'ActEffect_{row["id"]}'
        chinese[key] = act_text(row)
        japanese[key] = plain(texts[458][row["id"]])
    assert not any("<color" in text or "\\n" in text for text in japanese.values())
    assert set(chinese) - set(ERRORS) == set(japanese)
    for key, original in japanese.items():
        if "Effect_" in key:
            assert sorted(re.findall(r"\d+", original)) == sorted(re.findall(r"\d+", chinese[key])), key

    lines = [
        "// <auto-generated> Generated by scripts/generate_festa_data.py. Do not edit by hand.",
        "using System.Collections.Frozen;", "", "namespace FestaScenarioAnalyzer;", "",
        "internal static partial class FestaMasterData", "{",
        "    public static IReadOnlyList<FestaAdviceDefinition> Advices { get; } = Array.AsReadOnly<FestaAdviceDefinition>(",
        "    [",
    ]
    lines += [f'        new({row["id"]}, {row["add_schedule_type"]}),' for row in advices]
    lines += ["    ]);", "", "    public static IReadOnlyList<FestaSchedule> Schedules { get; } = Array.AsReadOnly<FestaSchedule>(", "    ["]
    lines += [f'        new({row["id"]}, {row["turn_num"]}, {row["need_act_level"]}),' for row in schedules]
    lines += ["    ]);", ""]
    for field, ids, prefix in [("ActNameKeys", ACT_NAMES, "Act"), ("AdviceNameKeys", ADVICE_NAMES, "Advice")]:
        lines += [f"    static readonly FrozenDictionary<int, string> {field} = new Dictionary<int, string>", "    {"]
        lines += [f'        [{item_id}] = "{prefix}_{item_id}",' for item_id in ids]
        lines += ["    }.ToFrozenDictionary();", ""]
    lines += ["    static readonly FrozenDictionary<int, int[]> AdviceEffectIds = new Dictionary<int, int[]>", "    {"]
    lines += [f'        [{advice_id}] = [{", ".join(map(str, ids))}],' for advice_id, ids in advice_ids.items()]
    lines += ["    }.ToFrozenDictionary();", "", "    static readonly FrozenDictionary<int, int> FusionEffectIds = new Dictionary<int, int>", "    {"]
    lines += [f"        [{advice_id}] = {text_id}," for advice_id, text_id in fusion_ids.items()]
    lines += ["    }.ToFrozenDictionary();", "", "    static readonly (int Id, int Level, bool RequiresFiveTypes)[] ActEffectEntries =", "    ["]
    lines += [f'        ({row["id"]}, {row["level"]}, {str(row["condition_type"] == 1).lower()}),' for row in act_effects]
    lines += ["    ];", "}", ""]

    with master.open("rb") as stream:
        source_hash = hashlib.file_digest(stream, "sha256").hexdigest()
    provenance = {
        "source": "Japanese game master.mdb",
        "sourcePath": master.as_posix(),
        "sha256": source_hash,
        "tables": list(TABLES),
        "textCategories": {
            "142": "index 47 -> assistance condition name referenced by effect_type 20",
            "143": "index 47 -> assistance condition description (three training applications, not three acquisition stacks)",
            "458": "act_effect.id -> effect description",
            "461": "minimum advice_effect.id per (advice_id, level) -> complete advice description",
            "462": "act_type -> act name",
            "463": "advice_id -> advice name",
            "466": "minimum fusion_effect.id per advice_id -> complete fusion description",
        },
        "generator": "scripts/generate_festa_data.py",
        "translation": "Simplified Chinese descriptions preserve the conditions and values in the Japanese source text.",
    }
    return {
        "Data/FestaMasterData.Generated.cs": "\n".join(lines).encode("utf-8"),
        "Data/master-data.json": json_bytes({"tables": tables, "texts": texts}),
        "Data/source.json": json_bytes(provenance),
        "i18n/Data.resx": resx(chinese),
        "i18n/Data.ja-JP.resx": resx(japanese),
    }, (len(advice_groups), len(fusion_groups), len(act_effects))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--master", required=True, type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    outputs, counts = generate(args.master.resolve(strict=True))
    for relative, content in outputs.items():
        path = root / relative
        if args.check:
            if not path.is_file() or path.read_bytes() != content:
                raise ValueError(f"Generated file differs: {relative}")
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
    print(f'{"Checked" if args.check else "Generated"} {len(outputs)} files; {counts[0]} advice-level groups, {counts[1]} fusion groups, {counts[2]} act effects; all source descriptions matched.')


if __name__ == "__main__":
    main()
