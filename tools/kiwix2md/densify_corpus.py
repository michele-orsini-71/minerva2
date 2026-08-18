import sys
import os
import json
import time
from extract_utils import load_config, ZimArchive
from dataclasses import dataclass
from litellm import embedding
from timeit import default_timer as timer
import numpy as np
from typing import Any

@dataclass(frozen=True)
class ZimArticle:
    path: str
    incipit: str

@dataclass(frozen=True)
class Neighbor:
    path: str
    score: float

MAX_ARTICLES = 0  # 0 = no limit, process the whole ZIM
ARTICLES_FILE = "zim-cluster-articles.jsonl"

def load_existing_articles(file_path: str) -> dict[str, ZimArticle]:
    articles: dict[str, ZimArticle] = {}
    if not os.path.isfile(file_path):
        return articles
    with open(file_path, "r") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            record = json.loads(line)
            articles[record["path"]] = ZimArticle(record["path"], record["incipit"])
    return articles

def extract_zim_articles(zim_file: str) -> list[ZimArticle]:
    existing = load_existing_articles(ARTICLES_FILE)
    article_list = list(existing.values())
    print(f"Loaded {len(existing)} already-extracted articles, resuming")

    archive = ZimArchive(zim_file)

    with open(ARTICLES_FILE, "a") as out:
        for i in range(archive.entry_count()):
            if MAX_ARTICLES and len(article_list) >= MAX_ARTICLES:
                break
            try:
                entry = archive.get_entry_by_index(i)
                if entry.path in existing:
                    continue
                markdown = archive.get_markdown(entry)
                if markdown is None:
                    continue

                lines = markdown.split('\n')
                start_index = lines[1:].index('---')
                excerpt = " ".join(lines[(start_index + 1):(start_index + 6)])
                article = ZimArticle(entry.path, excerpt)
                article_list.append(article)
                out.write(json.dumps({"path": article.path, "incipit": article.incipit}) + "\n")
                out.flush()
            except Exception as e:
                print(f"skipped entry {i}: {e}")
                continue

    return article_list

EMBED_MAX_RETRIES = 5

def embed_content(content:list[str]) -> list:
    for attempt in range(EMBED_MAX_RETRIES):
        try:
            response = embedding(model='lm_studio/text-embedding-bge-m3', input=content, api_base="http://localhost:1234/v1", api_key="not-needed")
            return [ data['embedding'] for data in response.data ]
        except Exception as e:
            if attempt == EMBED_MAX_RETRIES - 1:
                raise
            wait = 2 ** attempt
            print(f"embed failed ({e}); retry {attempt + 1}/{EMBED_MAX_RETRIES} in {wait}s")
            time.sleep(wait)
    raise RuntimeError("unreachable: retries exhausted without raising")

EMBEDDINGS_FILE = "zim-cluster-embeddings.jsonl"
EMBED_BATCH_SIZE = 64
DONE_MARKER = "zim-cluster.done"  # written only after a full extract+embed pass

def load_embedded_paths(file_path: str) -> set[str]:
    paths: set[str] = set()
    if not os.path.isfile(file_path):
        return paths
    with open(file_path, "r") as f:
        for line in f:
            line = line.strip()
            if line:
                paths.add(json.loads(line)["path"])
    return paths

def embed_articles(articles: list[ZimArticle]) -> None:
    already_embedded = load_embedded_paths(EMBEDDINGS_FILE)
    pending = [a for a in articles if a.path not in already_embedded]
    print(f"{len(already_embedded)} already embedded, {len(pending)} pending")

    with open(EMBEDDINGS_FILE, "a") as out:
        for start in range(0, len(pending), EMBED_BATCH_SIZE):
            batch = pending[start:start + EMBED_BATCH_SIZE]
            vectors = embed_content([a.incipit for a in batch])
            for article, vector in zip(batch, vectors):
                out.write(json.dumps({"path": article.path, "embedding": vector}) + "\n")
            out.flush()
            print(f"embedded {start + len(batch)}/{len(pending)}")

def load_embeddings(file_path: str) -> tuple[list[str], list[list[float]]]:
    paths: list[str] = []
    vectors: list[list[float]] = []
    with open(file_path, "r") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            record = json.loads(line)
            paths.append(record["path"])
            vectors.append(record["embedding"])
    return paths, vectors

def neighbors(embedding_matrix:np.typing.NDArray[Any], paths: list[str], seed_path:str, k:int) -> list[Neighbor]:
    index = paths.index(seed_path)
    
    q = embedding_matrix[index]
    
    similar = embedding_matrix @ q
    order = np.argsort(-similar)
    result:list[Neighbor] = []
    for i in order:
        if i == index:                     # skip the seed by identity, not position
            continue
        result.append(Neighbor(paths[i], float(similar[i])))
        if len(result) == k:
            break

    return result


if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] != "--config":
        print("Usage: densify_corpus.py --config <corpus.json>")
        sys.exit(1)
    cfg = load_config(sys.argv[2])
    if os.path.isfile(DONE_MARKER):
        print(f"{DONE_MARKER} present, skipping extraction and embedding")
    else:
        start = timer()
        articles = extract_zim_articles(cfg["zim"])
        end_extraction = timer()
        print(f'Extraction time: {end_extraction - start}')
        embed_articles(articles)
        end_embedding = timer()
        print(f'Embedding time: {end_embedding - end_extraction}')
        with open(DONE_MARKER, "w"):
            pass
        print(f"wrote {DONE_MARKER}")

    paths, embeddings = load_embeddings(EMBEDDINGS_FILE)
    assert len(paths) > 0
    embedding_matrix = np.array(embeddings)
    embedding_matrix /= np.linalg.norm(embedding_matrix, axis=1, keepdims=True)
    
    try:
        neighbors_list = neighbors(embedding_matrix, paths, "James_Franco", 10)
        for neighbor in neighbors_list:
            print(f"{neighbor.score:.3f}  {neighbor.path}")
    except ValueError as e:
        print(f"Error: {e}")
        