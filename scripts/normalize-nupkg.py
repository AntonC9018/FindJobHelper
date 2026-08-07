#!/usr/bin/env python3
"""Normalize unsigned NuGet packages into byte-reproducible ZIP archives."""

from __future__ import annotations

import hashlib
import pathlib
import re
import sys
import tempfile
import zipfile


FIXED_TIME = (1980, 1, 1, 0, 0, 0)
CORE_PATH = re.compile(
    r"^package/services/metadata/core-properties/[0-9A-Fa-f]+\.psmdcp$"
)


def stable_id(value: str) -> str:
    return "R" + hashlib.sha256(value.encode("utf-8")).hexdigest()[:16].upper()


def normalize(package: pathlib.Path) -> None:
    with zipfile.ZipFile(package, "r") as source:
        entries = {info.filename: source.read(info) for info in source.infolist()}

    core_paths = [name for name in entries if CORE_PATH.match(name)]
    if len(core_paths) != 1:
        raise ValueError(f"{package}: expected one core-properties entry, found {core_paths}")

    old_core_path = core_paths[0]
    core_bytes = entries.pop(old_core_path)
    core_digest = hashlib.sha256(core_bytes).hexdigest().upper()
    new_core_path = f"package/services/metadata/core-properties/{core_digest[:32]}.psmdcp"
    entries[new_core_path] = core_bytes

    relationships = entries["_rels/.rels"].decode("utf-8")
    relationships = relationships.replace("/" + old_core_path, "/" + new_core_path)
    relationships = re.sub(
        r'Id="R[0-9A-F]+"',
        lambda match: f'Id="{stable_id(relationships[match.start() - 160:match.start()])}"',
        relationships,
    )
    entries["_rels/.rels"] = relationships.encode("utf-8")

    with tempfile.NamedTemporaryFile(delete=False, dir=package.parent, suffix=".nupkg") as temp:
        temp_path = pathlib.Path(temp.name)

    try:
        with zipfile.ZipFile(
            temp_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9
        ) as output:
            for name in sorted(entries):
                info = zipfile.ZipInfo(name, FIXED_TIME)
                info.compress_type = zipfile.ZIP_DEFLATED
                info.create_system = 3
                info.external_attr = 0o100644 << 16
                output.writestr(info, entries[name], compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
        temp_path.replace(package)
    finally:
        temp_path.unlink(missing_ok=True)


def main(arguments: list[str]) -> int:
    if not arguments:
        raise SystemExit("usage: normalize-nupkg.py PACKAGE [PACKAGE ...]")
    for argument in arguments:
        normalize(pathlib.Path(argument).resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
