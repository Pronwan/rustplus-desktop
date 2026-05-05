#!/usr/bin/env python3
"""
Find user-visible string literals in XAML and C# files that are NOT yet wired
through the localization system. Helpful for the next contributor migrating
the remaining strings after this PR.

Heuristics (deliberately noisy, false-positive friendly):
  - XAML: Text="...", Content="...", Header="...", ToolTip="..." with a
    literal value (skipped when value already starts with "{l:Loc" or "{Binding").
  - C#:   MessageBox.Show("...", ...) string literals.

Usage: python3 extract-strings.py [path]
"""
import re
import sys
from pathlib import Path

XAML_ATTR_RE = re.compile(r'(?P<attr>Text|Content|Header|ToolTip|Title)\s*=\s*"(?P<val>[^"]*)"')
MESSAGEBOX_RE = re.compile(r'MessageBox\.Show\s*\(\s*"(?P<val>[^"]*)"')


def is_localized_or_binding(value):
    if not value:
        return True
    v = value.strip()
    if v.startswith("{") and ("Binding" in v or "l:Loc" in v or "DynamicResource" in v or "StaticResource" in v):
        return True
    return False


def looks_like_user_string(value):
    if not value or value.isspace():
        return False
    if all(not c.isalpha() for c in value):
        return False
    if value.startswith("pack://") or value.startswith("/icons"):
        return False
    return True


def scan_xaml(path):
    hits = []
    text = path.read_text(encoding="utf-8", errors="replace")
    for m in XAML_ATTR_RE.finditer(text):
        v = m.group("val")
        if is_localized_or_binding(v):
            continue
        if not looks_like_user_string(v):
            continue
        line = text.count("\n", 0, m.start()) + 1
        hits.append((line, m.group("attr"), v))
    return hits


def scan_cs(path):
    hits = []
    text = path.read_text(encoding="utf-8", errors="replace")
    for m in MESSAGEBOX_RE.finditer(text):
        v = m.group("val")
        if not looks_like_user_string(v):
            continue
        line = text.count("\n", 0, m.start()) + 1
        hits.append((line, "MessageBox", v))
    return hits


def main():
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent
    total = 0
    for path in sorted(root.rglob("*")):
        if "Languages" in path.parts or "Tools" in path.parts or "obj" in path.parts or "bin" in path.parts:
            continue
        if path.suffix == ".xaml":
            hits = scan_xaml(path)
        elif path.suffix == ".cs":
            hits = scan_cs(path)
        else:
            continue
        if not hits:
            continue
        rel = path.relative_to(root)
        print(f"\n{rel}:")
        for line, kind, value in hits:
            short = value if len(value) <= 70 else value[:67] + "..."
            print(f"  {line:>5}  {kind:<10} {short!r}")
            total += 1
    print(f"\nTotal candidates: {total}")
    print("Note: this is a heuristic. Inspect each hit before migrating.")


if __name__ == "__main__":
    main()
