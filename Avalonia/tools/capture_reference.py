"""Snapshot legacy UI and source candidates without changing the reference app."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import xml.etree.ElementTree as ET

EXCLUDED = {"bin", "obj", "node_modules", "dist", ".git", ".vs"}
SOURCE_SUFFIXES = {".cs", ".xaml", ".resx", ".csproj", ".ts", ".tsx", ".js", ".jsx", ".css", ".html", ".json", ".ps1"}
ACTION_ATTRIBUTES = {
    "Click", "Command", "Checked", "Unchecked", "SelectionChanged", "Selected", "TextChanged",
    "KeyDown", "KeyUp", "PreviewKeyDown", "MouseDown", "MouseUp", "MouseMove", "MouseWheel",
    "MouseDoubleClick", "MouseLeftButtonDown", "MouseLeftButtonUp", "MouseRightButtonDown",
    "PreviewMouseLeftButtonDown", "PreviewMouseLeftButtonUp", "PreviewTextInput", "ValueChanged",
    "Drop", "DragOver", "DragEnter", "DragLeave", "Loaded", "Unloaded", "CloseRequested",
    "Closing", "Closed", "SizeChanged", "ContextMenuOpening", "GotFocus", "LostFocus",
}
XAML = "{http://schemas.microsoft.com/winfx/2006/xaml}"


def capture(source: Path) -> dict:
    files, views = [], []
    for directory, folders, names in os.walk(source):
        folders[:] = sorted(folder for folder in folders if folder not in EXCLUDED)
        for name in sorted(names):
            path = Path(directory) / name
            if path.suffix.lower() not in SOURCE_SUFFIXES:
                continue
            content = path.read_bytes()
            relative = path.relative_to(source).as_posix()
            files.append({"path": relative, "sha256": hashlib.sha256(content).hexdigest()})
            if path.suffix.lower() != ".xaml":
                continue
            root = ET.fromstring(content)
            actions = []
            for node in root.iter():
                attributes = {key.split("}")[-1]: value for key, value in node.attrib.items()}
                if ACTION_ATTRIBUTES.intersection(attributes):
                    actions.append({"type": node.tag.split("}")[-1], "attributes": attributes})
            views.append({
                "path": relative,
                "class": root.get(XAML + "Class"),
                "rootType": root.tag.split("}")[-1],
                "required": True,
                "behaviorStatus": "pending",
                "visualStatus": "pending",
                "namedElements": sorted({node.get(XAML + "Name") for node in root.iter() if node.get(XAML + "Name")}),
                "actions": actions,
            })
    return {
        "schemaVersion": 1,
        "scope": "Filesystem source candidates, not proof of runtime reachability. All UI candidates remain required until reviewed.",
        "sourceFiles": sorted(files, key=lambda item: item["path"]),
        "uiCandidates": sorted(views, key=lambda item: item["path"]),
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    source = args.source.resolve(strict=True)
    if not source.is_dir():
        parser.error("--source must be a directory")
    output = args.output.resolve()
    if output == source or source in output.parents:
        parser.error("--output must be outside the reference application")
    manifest = capture(source)
    output.mkdir(parents=True, exist_ok=True)
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    rows = ["# UI parity checklist", "", "Every candidate is required pending behavior and design verification.",
            "Generated source evidence is in `manifest.json`; do not equate a file with a complete feature.", "",
            "| Reference UI | Type | Behavior | Visual |", "| --- | --- | --- | --- |"]
    rows.extend(f'| `{view["path"]}` | {view["rootType"]} | Pending | Pending |' for view in manifest["uiCandidates"])
    (output / "UI-CHECKLIST.md").write_text("\n".join(rows) + "\n", encoding="utf-8")
    print(f'{len(manifest["sourceFiles"])} source candidates; {len(manifest["uiCandidates"])} XAML candidates; '
          f'{sum(len(view["actions"]) for view in manifest["uiCandidates"])} action-bearing elements')


if __name__ == "__main__":
    main()
