import os
import sys
import json
import hashlib
from bs4 import BeautifulSoup
from html_to_markdown import convert, ConversionOptions, PreprocessingOptions
from libzim.reader import Archive, Entry #type: ignore
from pathlib import Path

REMOVE_SELECTORS = ["table.infobox", ".navbox", ".sidebar", "sup.reference"]
options = ConversionOptions(
    skip_images=True,
    preprocessing=PreprocessingOptions(enabled=True, preset="aggressive"),
)

def clean_html(raw):
    soup = BeautifulSoup(raw, "html.parser")
    if soup.find("meta", attrs={"http-equiv": "refresh"}):
        return None  # redirect stub, not an article
    for selector in REMOVE_SELECTORS:
        for node in soup.select(selector):
            node.decompose()
    return str(soup)

# The landing page and MediaWiki internal resources (the "_" namespace) are valid HTML but not articles; exclude them by identity, not by content.

def md_filename(entry) -> str:
    return entry.path.replace("/", "_") + ".md"

def to_markdown(text: str) -> str | None:
    # cleaned = clean_html(text)
    # if cleaned is None:
    #     return None

    result = convert(text, options)
    return result.content


# Validate the manifest against the zim before any extraction: right zim, no
# duplicates, every entry present. Returns the entries so callers can reuse them.
def verify_manifest(zim_file: str, zim_file_sha: str, manifest_file: str) -> list:

    if not os.path.isfile(zim_file):
        print("File does not exists: ", zim_file)
        sys.exit(1)

    if not os.path.isfile(manifest_file):
        print("File does not exists: ", manifest_file)
        sys.exit(1)

    zim_file_sha_to_check = hashlib.sha256(Path(zim_file).read_bytes()).hexdigest()
    if zim_file_sha != zim_file_sha_to_check:
        print("Zim file has wrong sha: ", zim_file)
        sys.exit(1)

    with open(manifest_file, "r") as f:
        manifest_entries = [s.strip() for s in f.readlines() if s.strip()]

    if len(manifest_entries) != len(set(manifest_entries)):
        print("detected duplicated entries in the manifest")
        sys.exit(1)

    zim = Archive(Path(zim_file))
    for manifest_entry in manifest_entries:
        if not zim.has_entry_by_path(manifest_entry):
            print("missing corpus entry: ", manifest_entry)
            sys.exit(1)

    return manifest_entries


def check_integrity(zim_file: str, zim_file_sha: str, manifest_file: str, output_dir: str) -> bool:

    if not os.path.isdir(output_dir):
        print("Corpus dir does not exists: ", output_dir)
        sys.exit(1)

    manifest_entries = verify_manifest(zim_file, zim_file_sha, manifest_file)
    output_path = Path(output_dir)

    for manifest_entry in manifest_entries:
        file_path = output_path / (manifest_entry.replace("/", "_") + ".md")
        if not os.path.isfile(file_path):
            print("missing corpus filename: ", file_path)
            sys.exit(1)

    if len(list(output_path.glob("*.md"))) != len(manifest_entries):
        print("Existing files and manifest entries differ in number")
        sys.exit(1)

    return True


def load_config(path: str) -> dict:
    with open(path, "r") as f:
        return json.load(f)

class ZimArchive:
    def __init__(self, zim_file: str):
        self.zim_file = zim_file
        self.zim = Archive(Path(zim_file))
        self.main = self.zim.main_entry
        self.main_path = self.main.get_redirect_entry().path if self.main.is_redirect else self.main.path

    def is_article(self, entry: Entry) -> bool:
        if entry.is_redirect:
            return False
        if entry.path == self.main_path or entry.path.startswith("_"):
            return False

        item = entry.get_item()
        if not item.mimetype.startswith("text/html"):
            return False

        return True
    
    def entry_count(self) -> int:
        return self.zim.entry_count;

    def get_entry_by_index(self, id: int):
        return self.zim._get_entry_by_id(id)
    
    def get_markdown(self, entry: Entry) -> str|None:
        if not self.is_article(entry):
            return

        text = bytes(entry.get_item().content).decode("UTF-8")
        cleaned = clean_html(text)
        if cleaned is None:
            return

        return to_markdown(cleaned)
