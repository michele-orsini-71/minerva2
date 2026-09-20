# usage: python3 smoke.py <port> [model-name]
# Sends the Roche query plus three chunks to a llama-server /rerank endpoint.
# Pass = Roche_limit #1 scores clearly above Solar_eclipse #13 and scores are not all ~0 or identical.
import sys, re, json, subprocess, pathlib
port = sys.argv[1]; model = sys.argv[2] if len(sys.argv) > 2 else "x"
raw = pathlib.Path(__file__).with_name("roche.txt").read_text()
docs = {}
for p in re.split(r'^(?=[0-9a-f]{12}\|)', raw, flags=re.M):
    if p.strip():
        cid, src, idx, content = p.split('|', 3); docs[cid] = (f"{src} #{idx}", content.strip())
keep = ["72e773adbedd", "d98d96618827", "2edf336bbdc3"]
q = "why is there a closest distance a moon can orbit before it breaks into a ring of debris?"
body = json.dumps({"model": model, "query": q, "documents": [docs[k][1] for k in keep]})
out = subprocess.run(["curl", "-s", f"localhost:{port}/rerank", "-H", "content-type: application/json", "-d", body], capture_output=True, text=True).stdout
res = json.loads(out)
print("server model:", res.get("model"))
for r in sorted(res["results"], key=lambda r: -r["relevance_score"]):
    print(f"  {r['relevance_score']:+.3f}  {docs[keep[r['index']]][0]}")
