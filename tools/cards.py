#!/usr/bin/env python3
"""Card authoring CLI. The JSON files in Assets/GameData/cards are the source of
truth for card data; this tool creates/edits/validates them. After editing, run
"Cards > Pipeline > Import All" inside Unity to bake JSON into game assets.

Examples:
    python tools/cards.py new guy goblin_02 --name "Big Goblin" --attack 3 --health 2
    python tools/cards.py set goblin_02 goldCost=4 description="Angrier."
    python tools/cards.py batch --filter rarity=Common type=guy --set baseHealth+=1
    python tools/cards.py list --filter type=guy
    python tools/cards.py validate
    python tools/cards.py rename goblin_02 goblin_big_01
"""
import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CARDS_DIR = ROOT / "Assets" / "GameData" / "cards"
ART_DIR = ROOT / "Assets" / "GameData" / "art" / "cards"
ABILITIES_PATH = ROOT / "Assets" / "GameData" / "abilities.json"
COSTING_PATH = ROOT / "Assets" / "GameData" / "costing.json"

RARITIES = ["Common", "Rare", "Epic", "Legendary"]
ARCHETYPES = ["Colorless", "Tank", "Bruiser", "Assassin", "Mage", "Healer"]
TYPES = ["guy", "spell"]
CARD_ID_RE = re.compile(r"^[a-z0-9_]+$")

# field name -> (python type, default). Lists are comma-separated on the CLI.
FIELDS = {
    "cardId": (str, None),
    "type": (str, None),
    "displayName": (str, ""),
    "energyCost": (int, 0),
    "goldCost": (int, 0),
    "description": (str, ""),
    "rarity": (str, "Common"),
    "archetypes": (list, []),
    "tags": (list, []),
    "baseAttack": (int, 0),   # guy-only fields below (kept flat; ignored for spells)
    "baseHealth": (int, 1),
    "abilities": (list, []),
    "killRewardGold": (int, 0),
}
GUY_ONLY = ["baseAttack", "baseHealth", "abilities", "killRewardGold"]


def load_all():
    cards = {}
    for path in sorted(CARDS_DIR.glob("*.json")):
        with open(path, encoding="utf-8") as f:
            cards[path] = json.load(f)
    return cards


def save(path, card):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(card, f, indent=4)
        f.write("\n")


def parse_abilities(raw):
    """'armored:1,thorns:2' (or bare 'armored', x defaults to 1) -> [{'id':..., 'x':...}]."""
    result = []
    for part in raw.split(","):
        part = part.strip()
        if not part:
            continue
        ability_id, _, x = part.partition(":")
        try:
            result.append({"id": ability_id.strip(), "x": int(x) if x else 1})
        except ValueError:
            die(f"bad ability '{part}' (use id or id:x, e.g. armored:2)")
    return result


def load_ability_defs():
    """abilityId -> definition dict from abilities.json, or {} if the file is missing."""
    if not ABILITIES_PATH.exists():
        return {}
    with open(ABILITIES_PATH, encoding="utf-8") as f:
        return {a["abilityId"]: a for a in json.load(f).get("abilities", [])}


def coerce(field, raw):
    """Parse a CLI string into the field's type."""
    if field not in FIELDS:
        die(f"unknown field '{field}' (valid: {', '.join(FIELDS)})")
    if field == "abilities":
        return parse_abilities(raw)
    ftype, _ = FIELDS[field]
    if ftype is int:
        try:
            return int(raw)
        except ValueError:
            die(f"{field} expects an integer, got '{raw}'")
    if ftype is list:
        return [v.strip() for v in raw.split(",") if v.strip()]
    return raw


def apply_assignment(card, expr):
    """Apply one key=value / key+=n / key-=n expression to a card dict."""
    m = re.match(r"^(\w+)(\+=|-=|=)(.*)$", expr, re.DOTALL)
    if not m:
        die(f"bad assignment '{expr}' (use field=value, field+=n, field-=n)")
    field, op, raw = m.groups()
    if op == "=":
        card[field] = coerce(field, raw)
    else:
        if FIELDS.get(field, (None,))[0] is not int:
            die(f"'{op}' only works on integer fields, not '{field}'")
        delta = coerce(field, raw)
        card[field] = card.get(field, 0) + (delta if op == "+=" else -delta)


