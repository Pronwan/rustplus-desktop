#!/usr/bin/env python3
"""
Validate language JSON files in RustPlusDesktop/Languages/.

Reports, for every language file other than en.json:
  - missing keys (present in en.json, absent here)
  - extra keys (present here, absent in en.json)
  - untranslated values (start with the @@TODO marker)
  - placeholder mismatches (e.g. en has "{0}" but translation drops it)

Exit code is 0 only when every non-English file is structurally complete
(no missing/extra keys and no placeholder mismatches). Untranslated @@TODO
entries are reported but do NOT fail the run, so partial translations stay
mergeable while still being highly visible.

Usage:
    python3 validate-languages.py             # validate Languages/ next to script
    python3 validate-languages.py --strict    # also fail on untranslated keys
"""
import json
import re
import sys
from pathlib import Path

PLACEHOLDER_RE = re.compile(r"\{(\d+)\}")
TODO_PREFIX = "@@TODO"


def load(path):
    with path.open(encoding="utf-8") as f:
        return json.load(f)


def placeholders(value):
    return sorted(set(PLACEHOLDER_RE.findall(value)))


def main():
    strict = "--strict" in sys.argv[1:]
    languages_dir = Path(__file__).resolve().parent.parent / "Languages"
    en_path = languages_dir / "en.json"
    if not en_path.exists():
        print(f"ERROR: reference file not found: {en_path}")
        return 2

    en = load(en_path)
    en_keys = {k for k in en.keys() if not k.startswith("__")}

    files = sorted(p for p in languages_dir.glob("*.json") if p.name != "en.json")
    if not files:
        print("No translation files found (only en.json).")
        return 0

    structural_failure = False
    todo_failure = False

    for path in files:
        data = load(path)
        keys = {k for k in data.keys() if not k.startswith("__")}

        missing = sorted(en_keys - keys)
        extra = sorted(keys - en_keys)
        todo = sorted(k for k, v in data.items()
                      if k in en_keys and isinstance(v, str) and v.startswith(TODO_PREFIX))
        placeholder_mismatches = []
        for k in sorted(en_keys & keys):
            ev, tv = en[k], data[k]
            if not isinstance(tv, str) or tv.startswith(TODO_PREFIX):
                continue
            if placeholders(ev) != placeholders(tv):
                placeholder_mismatches.append((k, placeholders(ev), placeholders(tv)))

        translated = len(en_keys) - len(todo) - len(missing)
        pct = 100.0 * translated / max(1, len(en_keys))

        print(f"\n--- {path.name} ({pct:.0f}% translated) ---")
        print(f"  total keys: {len(en_keys)}, translated: {translated}, missing: {len(missing)}, extra: {len(extra)}, untranslated: {len(todo)}")

        if missing:
            print("  MISSING KEYS:")
            for k in missing:
                print(f"    - {k}")
            structural_failure = True
        if extra:
            print("  EXTRA KEYS (not in en.json):")
            for k in extra:
                print(f"    - {k}")
            structural_failure = True
        if placeholder_mismatches:
            print("  PLACEHOLDER MISMATCHES:")
            for k, en_ph, tr_ph in placeholder_mismatches:
                print(f"    - {k}: en uses {en_ph}, translation uses {tr_ph}")
            structural_failure = True
        if todo:
            todo_failure = True
            preview = todo[:5]
            print(f"  UNTRANSLATED ({len(todo)}, showing first {len(preview)}):")
            for k in preview:
                print(f"    - {k}")

    print()
    if structural_failure:
        print("FAIL: structural problems found in one or more language files.")
        return 1
    if strict and todo_failure:
        print("FAIL (--strict): untranslated keys remain.")
        return 1
    print("OK: no structural problems.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
