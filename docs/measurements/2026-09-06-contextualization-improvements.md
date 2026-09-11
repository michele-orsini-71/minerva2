---
parent: "[[Minerva RAG System]]"
---
# Crafting a contextualization routine for small local models

Date: 2026-09-05 / 2026-09-06. Model under test: Gemma 4 E2B in LM Studio, no thinking.
Corpus: wp111 (111 Wikipedia articles, 7776 chunks of about 820 chars).

## The initial problem

Contextualization adds an LLM-written prefix to every chunk before embedding and
BM25 indexing. The eval on wp111 compared three collections: no prefix, prefixes by
Gemma 4 E2B, prefixes by Qwen3 1.7B. The gain was small and the experiment could not
show more:

| alpha | no prefix | Gemma E2B | Qwen3 1.7B |
|---|---|---|---|
| 0.3 | 0.611 | 0.674 | 0.699 |
| 0.5 | 0.687 | 0.738 | 0.739 |
| 0.7 | 0.707 | 0.751 | 0.709 |

(MRR@10, 18 queries.) Recall@10 was 1.0 for every collection at alpha 0.5 and above.
Thirteen of eighteen queries had the gold at rank 1 or 2 in every cell. The three hard
queries failed identically everywhere, because a competing article matched the query
terms better. There was no headroom for a prefix to show its value.

Reading the stored prefixes explained the small effect:

- The contextualizer never saw the document. Articles are split into 8000-char
  segments, each segment is summarized on its own, and the prefix situates the chunk
  inside that segment. 109 of 111 articles are larger than one segment.
- The prefixes were meta-descriptions: "This chunk provides a detailed overview of
  dinosaurs, covering their classification, evolutionary relationships, morphological
  features...". Category words nobody types in a query, and no article name when the
  segment was about a sub-topic.

Decisions taken on the way: Qwen dropped, Gemma E2B kept. Whole-document prompting
(the Anthropic recipe) rejected, because a 2B model loses accuracy on 10k-token inputs
and Minerva must stay light. The full-corpus ingestion with the old prompt started
anyway, as a baseline for the new one.

## Where we want to arrive

A prefix helps retrieval only when it adds words that a query would use and that the
chunk itself lacks. Which words are missing depends on the source. For an encyclopedia 
article it is mostly the article title, then the section topic, the entities a 
pronoun refers to, and synonyms of the subject. For personal notes or internal documentation
 the title is often weak and the missing words are the project, the people, the product 
 or the component; the document summary supplies those. The mechanism is the same in every case.
It should not restate the chunk, and it should not describe the chunk.

Target shape, one or two sentences:

> Quinine, antimalarial alkaloid from cinchona bark. History: William Perkin's 1856
> attempt to synthesize quinine produced mauveine, the first synthetic dye; British
> officers in India mixed quinine tonic with gin.

Every noun in it is a possible query term. The value shows on the dense side: the
prefix pulls each chunk's embedding toward its own article, so off-topic chunks of a
competing article match less. BM25 can only gain terms, it cannot lose any.

## Method

Ingestion is the expensive step: 4 to 5 hours for wp111, 50 to 60 hours for the full
corpus. Contextualization is text in, text out, so it can be studied without ingesting.
A small Python tool ran summarizer and contextualizer against LM Studio on a few
articles and printed the prefixes with their length. One run takes 5 to 15 minutes.
Seven runs were needed. Each run was judged by reading the prefixes and by four
counters: mean length, prefixes under 100 chars, prefixes containing a literal
"Section:", prefixes containing "this chunk".

## Part one: what the model should see (summary scope)

Three inputs were compared on the same chunks, same prompt.

- **A, segment summary.** What the C# pipeline does today.
- **B, document summary.** Built hierarchically: every segment is summarized, then the
  segment summaries are summarized once more. One extra LLM call per document, always
  fits the context window, and a 2B model reads a 1k-token summary better than a
  45k-char article.
- **C, document summary plus the chunk's own segment summary.** Global and local frame.

Finding, once the prompt was good enough to make the differences visible:

- A loses the article subject whenever the segment is about a sub-topic. The history
  segment of the dinosaur article produced "Scientific discovery: William Buckland..."
  with no "Dinosaur" in it. The one term every query about the article contains was
  missing from the prefix.
- B always names the subject but sometimes drops specifics.
- C keeps the subject and carries the most specifics: population estimates, the twelve
  synapomorphies, the full host list of a disease.

C was chosen. The sampling noise had to be removed first: at the default temperature,
identical inputs gave outputs differing by 30 to 40 chars, the same size as the
differences between A, B and C. Temperature 0 made the comparison readable.

