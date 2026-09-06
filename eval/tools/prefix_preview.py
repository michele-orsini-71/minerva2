import re
from openai import OpenAI
from openai.types.chat import ChatCompletion
from pathlib import Path
from dataclasses import dataclass

SEGMENT_LENGTH = 8000
CHUNK_LENGTH = 1200
MAX_SEGMENTS_PER_DOC = 4      # cap LLM calls for a first look
MAX_CHUNKS_PER_SEGMENT = 3
MIN_CHUNK_CHARS = 200         # skips front matter and heading-only chunks

SUMMARIZE_PROMPT = """Summarize the following document in one paragraph. Focus on the main topics,
key concepts, and structure. This summary will be used to provide context
when embedding individual chunks of this document."""

files = [
    Path("eval/collections/wikipedia-small-en-corpus/Dinosaur.md"),
    Path("eval/collections/wikipedia-small-en-corpus/Glanders.md"),
    Path("eval/collections/wikipedia-small-en-corpus/bird.md"),
    Path("eval/collections/wikipedia-small-en-corpus/Jamaica.md"),
]

def split_string(string:str, lenght:int) -> list[str]:
    return [ string[0+i:lenght+i] for i in range(0, len(string), lenght)]

def split_paragraphs(text: str, budget: int) -> list[str]:
    # pack whole paragraphs (blank-line separated) up to budget chars;
    # a single paragraph longer than the budget is hard-sliced
    pieces: list[str] = []
    current = ""
    for paragraph in text.split("\n\n"):
        paragraph = paragraph.strip()
        if not paragraph:
            continue
        if len(paragraph) > budget:
            if current:
                pieces.append(current)
                current = ""
            pieces.extend(split_string(paragraph, budget))
            continue
        candidate = paragraph if not current else current + "\n\n" + paragraph
        if len(candidate) > budget:
            pieces.append(current)
            current = paragraph
        else:
            current = candidate
    if current:
        pieces.append(current)
    return pieces

client = OpenAI(base_url="http://localhost:1234/v1", api_key="LMStudio")
# response:ChatCompletion = client.chat.completions.create(model="google/gemma-4-e2b", messages=[{"role": "user", "content": "Say hello in three words."}])
# print(response.choices[0].message.content)

def callLMSummary(document:str) -> str:
    response:ChatCompletion = client.chat.completions.create(model="google/gemma-4-e2b", temperature=0, messages=[
        { "role": "system", "content": SUMMARIZE_PROMPT },
        { "role": "user", "content": document}])
    return response.choices[0].message.content or ""

def callLLMContextualize(title: str, summary: str, chunk: str) -> str:
    prompt = f"""
<document_title>
{title}
</document_title>
<document>
{summary}
</document>
Here is the chunk we want to situate within the whole document:
<chunk>
{chunk}
</chunk>
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
"""

    response:ChatCompletion = client.chat.completions.create(model="google/gemma-4-e2b", temperature=0, messages=[
        { "role": "user", "content": prompt}])
    if response.choices[0].message.content:
        contextualization = response.choices[0].message.content.strip().removeprefix("<annotation>").removesuffix("</annotation>").strip()
        contextualization = re.sub(rf"^<\s*{re.escape(title)}\s*>", title, contextualization, flags=re.IGNORECASE)
        if not contextualization.lower().startswith(title.lower()):
            print(f"correction adding the title {title}")
            contextualization = title + ", " + contextualization
        return contextualization

    return ""

def sort_chunks(chunks: list[str]) -> list[str]:
    special = [c for c in chunks if "\n|" in c or "\n- " in c or "\n* " in c]
    regular = [c for c in chunks if c not in special]
    return (special + regular)


