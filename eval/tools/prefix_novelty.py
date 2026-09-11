import argparse
import os
import random
import re
from collections import Counter
from dataclasses import dataclass
from statistics import mean

import psycopg

DEFAULT_DATABASE_URL = "postgresql://minerva:minerva@localhost/minerva"
MIN_WORD_LENGTH = 4                  # drops most function words without a stop list
WORD_PATTERN = re.compile(r"[^\W_]+")
SENTENCE_START_PATTERN = re.compile(r"(^|[.!?:]\s+)(\w)")


@dataclass(frozen=True)
class StoredChunk:
    chunk_id: str
    source_id: str
    content: str
    prefix: str

    @property
    def title(self) -> str:
        return self.source_id.removesuffix(".md").replace("_", " ")


@dataclass(frozen=True)
class NoveltyReport:
    chunk: StoredChunk
    prefix_words: list[str]
    novel_words: list[str]
    novel_entities: list[str]

    @property
    def novelty(self) -> float:
        return ratio(len(self.novel_words), len(self.prefix_words))

    @property
    def entity_novelty(self) -> float:
        return ratio(len(self.novel_entities), len(self.prefix_words))

    @property
    def has_novel_entity(self) -> bool:
        return len(self.novel_entities) > 0


def ratio(part: int, whole: int) -> float:
    return part / whole if whole else 0.0


def percentage(part: int, whole: int) -> float:
    return 100 * ratio(part, whole)


def lowercase_sentence_starts(text: str) -> str:
    # a capital after a full stop or colon is not a name, so it must not pass as an entity
    return SENTENCE_START_PATTERN.sub(lambda match: match.group(1) + match.group(2).lower(), text)


def words_of(text: str) -> list[str]:
    return [word for word in WORD_PATTERN.findall(text) if len(word) >= MIN_WORD_LENGTH]


def distinct_ignoring_case(words: list[str]) -> list[str]:
    first_seen: dict[str, str] = {}
    for word in words:
        first_seen.setdefault(word.lower(), word)
    return list(first_seen.values())


def is_known(word: str, known_text: str) -> bool:
    return word.lower() in known_text


def looks_like_entity(word: str) -> bool:
    return word[0].isupper() or any(character.isdigit() for character in word)


def analyze(chunk: StoredChunk) -> NoveltyReport:
    known_text = f"{chunk.content}\n{chunk.title}".lower()
    prefix_words = distinct_ignoring_case(words_of(lowercase_sentence_starts(chunk.prefix)))
    novel_words = [word for word in prefix_words if not is_known(word, known_text)]
    novel_entities = [word for word in novel_words if looks_like_entity(word)]
    return NoveltyReport(chunk, prefix_words, novel_words, novel_entities)


def load_chunks(database_url: str, collection: str) -> list[StoredChunk]:
    query = """
        select id, source_id, content, contextual_prefix
        from chunks
        where collection_name = %s and contextual_prefix is not null
    """
    with psycopg.connect(database_url) as connection:
        rows = connection.execute(query, (collection,)).fetchall()
    return [StoredChunk(*row) for row in rows]


def print_summary(collection: str, reports: list[NoveltyReport]) -> None:
    with_novel_entity = sum(report.has_novel_entity for report in reports)
    print(f"{collection}: {len(reports)} chunks with a prefix")
    print(f"  words per prefix                 {mean(len(r.prefix_words) for r in reports):6.1f}")
    print(f"  novelty, any word                {mean(r.novelty for r in reports):6.3f}")
    print(f"  novelty, entities and numbers    {mean(r.entity_novelty for r in reports):6.3f}")
    print(f"  prefixes with a novel entity     {percentage(with_novel_entity, len(reports)):5.1f}%")


def print_most_common_novel_words(reports: list[NoveltyReport], limit: int) -> None:
    counter = Counter(word.lower() for report in reports for word in report.novel_words)
    print(f"\nmost common novel words")
    for word, count in counter.most_common(limit):
        print(f"  {word:20} {count}")


def print_samples(reports: list[NoveltyReport], count: int, seed: int) -> None:
    print(f"\n{count} random prefixes")
    for report in random.Random(seed).sample(reports, min(count, len(reports))):
        print(f"\n== {report.chunk.source_id}")
        print(report.chunk.prefix)
        print(f"-- novel words:    {' '.join(report.novel_words) or '(none)'}")
        print(f"-- novel entities: {' '.join(report.novel_entities) or '(none)'}")


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Measures how much a stored contextual prefix adds beyond its chunk.")
    parser.add_argument("collection")
    parser.add_argument("--database-url",
                        default=os.environ.get("MINERVA_DATABASE_URL", DEFAULT_DATABASE_URL))
    parser.add_argument("--top-words", type=int, default=20)
    parser.add_argument("--samples", type=int, default=6)
    parser.add_argument("--seed", type=int, default=0)
    return parser.parse_args()


def main() -> None:
    arguments = parse_arguments()
    chunks = load_chunks(arguments.database_url, arguments.collection)
    if not chunks:
        print(f"no chunk with a prefix in collection {arguments.collection}")
        return
    reports = [analyze(chunk) for chunk in chunks]
    print_summary(arguments.collection, reports)
    print_most_common_novel_words(reports, arguments.top_words)
    print_samples(reports, arguments.samples, arguments.seed)


if __name__ == "__main__":
    main()
