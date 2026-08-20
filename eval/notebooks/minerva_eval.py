from pathlib import Path
import re
import json
import hashlib
from functools import lru_cache
from collections import defaultdict
import pandas as pd
from pydantic import BaseModel, Field

class CellResult(BaseModel):
    top_k: int
    hybrid_alpha: float

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

results_roots = [Path("../baselines"), Path("../results")]   # committed refs + scratch

def find_run_dirs(root: Path) -> list[Path]:
    if not root.exists():                                     # a root may be absent on a fresh checkout
        return []
    return sorted(p for p in root.iterdir()
                  if p.is_dir() and re.match(r"\d{4}-\d{2}-\d{2}", p.name))

def all_run_dirs() -> list[Path]:
    return [d for root in results_roots for d in find_run_dirs(root)]

def load_run(run_dir: Path) -> list[EvalResult]:
    meta = json.loads((run_dir / "run.json").read_text())
    collection = meta["collection"]
    label = meta.get("label", run_dir.name)     # harness written or timestamped folder
    lines = (run_dir / "details.jsonl").read_text().splitlines()
    return [EvalResult.model_validate(json.loads(line) | {"collection": collection, "label": label})
            for line in lines if line.strip()]

def load_all_results():
    return [r for d in all_run_dirs() for r in load_run(d)]

def summarize_run(run_dir: Path) -> dict:
    run = json.loads((run_dir / "run.json").read_text())
    rows = load_run(run_dir)
    matrix = run["resolved_sweep"]["matrix"]
    return {
        "folder": run_dir.name,
        "collection": run["collection"],
        "timestamp": run["timestamp"],
        "sweep": f"top_k={matrix['top_k']} alpha={matrix['hybrid_alpha']}",
        "queries": len({r.query_id for r in rows}),
        "rows": len(rows),
    }

def get_run_summary():
    return pd.DataFrame(summarize_run(d) for d in all_run_dirs())

metric_cols = ["R@5", "R@10", "R@20", "MRR@10"]

def metrics_heatmap(results: list[EvalResult], group_cols=("collection", "alpha")):
    per_query = pd.DataFrame([
        {
            "label": r.label,
            "collection": r.collection,
            "alpha": r.cell.hybrid_alpha,
            "R@5": r.metrics.recall_at_5,
            "R@10": r.metrics.recall_at_10,
            "R@20": r.metrics.recall_at_20,
            "MRR@10": r.metrics.rr_at_10,  # per-query RR; becomes MRR@10 after the groupby mean below
        }
        for r in results
    ])

    groups = per_query.groupby(list(group_cols))
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
    gold: str
    rank: int

def gold_ranks(results, top_k=50) -> list[GoldRank]:
    out = []
    for r in results:
        ranks = best_rank_by_source(r)
        for g in r.gold_sources:
            out.append(GoldRank(r.query_id, r.collection, r.label, r.cell.hybrid_alpha,
                                g, ranks.get(g, top_k + 1)))
    return out

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

@lru_cache(maxsize=None)
def chunk_index_of(collection: str, source_id: str, chunk_id: str, max_scan: int = 5000) -> int:
    """Recover a chunk's index by inverting the deterministic id hash
    (chunk_id = sha256(f"{collection}:{source_id}:{index}")). Cached per distinct chunk."""
    for i in range(max_scan):
        if hashlib.sha256(f"{collection}:{source_id}:{i}".encode()).hexdigest() == chunk_id:
            return i
    raise ValueError(f"chunk_index not found for {chunk_id} within {max_scan} scans")

def source_chunk_ranks(result: EvalResult, source_id: str) -> dict[int, int]:
    """{chunk_index: best rank} for one source's chunks in one result."""
    out: dict[int, int] = {}
    for h in result.hits:
        if h.source_id != source_id:
            continue
        idx = chunk_index_of(result.collection, source_id, h.chunk_id)
        if idx not in out or h.rank < out[idx]:
            out[idx] = h.rank
    return out

@dataclass(frozen=True)
class ChunkDelta:
    query_id: str
    alpha: float
    gold: str
    mean_delta: float   # baseline rank - treatment rank; > 0 = treatment ranks the gold's chunks higher
    n_chunks: int

def paired_chunk_deltas(results, baseline: str, treatment: str, top_k: int = 50) -> list[ChunkDelta]:
    """Per (query, alpha, gold): mean signed rank change of the gold source's chunks, pairing
    baseline vs treatment on chunk_index. A chunk present on only one side is floored to top_k + 1,
    so a chunk rescued from beyond the cutoff still counts. Positive mean_delta means the treatment
    (contextualization) ranks the gold's chunks higher (lower rank number)."""
    by_cell: dict[tuple[str, float], dict[str, EvalResult]] = defaultdict(dict)
    for r in results:
        by_cell[(r.query_id, r.cell.hybrid_alpha)][r.collection] = r

    out = []
    for (query_id, alpha), by_collection in by_cell.items():
        base = by_collection.get(baseline)
        treat = by_collection.get(treatment)
        if base is None or treat is None:
            continue
        for g in set(base.gold_sources) | set(treat.gold_sources):
            base_ranks = source_chunk_ranks(base, g)
            treat_ranks = source_chunk_ranks(treat, g)
            indices = set(base_ranks) | set(treat_ranks)
            if not indices:
                continue
            deltas = [base_ranks.get(i, top_k + 1) - treat_ranks.get(i, top_k + 1) for i in indices]
            out.append(ChunkDelta(query_id, alpha, g, sum(deltas) / len(deltas), len(indices)))
    return out

@dataclass(frozen=True)
class ChunkWinLoss:
    query_id: str
    alpha: float
    gold: str
    better_chunk_baseline_count: int    # chunks the baseline ranks better (lower rank number)
    better_chunk_treatment_count: int   # chunks the treatment ranks better
    net_wins: int                       # treatment - baseline; > 0 = treatment wins more often
    n_chunks: int

def paired_chunk_winloss(results, baseline: str, treatment: str, top_k: int = 50) -> list[ChunkWinLoss]:
    """Per (query, alpha, gold): count how many of the gold source's chunks the treatment ranks
    better than the baseline vs how many the baseline ranks better, pairing on chunk_index. A chunk
    present on only one side is floored to top_k + 1. net_wins is treatment wins minus baseline wins;
    positive means contextualization ranks more chunks higher than the baseline does. Ties don't count."""
    by_cell: dict[tuple[str, float], dict[str, EvalResult]] = defaultdict(dict)
    for r in results:
        by_cell[(r.query_id, r.cell.hybrid_alpha)][r.collection] = r

    out = []
    for (query_id, alpha), by_collection in by_cell.items():
        base = by_collection.get(baseline)
        treat = by_collection.get(treatment)
        if base is None or treat is None:
            continue
        for g in set(base.gold_sources) | set(treat.gold_sources):
            base_ranks = source_chunk_ranks(base, g)
            treat_ranks = source_chunk_ranks(treat, g)
            indices = set(base_ranks) | set(treat_ranks)
            if not indices:
                continue
            better_chunk_baseline_count = 0
            better_chunk_treatment_count = 0
            for i in indices:
                base_rank = base_ranks.get(i, top_k + 1)
                treat_rank = treat_ranks.get(i, top_k + 1)
                if treat_rank < base_rank:
                    better_chunk_treatment_count += 1
                elif base_rank < treat_rank:
                    better_chunk_baseline_count += 1
            out.append(ChunkWinLoss(
                query_id, alpha, g,
                better_chunk_baseline_count,
                better_chunk_treatment_count,
                better_chunk_treatment_count - better_chunk_baseline_count,
                len(indices),
            ))
    return out
