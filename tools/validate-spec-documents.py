#!/usr/bin/env python3
"""Validates the published catalog and lines documents against their schemas.

Why this exists: the schemas under docs/extension-spec/ describe documents that are
*generated*, by tools/build-skin-catalog.ps1 and tools/build-appearance-lines.ps1. A
schema and a generator that disagree is worse than no schema, because the document is
what a third party reads to learn the format -- and the generator is what actually
decides it. This reads the real files and checks them, so the two cannot drift apart
unnoticed.

Deliberately dependency-free. Only the subset of JSON Schema the project's documents
actually use is implemented, and anything outside that subset raises rather than being
skipped: a validator that silently ignores a keyword it does not know would report
success on a schema it never really applied.

Usage:
    python tools/validate-spec-documents.py
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# (document, schema) pairs. The plugin catalog is included because it is published by
# the same process and its schema is the older one the appearance documents were modelled
# on -- if only one of them still matches its schema, that is worth knowing.
PAIRS = [
    ("skins/catalog.json", "docs/extension-spec/appearance-v1/catalog.schema.json"),
    ("skins/lines.json", "docs/extension-spec/appearance-v2/lines.schema.json"),
    ("plugin-catalog.json", "docs/extension-spec/feature-v1/catalog.schema.json"),
    ("notices.json", "docs/extension-spec/notices-v1/notice.schema.json"),
]

KNOWN = {
    "$schema", "$id", "title", "description", "$defs", "$ref",
    "type", "const", "enum", "required", "properties", "additionalProperties",
    "items", "maxItems", "maxProperties", "propertyNames", "pattern",
    "minLength", "maxLength", "format", "minimum", "maximum",
}

TYPES = {
    "object": dict, "array": list, "string": str,
    "number": (int, float), "integer": int, "boolean": bool,
}


def resolve(schema: dict, root: dict) -> dict:
    """Follows a local $ref, which is the only kind these schemas use."""
    seen = 0
    while "$ref" in schema:
        ref = schema["$ref"]
        if not ref.startswith("#/"):
            raise ValueError(f"only local $ref is supported, got {ref!r}")
        target = root
        for part in ref[2:].split("/"):
            target = target[part]
        schema = target
        seen += 1
        if seen > 32:
            raise ValueError(f"$ref cycle at {ref!r}")
    return schema


def check(node, schema, root, path, errors):
    schema = resolve(schema, root)

    unknown = set(schema) - KNOWN
    if unknown:
        raise ValueError(f"{path}: schema uses unimplemented keyword(s) {sorted(unknown)}")

    if "const" in schema and node != schema["const"]:
        errors.append(f"{path}: expected {schema['const']!r}, found {node!r}")
        return
    if "enum" in schema and node not in schema["enum"]:
        errors.append(f"{path}: expected one of {schema['enum']!r}, found {node!r}")
        return

    if "type" in schema:
        expected = TYPES[schema["type"]]
        # bool is a subclass of int in Python, so an integer field would accept True.
        if isinstance(node, bool) and schema["type"] != "boolean":
            errors.append(f"{path}: expected {schema['type']}, found boolean")
            return
        if not isinstance(node, expected):
            errors.append(f"{path}: expected {schema['type']}, found {type(node).__name__}")
            return

    if isinstance(node, dict):
        for key in schema.get("required", []):
            if key not in node:
                errors.append(f"{path}: missing required {key!r}")
        properties = schema.get("properties", {})
        extra = schema.get("additionalProperties", True)
        if schema.get("maxProperties") is not None and len(node) > schema["maxProperties"]:
            errors.append(f"{path}: {len(node)} properties exceeds maxProperties {schema['maxProperties']}")
        names = schema.get("propertyNames")
        for key, value in node.items():
            if names is not None:
                check(key, names, root, f"{path}.{key}<name>", errors)
            if key in properties:
                check(value, properties[key], root, f"{path}.{key}", errors)
            elif extra is False:
                errors.append(f"{path}: unexpected property {key!r}")
            elif isinstance(extra, dict):
                check(value, extra, root, f"{path}.{key}", errors)

    if isinstance(node, list):
        if schema.get("maxItems") is not None and len(node) > schema["maxItems"]:
            errors.append(f"{path}: {len(node)} items exceeds maxItems {schema['maxItems']}")
        if "items" in schema:
            for index, item in enumerate(node):
                check(item, schema["items"], root, f"{path}[{index}]", errors)

    if isinstance(node, str):
        if schema.get("maxLength") is not None and len(node) > schema["maxLength"]:
            errors.append(f"{path}: {len(node)} characters exceeds maxLength {schema['maxLength']}")
        if schema.get("minLength") is not None and len(node) < schema["minLength"]:
            errors.append(f"{path}: shorter than minLength {schema['minLength']}")
        if "pattern" in schema and not re.search(schema["pattern"], node):
            errors.append(f"{path}: {node!r} does not match {schema['pattern']}")

    if isinstance(node, (int, float)) and not isinstance(node, bool):
        if schema.get("minimum") is not None and node < schema["minimum"]:
            errors.append(f"{path}: {node} is below minimum {schema['minimum']}")
        if schema.get("maximum") is not None and node > schema["maximum"]:
            errors.append(f"{path}: {node} is above maximum {schema['maximum']}")


def main() -> int:
    failures = 0
    for document_path, schema_path in PAIRS:
        document_file = ROOT / document_path
        schema_file = ROOT / schema_path
        if not document_file.exists():
            print(f"FAIL  {document_path}: not found")
            failures += 1
            continue

        document = json.loads(document_file.read_text(encoding="utf-8"))
        schema = json.loads(schema_file.read_text(encoding="utf-8"))
        errors: list[str] = []
        try:
            check(document, schema, schema, document_path, errors)
        except ValueError as error:
            print(f"FAIL  {document_path}: {error}")
            failures += 1
            continue

        if errors:
            failures += 1
            print(f"FAIL  {document_path} against {schema_path}")
            for error in errors[:12]:
                print(f"        {error}")
            if len(errors) > 12:
                print(f"        ... and {len(errors) - 12} more")
        else:
            print(f"ok    {document_path}  ({schema_path})")

    # Package-local copies are the offline source of truth. Validate every one,
    # including the built-in placeholder, against the current additive line schema.
    schema_path = ROOT / "docs/extension-spec/appearance-v2/lines.schema.json"
    schema = json.loads(schema_path.read_text(encoding="utf-8"))
    pet_root = ROOT / "versions/csharp-wpf/assets/pets"
    for document_file in sorted(pet_root.glob("*/lines.json")):
        document_path = document_file.relative_to(ROOT).as_posix()
        document = json.loads(document_file.read_text(encoding="utf-8"))
        errors: list[str] = []
        try:
            check(document, schema["$defs"]["lineSet"], schema, document_path, errors)
        except ValueError as error:
            print(f"FAIL  {document_path}: {error}")
            failures += 1
            continue
        if errors:
            failures += 1
            print(f"FAIL  {document_path} against docs/extension-spec/appearance-v2/lines.schema.json")
            for error in errors[:12]:
                print(f"        {error}")
            if len(errors) > 12:
                print(f"        ... and {len(errors) - 12} more")
        else:
            print(f"ok    {document_path}  (appearance lines v2)")

    # The catalog and served lines are generated from authored/package sources.
    # A valid JSON document can still be stale, so check that the generated copy
    # actually reflects those inputs as well as matching its schema.
    copy_path = ROOT / "skins/appearance-copy.json"
    catalog = json.loads((ROOT / "skins/catalog.json").read_text(encoding="utf-8"))
    authored = json.loads(copy_path.read_text(encoding="utf-8")).get("descriptions", {})
    catalog_by_style = {
        entry["id"][4:]: entry for entry in catalog.get("appearances", [])
        if isinstance(entry.get("id"), str) and entry["id"].startswith("pet.")
    }
    copy_issues = []
    if set(authored) != set(catalog_by_style):
        copy_issues.append(
            f"appearance-copy/catalog ids differ: missing catalog={sorted(set(authored) - set(catalog_by_style))}; "
            f"missing copy={sorted(set(catalog_by_style) - set(authored))}"
        )
    for style, copy in authored.items():
        entry = catalog_by_style.get(style)
        if entry is None:
            continue
        if entry.get("description") != copy.get("zh") or entry.get("description_en") != copy.get("en"):
            copy_issues.append(f"{style}: catalog description is stale")
    if copy_issues:
        failures += 1
        print("FAIL  skins/catalog.json vs skins/appearance-copy.json")
        for issue in copy_issues[:12]:
            print(f"        {issue}")
    else:
        print(f"ok    skins/catalog.json descriptions match all {len(authored)} authored bilingual entries")

    published_lines = json.loads((ROOT / "skins/lines.json").read_text(encoding="utf-8")).get("lines", {})
    source_lines = {}
    for document_file in sorted(pet_root.glob("*/lines.json")):
        if document_file.parent.name.startswith("_"):
            continue
        line_set = json.loads(document_file.read_text(encoding="utf-8"))
        line_set.pop("schema_version", None)
        source_lines[document_file.parent.name] = line_set
    line_issues = []
    if set(source_lines) != set(published_lines):
        line_issues.append(
            f"package/catalog line ids differ: missing published={sorted(set(source_lines) - set(published_lines))}; "
            f"missing source={sorted(set(published_lines) - set(source_lines))}"
        )
    for style, source in source_lines.items():
        if published_lines.get(style) != source:
            line_issues.append(f"{style}: served lines are stale versus package source")
    if line_issues:
        failures += 1
        print("FAIL  skins/lines.json vs package-local lines.json")
        for issue in line_issues[:12]:
            print(f"        {issue}")
    else:
        print(f"ok    skins/lines.json matches all {len(source_lines)} package-local sources")

    if failures:
        print(f"\n{failures} document(s) do not match their schema or source.")
        return 1
    print("\nAll documents match their schemas and generated sources.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
