import sys 
import os
from libzim.reader import Archive #type: ignore
from pathlib import Path
from extract import is_article, to_markdown, clean_html

if len(sys.argv) < 3:
    print("Usage: kiwix2md.py <zim_file> <output_dir>")
    sys.exit(1)

zim_file = sys.argv[1]
output_dir = Path(sys.argv[2])

if not os.path.isfile(zim_file):
    print("File does not exists: ", zim_file)
    sys.exit(1)

output_dir.mkdir(parents=True, exist_ok=True)
zim = Archive(Path(zim_file))

main = zim.main_entry
main_path = main.get_redirect_entry().path if main.is_redirect else main.path

written = 0
for i in range(zim.entry_count):
    entry = zim._get_entry_by_id(i)
    if not is_article(entry, main_path):
        continue

    text = bytes(entry.get_item().content).decode("UTF-8")
    cleaned = clean_html(text)
    if cleaned is None:
        continue

    markdownContent = to_markdown(cleaned)
    if markdownContent is None:
        continue

    filename = entry.path.replace("/", "_") + ".md"
    (output_dir / filename).write_text(markdownContent, encoding="utf-8")
    written += 1

print(f"Wrote {written} markdown files to {output_dir}")