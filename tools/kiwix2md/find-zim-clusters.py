import sys
from extract_utils import load_config, ZimArchive
from dataclasses import dataclass
from litellm import embedding
import os

@dataclass(frozen=True)
class ZimArticle:
    path: str
    incipit: str

MAX_ARTICLES = 30

def extract_zim_articles(zim_file: str) -> list[ZimArticle]:
    article_list = []

    archive = ZimArchive(zim_file)

    for i in range(archive.entry_count()):
        entry = archive.get_entry_by_index(i)
        markdown = archive.get_markdown(entry)
        if markdown is None:
            continue

        lines = markdown.split('\n')
        start_index = lines[1:].index('---')
        excerpt = " ".join(lines[(start_index + 1):(start_index + 6)])
        article = ZimArticle(entry.path, excerpt)
        article_list.append(article)
        if (len(article_list) % 100) == 0:
            print(len(article_list))
            print(entry.path)
            print(excerpt)
        
        if len(article_list) == MAX_ARTICLES:
            break

    return article_list

if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] != "--config":
        print("Usage: find-zim-clusters.py --config <corpus.json>")
        sys.exit(1)
    cfg = load_config(sys.argv[2])
    # articles = extract_zim_articles(cfg["zim"])

    # os.environ['OPENAI_API_KEY'] = ""
    response = embedding(model='lm_studio/text-embedding-bge-m3', input=["good morning from litellm"], api_base="http://localhost:1234/v1", api_key="not-needed")
    print(response)
    print(response.data[0]['embedding'])