def matches(card, filters):
    """filters: list of 'field=value' strings; lists match if they contain the value."""
    for expr in filters:
        field, _, value = expr.partition("=")
        actual = card.get(field)
        if field == "abilities":
            if value not in [a.get("id") for a in actual or [] if isinstance(a, dict)]:
                return False
        elif isinstance(actual, list):
            if value not in actual:
                return False
        elif str(actual) != value:
            return False
    return True


def die(msg):
    print(f"error: {msg}", file=sys.stderr)
    sys.exit(1)


def path_for(card_id):
    return CARDS_DIR / f"{card_id}.json"


# ---------------------------------------------------------------- commands

def cmd_new(args):
    card_id = args.cardId
    if not CARD_ID_RE.match(card_id):
        die("cardId must be lowercase letters, digits, and underscores (e.g. goblin_01)")
    path = path_for(card_id)
    if path.exists():
        die(f"{path.name} already exists (use 'set' to edit it)")
    card = {f: default for f, (_, default) in FIELDS.items()}
    card["cardId"] = card_id
    card["type"] = args.card_type
    card["displayName"] = args.name or card_id.replace("_", " ").title()
    if args.energy is not None:
        card["energyCost"] = args.energy
    if args.gold is not None:
        card["goldCost"] = args.gold
    if args.rarity:
        card["rarity"] = args.rarity
    if args.archetypes:
        card["archetypes"] = coerce("archetypes", args.archetypes)
    if args.desc:
        card["description"] = args.desc
    if args.card_type == "guy":
        if args.attack is not None:
            card["baseAttack"] = args.attack
        if args.health is not None:
            card["baseHealth"] = args.health
    for expr in args.set or []:
        apply_assignment(card, expr)
    CARDS_DIR.mkdir(parents=True, exist_ok=True)
    save(path, card)
    print(f"created {path.relative_to(ROOT)}")
    warn_card(card)


def cmd_set(args):
    path = path_for(args.cardId)
    if not path.exists():
        die(f"no card '{args.cardId}' (looked for {path.relative_to(ROOT)})")
    with open(path, encoding="utf-8") as f:
        card = json.load(f)
    for expr in args.assignments:
        if expr.split("=")[0].rstrip("+-") == "cardId":
            die("use the 'rename' command to change a cardId")
        apply_assignment(card, expr)
    save(path, card)
    print(f"updated {path.relative_to(ROOT)}")
    warn_card(card)


def cmd_batch(args):
    touched = 0
    for path, card in load_all().items():
        if not matches(card, args.filter or []):
            continue
        for expr in args.set:
            if expr.split("=")[0].rstrip("+-") == "cardId":
                die("batch cannot change cardId")
            apply_assignment(card, expr)
        save(path, card)
        touched += 1
        print(f"updated {card['cardId']}")
    print(f"{touched} card(s) updated")


def cmd_list(args):
    rows = [c for _, c in load_all().items() if matches(c, args.filter or [])]
    if not rows:
        print("no cards match")
        return
    fmt = "{:<16} {:<6} {:<20} {:>3}E {:>3}G {:<10} {}"
    print(fmt.format("cardId", "type", "displayName", "", "", "rarity", "stats"))
    for c in rows:
        stats = f"{c.get('baseAttack')}/{c.get('baseHealth')}" if c.get("type") == "guy" else "-"
        print(fmt.format(c["cardId"], c["type"], c["displayName"][:20],
                         c["energyCost"], c["goldCost"], c["rarity"], stats))
    print(f"{len(rows)} card(s)")


def cmd_rename(args):
    old, new = args.old, args.new
    if not CARD_ID_RE.match(new):
        die("new cardId must be lowercase letters, digits, and underscores")
    old_path, new_path = path_for(old), path_for(new)
    if not old_path.exists():
        die(f"no card '{old}'")
    if new_path.exists():
        die(f"'{new}' already exists")
    with open(old_path, encoding="utf-8") as f:
        card = json.load(f)
    card["cardId"] = new
    save(new_path, card)
    old_path.unlink()
    moved = 0
    if ART_DIR.exists():
        for png in ART_DIR.glob(f"{old}__*.png"):
            png.rename(ART_DIR / png.name.replace(f"{old}__", f"{new}__", 1))
            moved += 1
    print(f"renamed {old} -> {new} ({moved} art file(s) moved)")
    print("note: any saved decks/instances referencing the old id will break; "
          "in Unity, delete the old .asset via Cards > Pipeline > Import All (it flags orphans).")


