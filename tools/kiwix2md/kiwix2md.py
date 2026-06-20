import sys 
import os
from libzim.reader import Archive #type: ignore
from pathlib import Path

from bs4 import BeautifulSoup
from html_to_markdown import convert, ConversionOptions, PreprocessingOptions

REMOVE_SELECTORS = ["table.infobox", ".navbox", ".sidebar", "sup.reference"]


def clean_html(raw):
    soup = BeautifulSoup(raw, "html.parser")
    if soup.find("meta", attrs={"http-equiv": "refresh"}):
        return None  # redirect stub, not an article
    for selector in REMOVE_SELECTORS:
        for node in soup.select(selector):
            node.decompose()
    return str(soup)

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

# The landing page and MediaWiki internal resources (the "_" namespace) are
# valid HTML but not articles; exclude them by identity, not by content.
main = zim.main_entry
main_path = main.get_redirect_entry().path if main.is_redirect else main.path

options = ConversionOptions(
    skip_images=True,
    preprocessing=PreprocessingOptions(enabled=True, preset="aggressive"),
)

written = 0
for i in range(zim.entry_count):
    entry = zim._get_entry_by_id(i)
    if entry.is_redirect:
        continue
    if entry.path == main_path or entry.path.startswith("_"):
        continue

    item = entry.get_item()
    if not item.mimetype.startswith("text/html"):
        continue

    text = bytes(item.content).decode("UTF-8")
    cleaned = clean_html(text)
    if cleaned is None:
        continue

    result = convert(cleaned, options)
    if result.content is None:
        continue

    filename = entry.path.replace("/", "_") + ".md"
    (output_dir / filename).write_text(result.content, encoding="utf-8")
    written += 1

print(f"Wrote {written} markdown files to {output_dir}")