"""Fetch publisher-owned application icons for the offline Software catalog.

No third-party icon service is used. Provenance is written beside the PNG assets.
Requires Pillow only as a development tool; the application uses WPF image decoding.
"""
import concurrent.futures
import io
import json
import re
import urllib.parse
import urllib.request
from html.parser import HTMLParser
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DESTINATION = ROOT / "src/WinModes.App/Assets/Software"
OVERRIDES = {
    "unigetui": "https://raw.githubusercontent.com/Devolutions/UniGetUI/main/media/Icon%20sizes/128.png",
    "powertoys": "https://raw.githubusercontent.com/microsoft/PowerToys/main/src/settings-ui/Settings.UI/Assets/Settings/icon.ico",
    "epic": "https://static-assets-prod.epicgames.com/epic-store/static/favicon.ico",
    "powershell": "https://raw.githubusercontent.com/PowerShell/PowerShell/master/assets/Powershell_256.png",
    "codex": "https://chatgpt.com/favicon.ico",
    "chatgpt": "https://chatgpt.com/favicon.ico",
    "claude-code": "https://claude.com/favicon.ico",
    "python": "https://www.python.org/static/favicon.ico",
}

class Icons(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []

    def handle_starttag(self, tag, attributes):
        attrs = dict(attributes)
        if tag == "link" and "icon" in attrs.get("rel", "").lower() and attrs.get("href"):
            self.links.append(attrs["href"])

def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 WinModes-Logo-Catalog"})
    with urllib.request.urlopen(request, timeout=18) as response:
        if urllib.parse.urlsplit(response.url).scheme != "https":
            raise ValueError("Non-HTTPS redirect")
        content = response.read(4 * 1024 * 1024 + 1)
        if len(content) > 4 * 1024 * 1024:
            raise ValueError("Asset too large")
        return content, response.url

def collect(entry):
    identifier, website = entry
    target = DESTINATION / f"{identifier}.png"
    if target.exists():
        return identifier, None
    candidates = []
    if identifier in OVERRIDES:
        candidates.append(OVERRIDES[identifier])
    try:
        html, resolved = fetch(website)
        parser = Icons()
        parser.feed(html.decode("utf-8", errors="replace"))
        candidates.extend(urllib.parse.urljoin(resolved, link) for link in sorted(parser.links, key=lambda link: ("apple" not in link, not link.endswith(".png"))))
    except Exception:
        pass
    origin = urllib.parse.urlsplit(website)
    candidates.append(f"https://{origin.netloc}/favicon.ico")
    for url in dict.fromkeys(candidates):
        if urllib.parse.urlsplit(url).scheme != "https":
            continue
        try:
            data, resolved = fetch(url)
            with Image.open(io.BytesIO(data)) as original:
                if original.format == "ICO":
                    original = original.ico.getimage(max(original.ico.sizes()))
                if max(original.size) > 4096:
                    continue
                image = original.convert("RGBA")
                image.thumbnail((128, 128), Image.Resampling.LANCZOS)
                image.save(target)
            return identifier, {"website": website, "asset": resolved}
        except Exception:
            continue
    return identifier, {"website": website, "error": "No readable official raster icon"}

def main():
    source = (ROOT / "src/WinModes.Core/Software/SoftwareCatalog.cs").read_text(encoding="utf-8")
    entries = [(match[0], re.search(r'"(https://[^\"]+)"', match[1]).group(1)) for match in re.findall(r'new\("([^\"]+)",(.*?)\),', source)]
    DESTINATION.mkdir(parents=True, exist_ok=True)
    provenance_path = DESTINATION / "sources.json"
    provenance = json.loads(provenance_path.read_text()) if provenance_path.exists() else {}
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        for identifier, result in pool.map(collect, entries):
            if result is not None:
                provenance[identifier] = result
            print(f"{identifier}: {'OK' if (DESTINATION / (identifier + '.png')).exists() else 'MISSING'}", flush=True)
    provenance_path.write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")

if __name__ == "__main__":
    main()
