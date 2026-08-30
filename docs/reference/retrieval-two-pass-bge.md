# Two-pass retrieval, rerankers, and BGE-M3

Background on the retrieval architecture Minerva draws on, and why specific
choices were made. Reranking is **deferred to roadmap Phase 2**; this
document explains what it is and how it would fit.

## Bi-encoder vs cross-encoder

The two-pass pattern is not just "fast model then heavy model" — the two
passes differ architecturally in how they compute similarity.

**Bi-encoder (the embedder, pass 1).** Encodes query and document
**independently** into separate vectors; similarity is cosine/dot product.
Fast, because all document embeddings are precomputed offline and only the
query is encoded at query time. But the two texts never see each other during
encoding — no cross-attention.

**Cross-encoder (the reranker, pass 2).** Feeds query + document **together**
as one input; attention layers attend across both texts, capturing
fine-grained interactions (word-level matching, negation, paraphrase).
Outputs a single relevance score, not an embedding. Much more accurate, but
needs one full forward pass per candidate — no precomputation.

**Why two stages.** A cross-encoder over 100k documents is 100k forward
passes — impractical. The bi-encoder narrows to, say, the top 100 cheaply;
the cross-encoder re-scores only those 100. This is exactly the shape Phase 2
adds to Minerva: widen the candidate pool, fuse, then rerank down to the
final K.

## BGE-M3 — what it is, and why Minerva does not depend on it

BGE = BAAI General Embedding, a *family of embedding models* (the library is
`FlagEmbedding`). It is not a database or retrieval engine — purely
text → vectors (and, separately, rerankers). Models are standard HuggingFace
checkpoints that run locally.

**bge-m3** = Multi-lingual, Multi-functional, Multi-granularity. One forward
pass yields three representations simultaneously:

- **Dense** — one ~1024-dim vector, cosine similarity (the classic approach).
- **Sparse** — learned sparse vectors (a neural BM25): a vocabulary-sized
  vector, mostly zeros, with learned weights on present tokens. Captures
  lexical/keyword matches dense embeddings miss.
- **ColBERT / multi-vector** — one vector per token; at scoring time each
  query token finds its best-matching document token (late interaction).
  Finer-grained, more expensive.

The smaller family members (`bge-small/base/large-en-v1.5`) are English-only,
dense-only, smaller dimensions, 512-token context.

**Why Minerva rejected it as a dependency.** BGE-M3 would natively support
hybrid retrieval, but exposing its sparse output requires a specialized model
server (not the OpenAI-compatible `/v1/embeddings` API Minerva targets), and
adopting it would lock the user into one embedding model. Minerva instead
gets the keyword leg from BM25 in PostgreSQL (`pg_search`), keeping model
choice free. See [architecture.md](architecture.md) and
[full-text-search.md](full-text-search.md).

## How a fully open-source stack would look

Contextual Retrieval and BGE-M3 solve **different layers** and are
complementary: Contextual Retrieval is preprocessing *before* embedding (an
LLM prepends a short context prefix to each chunk); BGE-M3 is the
embedding/retrieval engine. A minimal locally-runnable stack:

1. **Contextual preprocessing** (local LLM, or Claude API with prompt caching)
2. **FlagEmbedding / BGE-M3** for embedding + sparse retrieval
3. **A BGE reranker** for the cross-encoder pass
4. **A vector DB** (Milvus has native hybrid support for BGE-M3 dense+sparse)
5. **An MCP server** wrapping search

Minerva realizes this pattern with different component choices: PostgreSQL
instead of Milvus, BM25 instead of BGE sparse vectors, and any
OpenAI-compatible embedder instead of mandatory BGE-M3.
