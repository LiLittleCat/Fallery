"""Publish captured PNGs under content-addressed names to refresh Markdown previews."""

import hashlib
import re
from pathlib import Path


def main():
    root = Path(__file__).resolve().parent.parent
    output = (root / "docs" / "screenshots").resolve()
    readmes = [root / "README.md", root / "README.zh-CN.md"]
    texts = {path: path.read_text(encoding="utf-8") for path in readmes}
    publications = []
    for stem in ("masonry-dark", "filmstrip-dark", "justified-light-zh"):
        pattern = rf"docs/screenshots/{stem}(?:-[a-f0-9]{{12}})?\.png"
        if stem != "masonry-dark" and not any(re.search(pattern, text) for text in texts.values()):
            continue
        source = output / f"{stem}.png"
        if not source.exists():
            match = re.search(pattern, texts[readmes[0]])
            if not match:
                raise RuntimeError(f"Missing capture or README link for {stem}")
            source = root / match.group()
        data = source.read_bytes()
        if not data.startswith(b"\x89PNG\r\n\x1a\n"):
            raise RuntimeError(f"Invalid PNG: {source.name}")
        digest = hashlib.sha256(data).hexdigest()[:12]
        target = output / f"{stem}-{digest}.png"
        for path in readmes:
            texts[path], count = re.subn(pattern, f"docs/screenshots/{target.name}", texts[path])
            if count != 1:
                raise RuntimeError(f"Expected one {stem} image in {path.name}, got {count}")
        publications.append((stem, target, data))

    for _, target, data in publications:
        target.write_bytes(data)
    for path, text in texts.items():
        path.write_text(text, encoding="utf-8")
    for stem, target, _ in publications:
        # Only remove known generated revisions inside the resolved screenshot directory.
        for candidate in output.iterdir():
            if (candidate != target and not candidate.is_symlink()
                    and candidate.resolve().parent == output
                    and re.fullmatch(rf"{stem}(?:-[a-f0-9]{{12}})?\.png", candidate.name)):
                candidate.unlink()
        print(target.relative_to(root))


if __name__ == "__main__":
    main()
