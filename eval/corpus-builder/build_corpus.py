import sys
from pathlib import Path
from libzim.reader import Archive #type: ignore

from extract_utils import clean_html, to_markdown, md_filename, load_config, verify_manifest


def build(zim_file: str, zim_file_sha: str, manifest_file: str, output_dir: str):
    # pre-flight: fail before writing anything if the manifest/zim do not line up
    entries = verify_manifest(zim_file, zim_file_sha, manifest_file)

    zim = Archive(Path(zim_file))
    output_path = Path(output_dir)
    output_path.mkdir(parents=True, exist_ok=True)

    written = 0
    skipped = 0
    for path in entries:
        entry = zim.get_entry_by_path(path)
        if entry.is_redirect:
            entry = entry.get_redirect_entry()

        text = bytes(entry.get_item().content).decode("UTF-8")
        cleaned = clean_html(text)
        if cleaned is None:
            print("skipped (redirect stub): ", path)
            skipped += 1
            continue

        markdown = to_markdown(cleaned)
        if markdown is None:
            print("skipped (no markdown): ", path)
            skipped += 1
            continue

        (output_path / md_filename(entry)).write_text(markdown, encoding="utf-8")
        written += 1

    print(f"wrote {written}, skipped {skipped}")


if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] != "--config":
        print("Usage: buildCorpus.py --config <corpus.json>")
        sys.exit(1)
    cfg = load_config(sys.argv[2])
    build(cfg["zim"], cfg["zim_sha256"], cfg["manifest"], cfg["output"])
