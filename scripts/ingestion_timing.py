#!/usr/bin/env python3
"""
Ingestion-duration analysis across collections.

Pulls per-source insertion times from `chunks`, computes per-source gaps
within each collection, and renders:

  1. A log-scale histogram of gaps per collection (median marked).
  2. A scatter of gaps for the same source across two collections — points
     above y=x reveal per-mode cost differences; points far off in either
     corner are restart pauses.

Why per-source and not per-chunk: Postgres `NOW()` returns transaction
start time, so every chunk inserted in one UpsertChunksAsync transaction
shares one timestamp. Per-source is the actual resolution available.

Dependencies:
    pip install psycopg2-binary pandas matplotlib

Connection: env vars MINERVA_PGHOST / MINERVA_PGDATABASE / MINERVA_PGUSER /
MINERVA_PGPASSWORD; defaults match Minerva.Search.Cli/appsettings.json.
"""

import os
import numpy as np
import psycopg2
import pandas as pd
import matplotlib.pyplot as plt


DSN = {
    "host": os.environ.get("MINERVA_PGHOST", "localhost"),
    "dbname": os.environ.get("MINERVA_PGDATABASE", "minerva"),
    "user": os.environ.get("MINERVA_PGUSER", "minerva"),
    "password": os.environ.get("MINERVA_PGPASSWORD", "minerva"),
}

QUERY = """
SELECT collection_name, source_id, MIN(created_at) AS created_at
FROM chunks
GROUP BY collection_name, source_id
ORDER BY collection_name, created_at
"""


def load() -> pd.DataFrame:
    with psycopg2.connect(**DSN) as conn, conn.cursor() as cur:
        cur.execute(QUERY)
        rows = cur.fetchall()
    return pd.DataFrame(rows, columns=["collection_name", "source_id", "created_at"])


def compute_gaps(df: pd.DataFrame) -> pd.DataFrame:
    df = df.sort_values(["collection_name", "created_at"]).copy()
    df["gap_s"] = (
        df.groupby("collection_name")["created_at"]
          .diff()
          .dt.total_seconds()
    )
    return df.dropna(subset=["gap_s"])


def find_valley(gaps: pd.Series, n_bins: int = 50) -> float | None:
    """
    Locate the log-space valley between a primary and secondary peak.
    Returns None when the distribution looks unimodal.
    """
    log_gaps = np.log10(gaps)
    counts, edges = np.histogram(log_gaps, bins=n_bins)
    centers = (edges[:-1] + edges[1:]) / 2

    peak1 = int(np.argmax(counts))
    min_sep = max(n_bins // 10, 1)  # secondary peak must be ≥10% of range away

    candidates = [i for i in range(n_bins) if abs(i - peak1) >= min_sep]
    if not candidates:
        return None
    peak2 = max(candidates, key=lambda i: counts[i])

    # Require the secondary peak to carry real mass — at least 20% of primary.
    if counts[peak2] < counts[peak1] * 0.2:
        return None

    lo, hi = sorted([peak1, peak2])
    valley_idx = lo + int(np.argmin(counts[lo:hi + 1]))
    return float(10 ** centers[valley_idx])


def print_summary(df: pd.DataFrame):
    summary = (
        df.groupby("collection_name")["gap_s"]
          .describe(percentiles=[0.5, 0.9, 0.99])
          .round(2)
    )
    print(summary)
    print()
    medians = df.groupby("collection_name")["gap_s"].median().sort_values()
    print("Median-gap ratios (relative to fastest):")
    base = medians.iloc[0]
    for coll, m in medians.items():
        print(f"  {coll:<30}  {m:>8.2f}s   ×{m / base:.2f}")
    print()
    print("Bimodal split (valley detected between fast/slow paths):")
    for coll in sorted(df["collection_name"].unique()):
        gaps = df.loc[df["collection_name"] == coll, "gap_s"]
        threshold = find_valley(gaps)
        if threshold is None:
            print(f"  {coll:<30}  unimodal — "
                  f"n={len(gaps)}, median={gaps.median():.3f}s")
            continue
        fast = gaps[gaps < threshold]
        slow = gaps[gaps >= threshold]
        print(f"  {coll:<30}  valley={threshold:.2f}s")
        print(f"    fast: n={len(fast):>5}, "
              f"median={fast.median():.3f}s, total={fast.sum():.1f}s")
        print(f"    slow: n={len(slow):>5}, "
              f"median={slow.median():.2f}s,  total={slow.sum():.1f}s")


def plot_histograms(df: pd.DataFrame):
    collections = sorted(df["collection_name"].unique())
    fig, axes = plt.subplots(
        1, len(collections), figsize=(5 * len(collections), 4), sharey=True
    )
    if len(collections) == 1:
        axes = [axes]
    for ax, coll in zip(axes, collections):
        gaps = df.loc[df["collection_name"] == coll, "gap_s"]
        bins = np.logspace(np.log10(gaps.min()), np.log10(gaps.max()), 40)
        ax.hist(gaps, bins=bins)
        ax.set_xscale("log")
        median = gaps.median()
        ax.axvline(median, color="red", linestyle="--", lw=1,
                   label=f"median {median:.2f}s")
        valley = find_valley(gaps)
        if valley is not None:
            ax.axvline(valley, color="green", linestyle=":", lw=1,
                       label=f"valley {valley:.2f}s")
        ax.set_title(f"{coll}  (n={len(gaps)})")
        ax.set_xlabel("gap to previous source (s, log)")
        ax.set_ylabel("count")
        ax.legend()
    fig.suptitle("Per-source ingestion gaps by collection")
    fig.tight_layout()
    return fig


def plot_scatter(df: pd.DataFrame):
    pivoted = df.pivot_table(
        index="source_id", columns="collection_name", values="gap_s"
    ).dropna()
    if pivoted.shape[1] < 2:
        return

    cols = sorted(pivoted.columns)
    x_coll, y_coll = cols[0], cols[-1]  # compare the two extremes

    fig, ax = plt.subplots(figsize=(6, 6))
    ax.scatter(pivoted[x_coll], pivoted[y_coll], alpha=0.6, s=20)
    lo = min(pivoted[x_coll].min(), pivoted[y_coll].min())
    hi = max(pivoted[x_coll].max(), pivoted[y_coll].max())
    ax.plot([lo, hi], [lo, hi], "k--", lw=1, label="y = x (no per-mode cost)")
    ax.set_xscale("log")
    ax.set_yscale("log")
    ax.set_xlabel(f"gap in {x_coll} (s)")
    ax.set_ylabel(f"gap in {y_coll} (s)")
    ax.set_title(f"Same-source ingestion gap: {x_coll} vs {y_coll}")
    ax.legend()
    fig.tight_layout()
    return fig


OUTPUT_DIR = os.path.dirname(os.path.abspath(__file__))


def main():
    df = load()
    df = compute_gaps(df)
    if df.empty:
        print("No gaps to plot — fewer than 2 sources per collection.")
        return
    print_summary(df)

    hist_fig = plot_histograms(df)
    scatter_fig = plot_scatter(df)

    hist_path = os.path.join(OUTPUT_DIR, "ingestion_gaps_histograms.png")
    hist_fig.savefig(hist_path, dpi=150, bbox_inches="tight")
    print(f"\nSaved {hist_path}")

    if scatter_fig is not None:
        scatter_path = os.path.join(OUTPUT_DIR, "ingestion_gaps_scatter.png")
        scatter_fig.savefig(scatter_path, dpi=150, bbox_inches="tight")
        print(f"Saved {scatter_path}")

    plt.show()


if __name__ == "__main__":
    main()
