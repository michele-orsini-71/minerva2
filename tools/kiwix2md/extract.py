from bs4 import BeautifulSoup
from html_to_markdown import convert, ConversionOptions, PreprocessingOptions

REMOVE_SELECTORS = ["table.infobox", ".navbox", ".sidebar", "sup.reference"]
options = ConversionOptions(
    skip_images=True,
    preprocessing=PreprocessingOptions(enabled=True, preset="aggressive"),
)

def clean_html(raw):
    soup = BeautifulSoup(raw, "html.parser")
    if soup.find("meta", attrs={"http-equiv": "refresh"}):
        return None  # redirect stub, not an article
    for selector in REMOVE_SELECTORS:
        for node in soup.select(selector):
            node.decompose()
    return str(soup)

# The landing page and MediaWiki internal resources (the "_" namespace) are valid HTML but not articles; exclude them by identity, not by content.
def is_article(entry, main_path) -> bool:
    if entry.is_redirect:
        return False
    if entry.path == main_path or entry.path.startswith("_"):
        return False

    item = entry.get_item()
    if not item.mimetype.startswith("text/html"):
        return False
    
    return True

def md_filename(entry) -> str:
    return entry.path.replace("/", "_") + ".md"

def to_markdown(text: str) -> str | None:
    # cleaned = clean_html(text)
    # if cleaned is None:
    #     return None

    result = convert(text, options)
    return result.content

#open_zim(path)