## Part two: how to ask (prompt iterations)

The summary scope turned out to matter less than the prompt wording. Seven iterations,
with what each one taught.

**1. Original prompt (Anthropic's contextual retrieval prompt, verbatim).**
"Please give a short succinct context to situate this chunk within the overall document
for the purposes of improving search retrieval of the chunk." Output: "This chunk
provides a detailed overview of..." A 2B model reads this as "describe the chunk in
abstract terms".

**2. Negative instruction.** Added "State the document's subject, the part of the
document the chunk belongs to, and the specific entities... Do not describe the chunk;
do not start with 'This chunk'." Output: "This chunk belongs to the section detailing
the scientific understanding and debates surrounding dinosaurs...". Worse. Small models
follow negative instructions badly, the prohibition puts the forbidden words in front
of the model, and the model invented section names because nothing told it the section.

**3. One-shot example plus a short output form, temperature 0.** "Answer with one or
two sentences, at most 60 words, in this form: `<document subject>. <section topic>:
<the specific names, terms, dates and claims found in the chunk>`", followed by the
Quinine example. Output: "Dinosaurs: Classification and Extinction. Dinosaurs are a
diverse group of warm-blooded reptiles in the clade Dinosauria...". The meta-opening
disappeared, specifics appeared. Showing the shape works where forbidding a shape
fails. This is also the run where A, B and C became distinguishable.

**4. Same prompt, more articles, tables and lists included, variant C only.**
119 prefixes, mean 249 chars, none under 100, no literal "Section:", 5 with "this
chunk" (all on tables or list fragments). One defect: 25 of 119 prefixes started with a
section word instead of the article name. The model guessed the subject, and guessed
the section when the section summary was strong. The title has to be given, not guessed.

**5. Title added, with long placeholders.** The form line became three lines of long
bracketed descriptions ("`<document title, copied exactly from the document_title
tag>, <what the document is about in a few words>. <section topic>: ...`"). Output:
"Dinosaur, diverse group of reptiles. Section: Dinosaur evolution", for a chunk full of
named dinosaur groups and continents. 19 prefixes under 100 chars, 35 with a literal
"Section:", mean length down to 178. The model stopped imitating the example and
started imitating the form line itself, writing its skeleton and stopping after the
placeholder it could fill. Long placeholders turn a form into a template to copy.

**6. Prose instruction, no form line.** The format described in a sentence, example
kept. Length recovered (mean 238) but 62 of 117 prefixes went back to "This chunk
discusses..." and the section topic vanished. Without a shape the model returns to
its default narration. The short form line in iteration 3 was doing real work.

**7. Iteration 3 prompt with the smallest change.** `<document subject>` replaced by
`<document title>`, a `document_title` tag added first in the data block, and a code
guard that prepends the title when the answer does not start with it. Counters back to
iteration 4 level: mean 242, none under 100, no "Section:", 3 with "this chunk", title
present in all 117. One new artifact: in 35 cases the model copied the placeholder's
angle brackets, "`<Dinosaur>.`". Angle brackets meant two things in the prompt, data
tags and placeholders. Fixed in code by unwrapping a bracketed title before the guard,
rather than by another prompt change.

Final prompt, kept as the reference:

```
You write index annotations for text chunks. Given a document title, a document
summary and a chunk, answer with one or two sentences, at most 60 words, in this form:
<document title>. <section topic>: <the specific names, terms, dates and
claims found in the chunk>.
Example answer for the document titled "Quinine":
<annotation>
Quinine, antimalarial alkaloid from cinchona bark. History: William Perkin's
1856 attempt to synthesize quinine produced mauveine, the first synthetic dye;
British officers in India mixed quinine tonic with gin.
</annotation>
Answer only with the annotation.
```

Data block: title, document summary, section summary (omitted for single-segment
documents), chunk, each in its own tag. Post-processing: strip a copied annotation tag,
unwrap a bracketed title, prepend the title if missing.

## Summary of the counters

| run | change | mean chars | under 100 | literal "Section:" | "this chunk" |
|---|---|---|---|---|---|
| 4 | example + short form, C only | 249 | 0 | 0 | 5 |
| 5 | title with long placeholders | 178 | 19 | 35 | 2 |
| 6 | prose instruction, no form | 238 | 0 | 0 | 62 |
| 7 | short form + title + guard | 242 | 0 | 0 | 3 |

## What holds for small models

- Old prompting practices are not obsolete for a 2B model: tagged blocks, one-shot
  example, positive phrasing, a fixed output form, temperature 0.
- Show the shape, do not forbid a shape.
- Keep placeholders short. A long placeholder becomes the answer.
- Do not use the same brackets for data tags and placeholders.
- Anything deterministic goes in the prompt as data and in code as a guard: the title
  is known, so give it and enforce it.
- Judge a prompt on counters over a hundred outputs, not on three examples. The
  regressions in runs 5 and 6 were invisible in the first ten prefixes.
- Check the inputs before judging the outputs (run 1 passed the wrong chunk).
- Study text-to-text steps with a text-to-text tool. Seven prompt iterations cost
  about an hour of machine time; one ingestion would have cost a night each.

## Still open

- The section topic is still guessed from content. Table rows and list fragments get
  wrong guesses. Passing the real markdown heading path would remove the guess. Deferred
  until the new prompt is measured on the full corpus.
- About 3% of prefixes still say "this table" or "this chunk", all on tables and lists.
  Acceptable.
- Temperature 0 in LM Studio is not fully deterministic; word-level differences remain.
- None of this is measured on retrieval yet. The small corpus has no headroom. The
  test is the full corpus, new prompt against no prefix and against the old prompt,
  judged by recall into the reranker's candidate pool rather than by MRR@10.

## Full-corpus result and verdict

Date: 2026-09-10 / 2026-09-11. Corpus: wp1283 (1283 articles, 72,252 chunks).
Dataset wp1283-v1, 84 queries. Pool 100, top_k 50, reranker off, alpha 0.3 / 0.5 / 0.7.
Three collections of the same corpus: no prefix (nollm), the old prompt (e2b, segment
summary only) and the new prompt (e2b-v2, document plus section summary, title
enforced, temperature 0). Both prefixed collections were written by Gemma 4 E2B; the
full e2b-v2 ingestion took about 55 hours.

### Retrieval metrics

| alpha | collection | R@5 | R@10 | R@20 | MRR@10 |
|---|---|---|---|---|---|
| 0.3 | nollm | 0.859 | 0.899 | 0.917 | 0.771 |
| 0.3 | e2b | 0.889 | 0.911 | 0.911 | 0.731 |
| 0.3 | e2b-v2 | 0.889 | 0.899 | 0.911 | 0.751 |
| 0.5 | nollm | 0.937 | 0.988 | 1.000 | 0.769 |
| 0.5 | e2b | 0.929 | 0.958 | 0.976 | 0.781 |
| 0.5 | e2b-v2 | 0.937 | 0.976 | 0.976 | 0.779 |
| 0.7 | nollm | 0.966 | 0.984 | 1.000 | 0.794 |
| 0.7 | e2b | 0.937 | 0.964 | 1.000 | 0.796 |
| 0.7 | e2b-v2 | 0.944 | 0.966 | 0.982 | 0.800 |

With 84 queries one gold moving from rank 2 to rank 1 changes MRR@10 by 0.006, so the
MRR differences at alpha 0.5 and 0.7 are one query. Recall is best without a prefix at
every alpha.

### Covering rank

Covering rank is the smallest k whose top-k holds every gold of a query. It is the
metric that matters for the reranker: it says how long the candidate list must be.

| alpha | collection | median | p95 | max | golds beyond 50 |
|---|---|---|---|---|---|
| 0.3 | nollm | 1 | 35.8 | 49 | 0 |
| 0.3 | e2b | 1 | 34.9 | 51 | 1 |
| 0.3 | e2b-v2 | 1 | 36.8 | 48 | 0 |
| 0.5 | nollm | 1 | 8.7 | 17 | 0 |
| 0.5 | e2b | 1 | 7.0 | 32 | 0 |
| 0.5 | e2b-v2 | 1 | 8.8 | 40 | 0 |
| 0.7 | nollm | 1 | 4.8 | 13 | 0 |
| 0.7 | e2b | 1 | 6.8 | 20 | 0 |
| 0.7 | e2b-v2 | 1 | 5.8 | 31 | 0 |

Per query, against nollm, a prefix gives many gains of one or two ranks inside the
top 5 and a few losses of 15 to 35 ranks that cross the recall thresholds. Sum of rank
movement (better / worse):

| alpha | e2b | e2b-v2 |
|---|---|---|
| 0.3 | 74 / 66 | 63 / 77 |
| 0.5 | 29 / 59 | 29 / 62 |
| 0.7 | 16 / 35 | 19 / 60 |

The gains shrink as alpha grows, so the prefix helped the lexical leg more than the
dense one, the opposite of the hypothesis in "Where we want to arrive". The largest
gains are chunks that lacked the article name (Antarctica.md-3: rank 36 to 3 at alpha
0.3). The largest losses are queries whose gold is beaten by neighbours that share
the subject words the prefix adds to every chunk (A_Fistful_of_Dollars.md-1: 5 to 40
at alpha 0.5; Virus.md-1: 5 to 23 at alpha 0.7, e2b-v2). The queries that stay hard
in every cell (A_Fistful_of_Dollars, Tharman_Shanmugaratnam, HIV) are the same three
the reranker failed on in August.

### Prefix audit on the stored prefixes

The counters used to select the prompt measured shape, not content. A new measure was
computed in SQL and then in `eval/tools/prefix_novelty.py` over all 72,251 stored
prefixes of each collection, with no LLM call. A prefix word (4+ characters, distinct,
sentence-initial capitals lowered) is novel when it is absent from the chunk and from
the title. An entity is a novel word that is capitalized or contains a digit.

| collection | words per prefix | novelty, any word | novelty, entities and numbers | prefixes with a novel entity |
|---|---|---|---|---|
| e2b | 22.3 | 0.548 | 0.010 | 14.2% |
| e2b-v2 | 20.3 | 0.226 | 0.011 | 13.1% |

The novel words of e2b are "this", "chunk", "details", "section", "discusses": the
narration. The novel words of e2b-v2 are "like", "including", "with", "details",
"various": the connectives of a paraphrase. On the measure that matters, entities and
numbers, the two prompts are identical: one prefix in eight names one thing the chunk
does not contain. Example, e2b-v2, George_Michael.md chunk 25:

> George Michael. 1990–1993: *Listen Without Prejudice Vol. 1*, *Red Hot + Dance* and
> *Five Live*: Details about live recordings from the Freddie Mercury Tribute Concert,
> including tracks like "Somebody to Love" and "These Are the Days of Our Lives," and
> sales performance of the *Five Live* EP.

The only contextualization is "George Michael"; the heading is copied from the chunk;
the rest restates the chunk. The document summary added in v2 left no trace in the
output. This is not a Gemma E2B defect: the v2 form line asked for "the specific names,
terms, dates and claims found in the chunk", and the model obeyed. The Quinine example
in the prompt has the same shape. The principle in "Where we want to arrive" was
stated and never encoded in the prompt.

A probe with the original Anthropic prompt on one bay cat chunk, whole article given:
Haiku, Sonnet without thinking and Gemma 26B returned a chunk summary; Sonnet with
thinking a hybrid; only Gemini returned a context ("a rare wild cat endemic to
Borneo", the article's first sentence). The prompt never defines the deliverable, so
each model falls back to its habit, which is to summarize. Model size decides the
quality of the summary, not whether it is one.

### What a context is, and why this corpus cannot show it

A context differs from a document brief in degree, not in kind. Every chunk of an
article shares the same document identity, so part of any context is the same for all
its chunks. Three things separate a useful context from the DocumentBrief that failed
on 2026-09-04: length (a few words on a 150-word chunk, not a quarter of the indexed
text), word rarity ("Borneo", "Catopuma" against "status", "distribution",
"conservation"), and a per-chunk part that resolves references the chunk makes
("Michael", "this cat", "the previous quarter") from the rest of the document.
Anthropic's own example has exactly this shape: document identity plus one resolved
reference.

Wikipedia chunks are packed by heading and paragraph, so they rarely open with an
unresolved reference; chunk overlap covers the antecedent when it sits in the previous
chunk; and the shared identity is the title, already in the heading path. A correct
context on this corpus degenerates to "title plus a few keywords", a tiny brief, and
the gain is bounded by what those words can do. The corpus where a context could pay
is one whose chunks say "the client", "the service", "we decided": notes, tickets,
internal documentation.

### Verdict

Contextualization is removed from Minerva (decision 2026-09-11). Reasons:

- No retrieval gain on the full corpus after two prompt generations; recall and
  covering rank are best without a prefix.
- The candidate pool does not need it: at alpha 0.7 without a prefix every gold sits
  within rank 13, so a reranker list of a few dozen hits misses nothing.
- Minerva is queried through an agent, which reformulates and searches several times;
  single-query rank gains of one or two positions matter less there.
- Cost: 55 to 60 hours of ingestion per corpus, two weeks of prompt work, and the
  whole LLM provider stack in the ingestion path for one feature.

Kept: the reranker, the covering-rank metric, the runs of the three collections. The
prefix column and the BM25 index expression from migration 005 are dropped;
`prefix_novelty.py` reads that column, so it is deleted in the same commit and lives in
history from the commit that added it.

Conditions to reopen: a private corpus whose chunks make unresolved references, an
eval on it showing golds missing from the pool, and a deterministic "title plus first
paragraph" prefix measured first as the control, at no LLM cost. Judge by covering
rank and by the novelty measure, not by MRR@10.
