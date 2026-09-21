#!/usr/bin/env python3
"""Query the minerva v1 collection directly and report ranked sources."""

import argparse
import sys
from pathlib import Path

import chromadb
import httpx

CHROMADB = Path(__file__).parent / "chromadb_data"
COLLECTION = "wp1283-legacy"
EMBEDDING_URL = "http://localhost:1234/v1/embeddings"
EMBEDDING_MODEL = "text-embedding-bge-m3"
TOP_K = 50


def embed(client: httpx.Client, text: str) -> list[float]:
    response = client.post(EMBEDDING_URL, json={"model": EMBEDDING_MODEL, "input": [text]})
    response.raise_for_status()
    vector = response.json()["data"][0]["embedding"]
    # v1 normalizes before storing and before querying; cosine space assumes it.
    norm = sum(v * v for v in vector) ** 0.5
    return [v / norm for v in vector]


def search(collection, client: httpx.Client, query: str, top_k: int) -> list[dict]:
    result = collection.query(
        query_embeddings=[embed(client, query)],
        n_results=top_k,
        include=["metadatas", "distances"],
    )
    return [
        {
            "rank": i + 1,
            "chunk_id": result["ids"][0][i],
            "source_id": result["metadatas"][0][i]["title"] + ".md",
            "score": 1.0 - result["distances"][0][i],
        }
        for i in range(len(result["ids"][0]))
    ]


def open_collection(chromadb_path: Path):
    collection = chromadb.PersistentClient(path=str(chromadb_path)).get_collection(COLLECTION)
    space = (collection.metadata or {}).get("hnsw:space")
    if space != "cosine":
        raise SystemExit(f"collection uses '{space}' space; the 1-distance score assumes cosine")
    return collection


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("query", nargs="?", default="Which phylum do the animals commonly called bugs belong to?")
    parser.add_argument("--chromadb", type=Path, default=CHROMADB)
    parser.add_argument("--top-k", type=int, default=TOP_K)
    parser.add_argument("--show", type=int, default=10)
    args = parser.parse_args()

    collection = open_collection(args.chromadb.resolve())
    print(f"collection: {COLLECTION} ({collection.count()} chunks)")
    print(f"query:      {args.query}\n")

    with httpx.Client(timeout=60.0) as client:
        hits = search(collection, client, args.query, args.top_k)

    for hit in hits[:args.show]:
        print(f"{hit['rank']:3d}  {hit['score']:.4f}  {hit['source_id']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