def warn_card(card):
    for w in card_problems(card, set(load_ability_defs())):
        print(f"  warning: {w}")


def card_problems(card, ability_ids=None):
    problems = []
    for a in card.get("abilities", []):
        if not isinstance(a, dict) or not a.get("id"):
            problems.append(f'bad ability entry {a!r} (expected {{"id": "...", "x": n}})')
            continue
        if not isinstance(a.get("x", 1), int) or a.get("x", 1) < 0:
            problems.append(f"ability '{a['id']}' has bad magnitude x={a.get('x')!r}")
        if ability_ids is not None and a["id"] not in ability_ids:
            problems.append(f"unknown ability '{a['id']}' (not in abilities.json)")
    cid = card.get("cardId", "")
    if not CARD_ID_RE.match(cid or ""):
        problems.append(f"bad cardId '{cid}'")
    if card.get("type") not in TYPES:
        problems.append(f"type must be one of {TYPES}, got '{card.get('type')}'")
    if card.get("rarity") not in RARITIES:
        problems.append(f"rarity must be one of {RARITIES}, got '{card.get('rarity')}'")
    for a in card.get("archetypes", []):
        if a not in ARCHETYPES:
            problems.append(f"unknown archetype '{a}' (valid: {ARCHETYPES})")
    if not card.get("displayName"):
        problems.append("displayName is empty")
    for f in ("energyCost", "goldCost", "baseAttack", "baseHealth", "killRewardGold"):
        if isinstance(card.get(f), int) and card[f] < 0:
            problems.append(f"{f} is negative")
    if card.get("type") == "guy" and card.get("baseHealth", 0) <= 0:
        problems.append("a guy needs baseHealth >= 1")
    if card.get("type") == "spell":
        for f in GUY_ONLY:
            _, default = FIELDS[f]
            if f != "baseHealth" and card.get(f, default) != default:
                problems.append(f"spell has guy-only field '{f}' set (ignored in game)")
    return problems


def cmd_validate(_args):
    cards = load_all()
    ability_ids = set(load_ability_defs())
    errors = 0
    seen = {}
    for path, card in cards.items():
        cid = card.get("cardId", "")
        for p in card_problems(card, ability_ids):
            print(f"{path.name}: {p}")
            errors += 1
        if path.stem != cid:
            print(f"{path.name}: filename does not match cardId '{cid}'")
            errors += 1
        if cid in seen:
            print(f"{path.name}: duplicate cardId '{cid}' (also in {seen[cid].name})")
            errors += 1
        seen[cid] = path
    if ART_DIR.exists():
        for png in ART_DIR.glob("*.png"):
            m = re.match(r"^([a-z0-9_]+)__([a-z0-9_]+)$", png.stem)
            if not m:
                print(f"art/{png.name}: bad name (expected {{cardId}}__{{artId}}.png)")
                errors += 1
            elif m.group(1) not in seen:
                print(f"art/{png.name}: no card with id '{m.group(1)}'")
                errors += 1
    print(f"{len(cards)} card(s) checked, {errors} problem(s)")
    sys.exit(1 if errors else 0)


