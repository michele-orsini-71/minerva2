#!/usr/bin/env python3
"""Run the whole eval dataset against the minerva v1 collection and write details.jsonl."""

import argparse
import json
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

import httpx

from run_legacy import CHROMADB, COLLECTION, EMBEDDING_MODEL, TOP_K, open_collection, search

HERE = Path(__file__).parent
DATASET = HERE / "../../datasets/wp1283-v1.jsonl"
RUNS = HERE / "runs"
CELL = {"enable_reranker": False, "hybrid_alpha": 1.0}
LABEL = "minerva1"


def recall_at(ranked: list[str], gold: set[str], k: int) -> float:
    top_k = set(ranked[:k])
    return sum(1 for g in gold if g in top_k) / len(gold)


def success_at(ranked: list[str], gold: set[str], k: int) -> float:
    return 1.0 if any(s in gold for s in ranked[:k]) else 0.0


def rr_at(ranked: list[str], gold: set[str], k: int) -> float:
    for i, source in enumerate(ranked[:k]):
        if source in gold:
            return 1.0 / (i + 1)
    return 0.0


def metrics(ranked: list[str], gold: set[str]) -> dict:
    return {
        "recall_at_5": recall_at(ranked, gold, 5),
        "recall_at_10": recall_at(ranked, gold, 10),
        "recall_at_20": recall_at(ranked, gold, 20),
        "success_at_5": success_at(ranked, gold, 5),
        "success_at_10": success_at(ranked, gold, 10),
        "success_at_20": success_at(ranked, gold, 20),
        "rr_at_10": rr_at(ranked, gold, 10),
    }


def run_query(collection, client: httpx.Client, entry: dict, top_k: int) -> dict:
    started = time.perf_counter()
    hits = search(collection, client, entry["query"], top_k)
    latency_ms = int((time.perf_counter() - started) * 1000)

    gold = set(entry["gold_sources"])
    for hit in hits:
        hit["gold_hit"] = hit["source_id"] in gold

    return {
        "query_id": entry["id"],
        "query": entry["query"],
        "cell": CELL,
        "gold_sources": entry["gold_sources"],
        "metrics": metrics([h["source_id"] for h in hits], gold),
        "latency_ms": latency_ms,
        "error": None,
        "hits": hits,
    }


def write_run_json(run_dir: Path, dataset: Path, top_k: int, timestamp: str) -> None:
    # Same shape the C# bench writes, so the notebooks treat this run like any other.
    manifest = {
        "timestamp": timestamp,
        "bench_version": "legacy-driver/minerva 3.0.0",
        "dataset_path": str(dataset),
        "collection": COLLECTION,
        "collectionDetails": {
            "Name": COLLECTION,
            "Provenance": {
                "Invariants": {
                    "EmbeddingModel": EMBEDDING_MODEL,
                    "EmbeddingDimension": 1024,
                    "ChunkerType": "minerva1 header + recursive",
                    "TargetChunkSize": 1200,
                },
            },
        },
        "reranker_model": None,
        "cascade_model": None,
        "embedding_model": EMBEDDING_MODEL,
        "top_k": top_k,
        # v1 has no candidate pool: the dense query returns exactly top_k hits.
        "candidate_pool_size": top_k,
        "resolved_sweep": {
            "top_k": top_k,
            "candidate_pool_size": top_k,
            "dataset": str(dataset),
            "collection": COLLECTION,
            "matrix": {
                "enable_reranker": [CELL["enable_reranker"]],
                "hybrid_alpha": [CELL["hybrid_alpha"]],
            },
        },
        "cells": [CELL],
        "label": LABEL,
    }
    (run_dir / "run.json").write_text(json.dumps(manifest, indent=2) + "\n")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dataset", type=Path, default=DATASET)
    parser.add_argument("--chromadb", type=Path, default=CHROMADB)
    parser.add_argument("--top-k", type=int, default=TOP_K)
    parser.add_argument("--limit", type=int, help="stop after this many queries")
    args = parser.parse_args()

    entries = [json.loads(line) for line in args.dataset.read_text().splitlines() if line.strip()]
    if args.limit:
        entries = entries[:args.limit]

    collection = open_collection(args.chromadb.resolve())
    now = datetime.now(timezone.utc)
    run_dir = RUNS / f"{now.strftime('%Y-%m-%dT%H-%M-%SZ')}_{args.dataset.stem}"
    run_dir.mkdir(parents=True)
    details = run_dir / "details.jsonl"
    write_run_json(run_dir, args.dataset, args.top_k, now.strftime("%Y-%m-%dT%H:%M:%SZ"))

    print(f"queries: {len(entries)}  top_k: {args.top_k}")
    print(f"output:  {details}\n")

    records = []
    started = time.perf_counter()
    with httpx.Client(timeout=60.0) as client, details.open("w") as out:
        for i, entry in enumerate(entries, 1):
            record = run_query(collection, client, entry, args.top_k)
            out.write(json.dumps(record, ensure_ascii=False) + "\n")
            records.append(record)
            if i % 100 == 0 or i == len(entries):
                print(f"  {i}/{len(entries)}  {(time.perf_counter() - started):.0f}s")

    print()
    for name in ("recall_at_5", "recall_at_10", "recall_at_20", "rr_at_10"):
        mean = sum(r["metrics"][name] for r in records) / len(records)
        print(f"{name:14s} {mean:.3f}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
