# Approfondimenti della nuova implementation roadmap

#### Chunk Expansion

- Contextual Retrieval helps the _retriever_ find the right chunk; chunk expansion helps the _generator_ understand it once found.
- Context window usage Chunk size ~ 200 tokens -> Top-K = 20, expansion ±2 → effectively 5× the text per hit -> 20 × 200 × 5 = **20k tokens**
- *Another deduplication but afterwards*: if you expand, chunks may have overlaps!

#### Wrong move with dedup

[[#Search e il problema del dedup]]

- **wrong choice** dictated by a single case with a wrong case and from a wrong idea: I need to fetch **the relevant information, not the relevant sources**
- You're enforcing **at most one chunk per document** in the final result set. That's fine for "give me diverse sources" queries, but it actively hurts you when:
	- The answer genuinely lives in _multiple_ parts of one document (a long technical doc, a meeting transcript, a chapter)
	- A document is the single best source and you'd benefit from 3 chunks from it rather than 1 from it + 2 from weaker docs

Anyway, dedupe alternatives are the following

- Cap per source at 2–3 instead of 1. Keeps diversity without starving good sources.
- Positional dedupe: only collapse chunks from the same source if they're _also_ adjacent or overlapping in the document (which expansion would merge anyway). Non-adjacent chunks from the same source are genuinely different information.
- MMR (Maximal Marginal Relevance) at the dedupe step: greedy selection trading off relevance vs. similarity to already-selected chunks. Solves the diversity problem in a more principled way than source-based capping.

#### Eval pipeline

- rollback dedup, it was solving [[Minerva RAG Log#Wrong move with dedup|the wrong problem]].
- lo stiamo facendo expansion? sospendi anche quello
- contextual size, lo stiamo limitando a 50-100 tokens?

- [[2026-05-10-Claude code reccommendations about a GOOD RAG]] - qui non c'e' un cazzo!
- https://www.anthropic.com/engineering/contextual-retrieval cites "1 minus recall @20" eval metric: % of docs that failed to be retrieved within top 20 chunks
- Eval pipeline: context precision and recall
- Cap 6 AIE page 169: factual consistency
- Minerva.Search.Bench automated test bench. Reads a queries file and a sweep config (TopK × HybridAlpha grid), runs the matrix, emits CSV with metrics (Recall@K, MRR if expected chunks are provided).
- Build a minimal eval first — even 20–30 hand-picked queries with known-good answers/sources is enough to start.

- eval pipeline: che differenza c'e' tra qwen e gemma3 - [[2026-05-05 what-about-a-smaller-model]]
- tutte le eval pipeline che ti vengono in mente
		- Model: evaluation pipeline; anche io dovrei fare una cosa del genere ma non l'ho fatta, anche perche' non sono sicuro di sapere cosa dovrei fare, probabilmente dovrei preparare delle query e vertificare che chromadb le ritorni corrette, magari anche cambiare modello di embedding e vedere se ce ne sono di migliori
		- guarda anche questa conversazione che hai fatto con claude a proposito del chunk size, accenna proprio a Recall e MRR (mean reciprocal rank) [[2026-05-05-A long context prefix is really useful?]]
- Ancora Claude sulle Eval: 
	- Baseline your current system on it.

#### Reranker

> A Reranker: extract more chunks but re-rank them with a recommender system -> context precision!

Con la query opportuna "Inghilterra uscita dall'europa" la nota giusta e' al primo posto!
Pero' il reranker forse servirebbe comunque, per una esperienza "completa".
Questo si combina con metadata: last updated time puo' essere rilevante per il ranking

Per usare il reranker dovrai alzare il numero di hits da tornare nelle prime fasi

- Hybrid search (semantic + BM25) on contextualized 200-token chunks → top 100–150 each
- RRF fusion → top 50–80
- Reranker on 50-80, cala se e' lento
- Ritorna top 10 o 20

#### Chunk size

chunks are too small? 
- how much space does Contextual Retrieval prefix take? reccomendation is 50-100 tokens
- the Contextual Retrieval prefix (Anthropic's example was ~50–100 tokens) is now a meaningful fraction of the embedded text — like 25–33%. That's actually _good_ for retrieval (the context is weighted heavily) but means your embeddings are skewing toward "what doc/section is this from" vs "what does this chunk literally say".


#### Expansion

return prev/next chunk id here, after you have the evals

5 chunks di context tornate alla LLM? Vedi le considerazioni su [[Minerva RAG Log#Wrong move with dedup]]: noi torniamo abbastanza per la risposta, se poi uno vuole il retrieve del document sono poi cazzi suoi
Quindi pensare di espandere l'espansione a 3 o 5 chunks e' un po' fuori luogo, rivalutalo, eventualmente fai come in minerva1, usa una stringa di chunk adiacenti invece che delle voci della tabella, e mettilo come parametro nelle opzioni; dovrai reindicizzare tutto eh

Prova +/- 1 ed eventualmente +/- 2, non di piu'

#### Restore dedupe?

Want to re-introduce dedupe? or maybe a different version of it? [[Minerva RAG Log#Wrong move with dedup|the wrong problem]]

#### Retune K of TopK at the rerank step

Puoi tornarne anche solo 8-10