#!/usr/bin/env python3
"""Generate a mod.yml for spawn point and AreaData program exports.

This helper scans the output of the "Mass Export" features of KH2 Map Studio
and produces an ``mod.yml`` that can be consumed by the OpenKH Mod Manager.
It automatically wires the exported spawn groups and AreaData scripts using the
appropriate asset methods.

Typical usage::

    python kh2_spawn_export_to_mod.py /path/to/export/root \
        --title "My Spawn Tweaks" --original-author "Modder" \
        --description "Spawn and script adjustments"

The script expects the export directory to contain the folder structure produced
by Map Studio, for example ``ard/<map>/<spawn>.yml`` for spawn groups and
``ard/<map>/<entry>_<id>.areadataprogram`` for AreaData scripts. Regional
variants (such as ``jp/ard/...``) are detected automatically.
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import OrderedDict, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Tuple


@dataclass
class ScriptEntry:
    """Represents a single AreaData program export."""

    program_id: str
    path: str

    def sort_key(self) -> Tuple[int, str]:
        try:
            return (0, f"{int(self.program_id):010d}")
        except ValueError:
            return (1, self.program_id)


@dataclass
class AssetBucket:
    """Collects all spawn/script data for a single ARD binarc."""

    region_parts: Tuple[str, ...]
    map_name: str
    spawnpoints: Dict[str, set]
    scripts: Dict[str, List[ScriptEntry]]

    @property
    def ard_relative_path(self) -> str:
        parts = list(self.region_parts) + [f"{self.map_name}.ard"]
        relative = "/".join(filter(None, parts))
        return f"ard/{relative}" if relative else f"ard/{self.map_name}.ard"


class ModBuilder:
    """Helper class that aggregates exports and renders the mod.yml."""

    def __init__(self, root: Path) -> None:
        self.root = root
        self.assets: Dict[Tuple[Tuple[str, ...], str], AssetBucket] = {}
        self.spawnpoint_count = 0
        self.script_count = 0

    def build(self) -> None:
        self._collect_spawnpoints()
        self._collect_scripts()

    # ------------------------------------------------------------------
    # Collectors
    # ------------------------------------------------------------------
    def _collect_spawnpoints(self) -> None:
        for path in self.root.rglob("*.yml"):
            if path.name == "mod.yml":
                continue
            parts = path.relative_to(self.root).parts
            try:
                ard_index = parts.index("ard")
            except ValueError:
                continue
            if len(parts) <= ard_index + 2:
                continue

            region_parts = tuple(parts[:ard_index])
            map_name = parts[ard_index + 1]
            entry_name = Path(parts[-1]).stem
            if not map_name or not entry_name:
                continue

            bucket = self._get_bucket(region_parts, map_name)
            bucket.spawnpoints.setdefault(entry_name, set()).add(path.relative_to(self.root).as_posix())
            self.spawnpoint_count += 1

    def _collect_scripts(self) -> None:
        for path in self.root.rglob("*.areadataprogram"):
            parts = path.relative_to(self.root).parts
            try:
                ard_index = parts.index("ard")
            except ValueError:
                continue
            if len(parts) <= ard_index + 2:
                continue

            region_parts = tuple(parts[:ard_index])
            map_name = parts[ard_index + 1]
            stem = Path(parts[-1]).stem
            if "_" not in stem:
                continue
            program_type, program_id = stem.rsplit("_", 1)
            if not program_type:
                continue

            bucket = self._get_bucket(region_parts, map_name)
            bucket.scripts.setdefault(program_type, []).append(
                ScriptEntry(program_id=program_id, path=path.relative_to(self.root).as_posix())
            )
            self.script_count += 1

    def _get_bucket(self, region_parts: Tuple[str, ...], map_name: str) -> AssetBucket:
        key = (region_parts, map_name)
        if key not in self.assets:
            self.assets[key] = AssetBucket(region_parts, map_name, defaultdict(set), defaultdict(list))
        return self.assets[key]

    # ------------------------------------------------------------------
    # Rendering
    # ------------------------------------------------------------------
    def render(self, metadata: OrderedDict) -> str:
        ordered_assets = []
        for (region_parts, map_name) in sorted(self.assets.keys(), key=self._asset_sort_key):
            bucket = self.assets[(region_parts, map_name)]
            entries = []

            for spawn_name in sorted(bucket.spawnpoints.keys(), key=str.lower):
                sources = sorted(bucket.spawnpoints[spawn_name])
                entry = OrderedDict()
                entry["method"] = "spawnpoint"
                entry["name"] = spawn_name
                entry["source"] = [OrderedDict([("name", src)]) for src in sources]
                entry["type"] = "AreaDataSpawn"
                entries.append(entry)

            for script_name in sorted(bucket.scripts.keys(), key=str.lower):
                scripts = bucket.scripts[script_name]
                scripts.sort(key=lambda item: item.sort_key())
                entry = OrderedDict()
                entry["method"] = "areadatascript"
                entry["name"] = script_name
                entry["source"] = [OrderedDict([("name", item.path)]) for item in scripts]
                entry["type"] = "AreaDataScript"
                entries.append(entry)

            if not entries:
                continue

            asset = OrderedDict()
            asset["method"] = "binarc"
            asset["name"] = bucket.ard_relative_path
            asset["source"] = entries
            ordered_assets.append(asset)

        if not ordered_assets:
            raise ValueError("No spawnpoint or AreaData program exports were found under the provided root.")

        document = OrderedDict(metadata)
        document["assets"] = ordered_assets
        return dump_yaml(document)

    @staticmethod
    def _asset_sort_key(key: Tuple[Tuple[str, ...], str]) -> Tuple[str, str]:
        region_parts, map_name = key
        return ("/".join(region_parts), map_name)


# ----------------------------------------------------------------------
# YAML helpers
# ----------------------------------------------------------------------

def dump_yaml(document: OrderedDict) -> str:
    lines: List[str] = []
    _dump_value(document, 0, lines)
    return "\n".join(lines) + "\n"


def _dump_value(value, indent: int, lines: List[str], *, list_item: bool = False) -> None:
    if isinstance(value, OrderedDict):
        _dump_dict(value, indent, lines, list_item=list_item)
    elif isinstance(value, dict):
        _dump_dict(OrderedDict(value.items()), indent, lines, list_item=list_item)
    elif isinstance(value, list):
        _dump_list(value, indent, lines)
    else:
        prefix = (" " * indent)
        if list_item:
            prefix += "- "
        lines.append(prefix + _format_scalar(value))


def _dump_dict(value: OrderedDict, indent: int, lines: List[str], *, list_item: bool = False) -> None:
    items = list(value.items())
    if not items:
        prefix = " " * indent + ("- {}" if list_item else "{}")
        lines.append(prefix)
        return

    first_key, first_value = items[0]
    leading = (" " * indent + "- " if list_item else " " * indent) + f"{first_key}:"
    if _is_scalar(first_value):
        lines.append(leading + " " + _format_scalar(first_value))
    else:
        lines.append(leading)
        _dump_value(first_value, indent + (4 if list_item else 2), lines)

    for key, value in items[1:]:
        offset = indent + 2 if list_item else indent
        prefix = " " * offset + f"{key}:"
        if _is_scalar(value):
            lines.append(prefix + " " + _format_scalar(value))
        else:
            lines.append(prefix)
            _dump_value(value, offset + 2, lines)


def _dump_list(value: Iterable, indent: int, lines: List[str]) -> None:
    value = list(value)
    if not value:
        lines.append(" " * indent + "[]")
        return

    for item in value:
        if isinstance(item, (OrderedDict, dict)):
            _dump_dict(OrderedDict(item.items()) if not isinstance(item, OrderedDict) else item, indent, lines, list_item=True)
        elif isinstance(item, list):
            lines.append(" " * indent + "-")
            _dump_list(item, indent + 2, lines)
        else:
            lines.append(" " * indent + "- " + _format_scalar(item))


def _is_scalar(value) -> bool:
    return isinstance(value, (str, int, float)) or value is None or isinstance(value, bool)


def _format_scalar(value) -> str:
    if isinstance(value, str):
        return json.dumps(value)
    if isinstance(value, bool):
        return "true" if value else "false"
    if value is None:
        return "null"
    return str(value)


# ----------------------------------------------------------------------
# CLI entry point
# ----------------------------------------------------------------------

def parse_args(argv: List[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Create a mod.yml for KH2 spawn exports.")
    parser.add_argument("root", type=Path, help="Path to the directory that contains the exported files.")
    parser.add_argument("--output", "-o", type=Path, default=None, help="Where to write the generated mod.yml (defaults to <root>/mod.yml).")
    parser.add_argument("--title", default="KH2 Spawn Data Mod", help="Title written into mod.yml.")
    parser.add_argument("--description", default="Auto-generated mod.yml for exported spawn points and AreaData scripts.", help="Description written into mod.yml.")
    parser.add_argument("--original-author", "--author", default="Unknown", help="Original author field for the mod.yml.")
    parser.add_argument("--game", default="kh2", help="Optional game identifier to include.")
    return parser.parse_args(argv)


def main(argv: List[str]) -> int:
    args = parse_args(argv)
    root = args.root.expanduser().resolve()
    if not root.is_dir():
        print(f"error: '{root}' is not a directory", file=sys.stderr)
        return 1

    output_path = args.output or (root / "mod.yml")

    builder = ModBuilder(root)
    builder.build()

    metadata = OrderedDict()
    metadata["title"] = args.title
    metadata["description"] = args.description
    metadata["originalAuthor"] = args.original_author
    if args.game:
        metadata["game"] = args.game

    try:
        yaml_text = builder.render(metadata)
    except ValueError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    output_path.write_text(yaml_text, encoding="utf-8")

    print(
        f"Wrote {output_path} (spawnpoints: {builder.spawnpoint_count}, AreaData programs: {builder.script_count})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
