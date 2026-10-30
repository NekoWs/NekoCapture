"""检查静态宣传页的本地资源、锚点与页签连接。"""
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit
import struct

ROOT = Path(__file__).resolve().parent.parent / "site"


class Page(HTMLParser):
    """收集页面可导航的资源与交互连接。"""

    def __init__(self):
        super().__init__()
        self.ids = set()
        self.references = []
        self.controls = []
        self.images = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if "id" in attrs:
            assert attrs["id"] not in self.ids, f"Duplicate id: {attrs['id']}"
            self.ids.add(attrs["id"])
        if "aria-controls" in attrs:
            self.controls.append(attrs["aria-controls"])
        for name in ("src", "href"):
            if attrs.get(name):
                self.references.append(attrs[name])
        if tag == "img":
            assert "alt" in attrs, "Image needs alt text"
            self.images.append(attrs)


pages = {}
for path in ROOT.rglob("*.html"):
    page = Page()
    page.feed(path.read_text(encoding="utf-8"))
    pages[path.resolve()] = page
assert ROOT / "index.html" in pages and ROOT / "en/index.html" in pages

for path, page in pages.items():
    for control in page.controls:
        assert control in page.ids, f"{path}: Missing tab panel {control}"
    for reference in page.references:
        url = urlsplit(reference)
        if url.scheme or url.netloc:
            continue
        target = (path.parent / unquote(url.path)).resolve() if url.path else path
        assert target.is_relative_to(ROOT.resolve()), f"Resource outside site: {reference}"
        if target.is_dir():
            target /= "index.html"
        assert target.is_file(), f"{path}: Missing resource {reference}"
        if url.fragment and target.suffix == ".html":
            assert unquote(url.fragment) in pages[target].ids, f"Missing anchor {reference}"
    for image in page.images:
        target = (path.parent / image["src"]).resolve()
        if target.suffix != ".png":
            continue
        size = struct.unpack(">II", target.read_bytes()[16:24])
        if "width" in image and "height" in image:
            ratio = int(image["width"]) / int(image["height"])
            assert abs(size[0] / size[1] - ratio) < 0.001, f"Image aspect ratio does not match: {image['src']} {size}"
print(f"SITE CHECK PASS: {len(pages)} pages, local resources, anchors, image ratios, and tabs")
