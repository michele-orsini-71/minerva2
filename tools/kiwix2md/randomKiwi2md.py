import sys
import os
import random
from libzim.reader import Archive #type: ignore
from pathlib import Path

from extract import clean_html, is_article, to_markdown
from datetime import datetime


# - input: the corpus produced by kiwix2md (the folder path could be enough)
# - input: another zim file
# - output: randomly extracted documents that are not included in the input corpus (I bet I will have to match this name `filename = entry.path.replace("/", "_") + ".md"`) added to the corpus
# - output: list of newly extracted doc names


if len(sys.argv) < 4:
    print("Usage: randomKiwi2md.py <number-of-docs> <zim_file> <corpus_dir>")
    sys.exit(1)

num_samples_raw = sys.argv[1]
zim_file = sys.argv[2]
output_dir = Path(sys.argv[3])

if not os.path.isfile(zim_file):
    print("File does not exists: ", zim_file)
    sys.exit(1)

if not os.path.isdir(output_dir):
    print("Corpus dir does not exists: ", output_dir)
    sys.exit(1)

if not num_samples_raw.isdigit():
    print("Number of docs should be an integer: ", num_samples_raw)
    sys.exit(1)

num_samples = int(num_samples_raw)

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

        entries_to_write.append((file_path, markdownContent))
        entries_to_remove.append(sample)
    
    # we have not finished yet
    if skipped_samples > 0:
        new_list = [element for index, element in enumerate(valid_entries) if index not in entries_to_remove]
        valid_entries = new_list

now = datetime.now()
output_file_name = "added_on_" + now.strftime("%Y%m%d_%H%M%S") + ".txt"
output_file_path = file_path = output_dir / output_file_name 
with open(output_file_path, "w") as f:
    for (file_path, markdownContent) in entries_to_write:
        file_path.write_text(markdownContent, encoding="utf-8")
        f.write(str(file_path) + '\n')
        # print(file_path)

print(f"Wrote {num_samples} markdown files to {output_dir}, found invalid entries: ", invalid_entries)