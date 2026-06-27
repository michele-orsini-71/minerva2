import sys
from extract_utils import check_integrity, load_config


if __name__ == "__main__":
    if len(sys.argv) != 3 or sys.argv[1] != "--config":
        print("Usage: extract.py --config <corpus.json>")
        sys.exit(1)
    cfg = load_config(sys.argv[2])
    check_integrity(cfg["zim"], cfg["zim_sha256"], cfg["manifest"], cfg["output"])
    print("integrity OK")