def cmd_cost_check(args):
    """Power-budget check: card value (stats + abilities) vs. what its energy cost buys.

    value  = attack*w_a + health*w_h + sum(flatCost + costPerX * x per ability)
    budget = base + perEnergy * energyCost + rarityBonus[rarity]
    Flags cards where |value - budget| > tolerance and suggests a cost.
    Weights live in costing.json; per-ability values live in abilities.json.
    """
    defaults = {"statWeights": {"attack": 1.0, "health": 1.0},
                "budget": {"base": 1.5, "perEnergy": 2.5},
                "rarityBonus": {r: 0.0 for r in RARITIES},
                "tolerancePoints": 1.0}
    costing = defaults
    if COSTING_PATH.exists():
        with open(COSTING_PATH, encoding="utf-8") as f:
            costing = {**defaults, **json.load(f)}
    weights, budget = costing["statWeights"], costing["budget"]
    rarity_bonus = costing["rarityBonus"]
    tol = costing["tolerancePoints"]
    ability_defs = load_ability_defs()

    rows, flagged, skipped = [], 0, 0
    for _, card in load_all().items():
        if not matches(card, args.filter or []):
            continue
        if card.get("type") != "guy":
            skipped += 1  # no formula for spells yet: value them by effect once spells do things
            continue
        value = (card.get("baseAttack", 0) * weights["attack"]
                 + card.get("baseHealth", 0) * weights["health"])
        for a in card.get("abilities", []):
            adef = ability_defs.get(a.get("id"), {})
            value += adef.get("flatCost", 0) + adef.get("costPerX", 0) * a.get("x", 1)
        bonus = rarity_bonus.get(card.get("rarity", "Common"), 0.0)
        target = budget["base"] + budget["perEnergy"] * card.get("energyCost", 0) + bonus
        delta = value - target
        suggested = max(0, round((value - budget["base"] - bonus) / budget["perEnergy"]))
        verdict = "ok" if abs(delta) <= tol else ("OVERTUNED" if delta > 0 else "UNDERTUNED")
        if verdict != "ok":
            flagged += 1
        rows.append((card["cardId"], card.get("energyCost", 0), value, target, delta, suggested, verdict))

    fmt = "{:<16} {:>4} {:>6} {:>7} {:>6} {:>9}  {}"
    print(fmt.format("cardId", "cost", "value", "budget", "delta", "suggested", "verdict"))
    for cid, cost, value, target, delta, suggested, verdict in sorted(rows, key=lambda r: -abs(r[4])):
        print(fmt.format(cid, cost, f"{value:.2f}", f"{target:.2f}", f"{delta:+.2f}", suggested, verdict))
    print(f"{len(rows)} guy(s) checked, {flagged} flagged, {skipped} spell(s) skipped "
          f"(tolerance +/-{tol}, weights from {COSTING_PATH.name})")


def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)

    n = sub.add_parser("new", help="create a card JSON")
    n.add_argument("card_type", choices=TYPES)
    n.add_argument("cardId")
    n.add_argument("--name")
    n.add_argument("--energy", type=int)
    n.add_argument("--gold", type=int)
    n.add_argument("--rarity", choices=RARITIES)
    n.add_argument("--archetypes", help="comma-separated, e.g. Tank,Colorless")
    n.add_argument("--desc")
    n.add_argument("--attack", type=int, help="guy only")
    n.add_argument("--health", type=int, help="guy only")
    n.add_argument("--set", nargs="*", help="extra field=value assignments")
    n.set_defaults(fn=cmd_new)

    s = sub.add_parser("set", help="edit one card")
    s.add_argument("cardId")
    s.add_argument("assignments", nargs="+", help="field=value, field+=n, field-=n")
    s.set_defaults(fn=cmd_set)

    b = sub.add_parser("batch", help="edit every card matching --filter")
    b.add_argument("--filter", nargs="*", help="field=value (lists match if they contain the value)")
    b.add_argument("--set", nargs="+", required=True)
    b.set_defaults(fn=cmd_batch)

    l = sub.add_parser("list", help="table of cards")
    l.add_argument("--filter", nargs="*")
    l.set_defaults(fn=cmd_list)

    v = sub.add_parser("validate", help="check every card + art filenames")
    v.set_defaults(fn=cmd_validate)

    c = sub.add_parser("cost-check", help="power-budget audit: stats+abilities vs energy cost")
    c.add_argument("--filter", nargs="*")
    c.set_defaults(fn=cmd_cost_check)

    r = sub.add_parser("rename", help="rename a cardId (json + art files)")
    r.add_argument("old")
    r.add_argument("new")
    r.set_defaults(fn=cmd_rename)

    args = p.parse_args()
    args.fn(args)


if __name__ == "__main__":
    main()
