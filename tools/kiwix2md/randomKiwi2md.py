import sys
import os
import random
from libzim.reader import Archive #type: ignore
from pathlib import Path

from extract_utils import clean_html, is_article, to_markdown, load_config, check_integrity


# - input: the corpus produced by kiwix2md (the folder path could be enough)
# - input: another zim file
# - output: randomly extracted documents that are not included in the input corpus (I bet I will have to match this name `filename = entry.path.replace("/", "_") + ".md"`) added to the corpus
# - output: list of newly extracted doc names


if len(sys.argv) != 4 or sys.argv[2] != "--config":
    print("Usage: randomKiwi2md.py <number-of-docs> --config <corpus.json>")
    sys.exit(1)

num_samples_raw = sys.argv[1]
if not num_samples_raw.isdigit():
    print("Number of docs should be an integer: ", num_samples_raw)
    sys.exit(1)
num_samples = int(num_samples_raw)

cfg = load_config(sys.argv[3])
zim_file = cfg["zim"]
output_dir = Path(cfg["output"])
manifest_file = cfg["manifest"]

# start from a verified-consistent corpus (also covers zim/manifest/dir existence and sha)
check_integrity(zim_file, cfg["zim_sha256"], manifest_file, str(output_dir))

zim = Archive(Path(zim_file))

main = zim.main_entry
main_path = main.get_redirect_entry().path if main.is_redirect else main.path

valid_entries = []
for i in range(zim.entry_count):
    entry = zim._get_entry_by_id(i)
    if not is_article(entry, main_path):
        continue

    filename = entry.path.replace("/", "_") + ".md"
    file_path = output_dir / filename
    if (os.path.isfile(file_path)):
        continue

    valid_entries.append(entry)

entries_to_write = []
invalid_entries = 0;
while (len(entries_to_write) < num_samples):

    remaining = num_samples - len(entries_to_write)
    if len(valid_entries) < remaining:
        print("Not enough entries in the zim file: ", len(valid_entries))
        sys.exit(1)

    samples = random.sample(range(len(valid_entries)), remaining)
    entries_to_remove = []
    skipped_samples = 0
    for sample in samples:

        entry = valid_entries[sample]
        filename = entry.path.replace("/", "_") + ".md"
        file_path = output_dir / filename

        text = bytes(entry.get_item().content).decode("UTF-8")
        cleaned = clean_html(text)
        if cleaned is None:
            invalid_entries+=1
            entries_to_remove.append(sample)
            skipped_samples = skipped_samples + 1
            continue

        markdownContent = to_markdown(cleaned)
        if markdownContent is None:
            entries_to_remove.append(sample)
            skipped_samples = skipped_samples + 1
            continue

        entries_to_write.append((entry.path, file_path, markdownContent))
        entries_to_remove.append(sample)
    
    # we have not finished yet
    if skipped_samples > 0:
        new_list = [element for index, element in enumerate(valid_entries) if index not in entries_to_remove]
        valid_entries = new_list

with open(manifest_file, "a") as manifest:
    for (entry_path, file_path, markdownContent) in entries_to_write:
        file_path.write_text(markdownContent, encoding="utf-8")
        manifest.write(entry_path + "\n")

print(f"Wrote {num_samples} markdown files to {output_dir}, found invalid entries: ", invalid_entries)

check_integrity(zim_file, cfg["zim_sha256"], manifest_file, str(output_dir))
print("integrity OK")