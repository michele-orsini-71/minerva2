from pathlib import Path
import re
import json
import pandas as pd
from pydantic import BaseModel, Field

class CellResult(BaseModel):
    # top_k: int
    hybrid_alpha: float
    rerank_depth: int = 0  # 0 = reranker off
    cascade_depth: int = 0  # 0 = reranker off

class Metrics(BaseModel):
    recall_at_5: float
    recall_at_10: float
    recall_at_20: float
    success_at_5: int
    success_at_10: int
    success_at_20: int
    rr_at_10: float

class Hit(BaseModel):
    rank: int
    chunk_id: str
    source_id: str
    score: float
    gold_hit: bool

class EvalResult(BaseModel):
    collection: str          # injected from run.json; not present in the jsonl row
    label: str               # run checkpoint name; injected from run.json (fallbacks to folder name)
    query_id: str
    query: str
    cell: CellResult
    gold_sources: list[str] = Field(default_factory=list)
    metrics: Metrics
    latency_ms: int
    error: str | None = None
    hits: list[Hit] = Field(default_factory=list)

results_roots = [Path("runs")]   # each experiment's own runs; pass roots=[...] to compare across experiments

def find_run_dirs(root: Path) -> list[Path]:
    if not root.exists():                                     # a root may be absent on a fresh checkout
        return []
    return sorted(p for p in root.iterdir()
                  if p.is_dir() and re.match(r"\d{4}-\d{2}-\d{2}", p.name))

def all_run_dirs(roots: list[Path] | None = None) -> list[Path]:
    return [d for root in (roots or results_roots) for d in find_run_dirs(root)]

def load_run(run_dir: Path) -> list[EvalResult]:
    meta = json.loads((run_dir / "run.json").read_text())
    collection = meta["collection"]
    label = meta.get("label", run_dir.name)     # harness written or timestamped folder
    lines = (run_dir / "details.jsonl").read_text().splitlines()
    return [EvalResult.model_validate(json.loads(line) | {"collection": collection, "label": label})
            for line in lines if line.strip()]

def load_all_results(roots: list[Path] | None = None):
    return [r for d in all_run_dirs(roots) for r in load_run(d)]

def summarize_run(run_dir: Path) -> dict:
    run = json.loads((run_dir / "run.json").read_text())
    rows = load_run(run_dir)
    matrix = run["resolved_sweep"]["matrix"]
    return {
        "folder": run_dir.name,
        "collection": run["collection"],
        "timestamp": run["timestamp"],
        "top_k": run["resolved_sweep"]['top_k'],
        "candidate_pool_size": run["resolved_sweep"]["candidate_pool_size"],
        "sweep": f"alpha={matrix['hybrid_alpha']}, reranker={matrix['enable_reranker']}, rerank_depth={matrix.get('rerank_depth')}, cascade_depth={matrix.get('cascade_depth')}",
        "queries": len({r.query_id for r in rows}),
        "rows": len(rows),
    }

def get_run_summary(roots: list[Path] | None = None):
    return pd.DataFrame(summarize_run(d) for d in all_run_dirs(roots))

metric_cols = ["R@5", "R@10", "R@20", "MRR@10"]

def metrics_heatmap(results: list[EvalResult]):
    per_query = pd.DataFrame([
        {
            "label": r.label,
            "collection": r.collection,
            "alpha": r.cell.hybrid_alpha,
            "rerank_depth": r.cell.rerank_depth,
            "cascade_depth": r.cell.cascade_depth,
            "R@5": r.metrics.recall_at_5,
            "R@10": r.metrics.recall_at_10,
            "R@20": r.metrics.recall_at_20,
            "MRR@10": r.metrics.rr_at_10,  # per-query RR; becomes MRR@10 after the groupby mean below
        }
        for r in results
    ])

    groups = _grouped(per_query)
    metrics = groups[metric_cols].mean().round(3)
    metrics["n"] = groups.size()
    return metrics.style.background_gradient(cmap="RdYlGn", subset=metric_cols)

def best_rank_by_source(result: EvalResult) -> dict[str, int]:
    """Collapse the result's chunk hits to sources, keeping each source's best (lowest) rank.
    Returns {source_id: best_rank}."""
    best = {}
    for h in result.hits:
        if h.source_id not in best or h.rank < best[h.source_id]:
            best[h.source_id] = h.rank
    return best

def average_chunk_rank_by_source(result: EvalResult) -> dict[str,float]:
    counts: dict[str,int] = {}
    rank_sum: dict[str,int] = {}
    for h in result.hits:
        if counts.get(h.source_id) is None:
            counts[h.source_id] = 1
            rank_sum[h.source_id] = h.rank
        else:
            counts[h.source_id] = counts[h.source_id] + 1
            rank_sum[h.source_id] = rank_sum[h.source_id] + h.rank

    return {sid: rank_sum[sid] / counts[sid] for sid in counts}


from dataclasses import dataclass

@dataclass(frozen=True)
class GoldRank:
    query_id: str
    collection: str
    label: str
    alpha: float
    rerank_depth: int | None
    cascade_depth: int | None
    gold: str
    rank: int

def gold_ranks(results, top_k=50) -> list[GoldRank]:
    out = []
    for r in results:
        ranks = best_rank_by_source(r)
        for g in r.gold_sources:
            out.append(GoldRank(r.query_id, r.collection, r.label, r.cell.hybrid_alpha, r.cell.rerank_depth, r.cell.cascade_depth,
                                g, ranks.get(g, top_k + 1)))
    return out

GROUP_KEYS = ["label", "collection", "alpha", "rerank_depth", "cascade_depth"]

def _grouped(df, extra=()):
    return df.groupby(GROUP_KEYS + list(extra))

def covering_ranks(results, top_k=50) -> pd.Series:
    # covering rank = smallest k whose top-k contains ALL the query's golds:
    # gold_ranks emits one row per gold source, so max over the query's group
    df = pd.DataFrame(gold_ranks(results, top_k))
    return _grouped(df, ["query_id"])["rank"].max()

def covering_rank_stats(results, top_k=50):
    # a gold beyond top_k carries the sentinel top_k + 1: read it as "> top_k", not a real rank
    covering = covering_ranks(results, top_k)
    return covering.groupby(["label", "collection", "alpha", "rerank_depth", "cascade_depth"]).agg(
        median="median", p95=lambda s: s.quantile(0.95), max="max")

@dataclass(frozen=True)
class AverageChunkRank:
    query_id: str
    collection: str
    alpha: float
    gold: str
    average: float

def average_chunk_ranks(results, top_k=50) -> list[AverageChunkRank]:
    out = []
    for r in results:
        ranks = average_chunk_rank_by_source(r)
        for g in r.gold_sources:
            out.append(AverageChunkRank(r.query_id, r.collection, r.cell.hybrid_alpha,
                                g, ranks.get(g, top_k + 1)))
    return out