@dataclass
class DocumentFragment:
    segment: str
    document_title: str
    chunks:list[str]
    chunk_contexts:list[str]
    extended_chunk_contexts:list[str]
    extended_local_chunk_contexts:list[str]
    summary: str
    
    def __init__(self, document_title: str, segment: str):
        self.segment = segment
        self.document_title = document_title
        all_chunks = split_paragraphs(segment, CHUNK_LENGTH)
        # self.chunks = [c for c in all_chunks if len(c) >= MIN_CHUNK_CHARS][:MAX_CHUNKS_PER_SEGMENT]
        self.chunks = [c for c in sort_chunks(all_chunks) if len(c) >= MIN_CHUNK_CHARS][:MAX_CHUNKS_PER_SEGMENT]
        self.summary = ""
        self.chunk_contexts = []
        self.extended_chunk_contexts = []
        self.extended_local_chunk_contexts = []

    def build_summary(self):
        if self.summary:
            print('DocumentFragment: attempting to call build_summary twice')
            return
            
        self.summary = callLMSummary(self.segment)
    
    def build_regular_chunk_contexts(self):
        if len(self.chunk_contexts):
            print('DocumentFragment: attempting to call build_regular_chunk_contexts twice')
            return

        if not self.summary:
            self.build_summary()
            
        for chunk in self.chunks:
            self.chunk_contexts.append(callLLMContextualize(self.document_title, self.summary, chunk))

    def build_extended_chunk_contexts(self, grand_summary):
        if len(self.extended_chunk_contexts):
            print('DocumentFragment: attempting to call build_extended_chunk_contexts twice')
            return
            
        for chunk in self.chunks:
            self.extended_chunk_contexts.append(callLLMContextualize(self.document_title, grand_summary, chunk))

    def build_extended_local_chunk_contexts(self, grand_summary):
        if len(self.extended_local_chunk_contexts):
            print('DocumentFragment: attempting to call build_extended_local_chunk_contexts twice')
            return
            
        for chunk in self.chunks:
            if grand_summary == self.summary:
                self.extended_local_chunk_contexts.append(callLLMContextualize(self.document_title, grand_summary, chunk))
            else:
                self.extended_local_chunk_contexts.append(
                    callLLMContextualize(self.document_title, f"<document_summary>\n{grand_summary}</document_summary>\n<section_summary>\n{self.summary}</section_summary>\n", chunk))

@dataclass
class Document:
    fragments: list[DocumentFragment]
    summary: str
    
    def __init__(self, fragments: list[DocumentFragment]):
        self.fragments = fragments
        self.summary = ""
    
    def build_summaries(self):
        if self.summary:
            print('Document: attempting to call build_summary twice')
            return

        for fragment in self.fragments:
            fragment.build_summary()

        if (len(self.fragments) == 1):
            self.summary = self.fragments[0].summary
        else:
            all_summaries = " ".join([fragment.summary for fragment in self.fragments])
            self.summary = callLMSummary(all_summaries)

    # all segments are summarized (the document summary needs them all);
    # only the first MAX_SEGMENTS_PER_DOC get their chunks contextualized
    # def previewed_fragments(self) -> list[DocumentFragment]:
    #     return self.fragments[:MAX_SEGMENTS_PER_DOC]
    def previewed_fragments(self) -> list[DocumentFragment]:
        return self.fragments

    def build_regular_chunk_contexts(self):
        for fragment in self.previewed_fragments():
            fragment.build_regular_chunk_contexts();

    def build_extended_chunk_contexts(self):
        for fragment in self.previewed_fragments():
            fragment.build_extended_chunk_contexts(self.summary)

    def build_extended_local_chunk_contexts(self):
        for fragment in self.previewed_fragments():
            fragment.build_extended_local_chunk_contexts(self.summary)

for file in files:
    with open(file) as f:
        document = f.read()
        file_segments:list[DocumentFragment]
        document_title = file.stem
        if len(document) > SEGMENT_LENGTH:
            file_segments = [ DocumentFragment(document_title, segment) for segment in split_paragraphs(document, SEGMENT_LENGTH) ]
        else:
            file_segments = [ DocumentFragment(document_title, document) ]

        print(f'"Doc: {str(file)}, segments {len(file_segments)}')
        # print(f'"Doc: {str(file)}, segments {len(file_segments)}, contextualizing the first {MAX_SEGMENTS_PER_DOC}')
        print('======================')
        document = Document(file_segments)
        document.build_summaries()
        # document.build_regular_chunk_contexts()
        # document.build_extended_chunk_contexts()
        document.build_extended_local_chunk_contexts()

        for fragment in document.previewed_fragments():
            for i, chunk in enumerate(fragment.chunks):
                print("=" * 20, fragment.document_title, "chunk", i)
                print(chunk[:300].replace("\n", " "), "...")
                for name, ctx in [("C doc+segment", fragment.extended_local_chunk_contexts[i])]:
                    print(f"--- {name} ({len(ctx)})\n{ctx}")
                # for name, ctx in [("A segment", fragment.chunk_contexts[i]),
                #                 ("B document", fragment.extended_chunk_contexts[i]),
                #                 ("C doc+segment", fragment.extended_local_chunk_contexts[i])]:
                #     print(f"--- {name} ({len(ctx)})\n{ctx}")

