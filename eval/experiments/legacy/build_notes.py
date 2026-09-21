#!/usr/bin/env python3
"""Convert the markdown corpus into minerva1's notes JSON format."""

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

CORPUS = Path(__file__).parent / "../../collections/wikipedia-en-corpus"
OUTPUT = Path(__file__).parent / "notes-wp1283.json"


def strip_frontmatter(raw: str) -> str:
    # minerva2 indexes the body only; stripping keeps the two chunkers comparable.
    if not raw.startswith("---"):
        return raw
    end = raw.find("\n---", 3)
    if end == -1:
        return raw
    body_start = raw.find("\n", end + 1)
    return raw[body_start + 1:] if body_start != -1 else ""


def build_note(path: Path) -> dict:
    markdown = strip_frontmatter(path.read_text(encoding="utf-8"))
    mtime = datetime.fromtimestamp(path.stat().st_mtime, tz=timezone.utc)
    return {
        "title": path.stem,
        "markdown": markdown,
        "size": len(markdown.encode("utf-8")),
        "modificationDate": mtime.strftime("%Y-%m-%dT%H:%M:%SZ"),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus", type=Path, default=CORPUS)
    parser.add_argument("--output", type=Path, default=OUTPUT)
    args = parser.parse_args()

    corpus = args.corpus.resolve()
    if not corpus.is_dir():
        print(f"corpus not found: {corpus}", file=sys.stderr)
        return 1

    files = sorted(corpus.glob("*.md"))
    if not files:
        print(f"no markdown files in {corpus}", file=sys.stderr)
        return 1

    notes = [build_note(f) for f in files]
    empty = [n["title"] for n in notes if n["size"] == 0]

    args.output.write_text(json.dumps(notes, ensure_ascii=False), encoding="utf-8")

    print(f"notes:  {len(notes)}")
    print(f"output: {args.output.resolve()}")
    if empty:
        print(f"empty notes ({len(empty)}): {', '.join(empty[:5])}")
    sample = dict(notes[0])
    sample["markdown"] = sample["markdown"][:200] + "..."
    print("sample:")
    print(json.dumps(sample, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
