#!/usr/bin/env python3
"""
Tiny RRF playground.

Reads two ranked lists from `fts.txt` and `vector.txt` in the current
directory (one id per line, top-ranked first) and prints the fused
ranking using the same formula as Minerva's RankFusion.Fuse.

    score(id) = alpha       * 1 / (k + vectorRank)
              + (1 - alpha) * 1 / (k + ftsRank)

Missing ids are given rank = max(len(vec), len(fts)) + 1.

Tweak ALPHA and K below and re-run to see how the order shifts.
"""

ALPHA = 0.5  # 1.0 = pure vector, 0.0 = pure fts
K = 60       # standard RRF constant


def read_ids(path):
    with open(path) as f:
        return [line.strip() for line in f if line.strip()]


def fuse(vector_ids, fts_ids, alpha=ALPHA, k=K):
    missing = max(len(vector_ids), len(fts_ids)) + 1

    # rank starts at 1 for the top result
    vector_rank = {id_: i + 1 for i, id_ in enumerate(vector_ids)}
    fts_rank = {id_: i + 1 for i, id_ in enumerate(fts_ids)}

    all_ids = set(vector_rank) | set(fts_rank)

    scored = []
    for id_ in all_ids:
        vr = vector_rank.get(id_, missing)
        fr = fts_rank.get(id_, missing)
        score = alpha / (k + vr) + (1 - alpha) / (k + fr)
        scored.append((id_, score, vr, fr))

    scored.sort(key=lambda x: x[1], reverse=True)
    return scored


def main():
    vector_ids = read_ids("vector.txt")
    fts_ids = read_ids("fts.txt")

    print(f"alpha={ALPHA}  k={K}")
    print(f"vector ({len(vector_ids)}): {vector_ids}")
    print(f"fts    ({len(fts_ids)}): {fts_ids}")
    print()
    print(f"{'rank':>4}  {'id':<8}  {'score':>10}  {'vRank':>6}  {'fRank':>6}")
    print("-" * 44)
    for i, (id_, score, vr, fr) in enumerate(fuse(vector_ids, fts_ids), start=1):
        print(f"{i:>4}  {id_:<8}  {score:>10.6f}  {vr:>6}  {fr:>6}")


if __name__ == "__main__":
    main()
