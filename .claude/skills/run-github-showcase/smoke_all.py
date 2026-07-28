"""
Smoke-test every project in the showcase: open it, do one representative
interaction, screenshot, and check for console/page errors.

Run:
    python .claude/skills/run-github-showcase/smoke_all.py [out_dir]

Exits non-zero if any project logged a console error or pageerror.
Screenshots land in out_dir (default: _smoke_shots/ next to this file).

Add a new project by adding one `smoke_<name>()` function that does a
representative interaction (not just open-and-screenshot -- click
something, fill something) and registering it in CHECKS.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from pw_driver import open_page, shot

OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent / "_smoke_shots"


def smoke_reflex_rush():
    pw, browser, page, errors = open_page("minijogos/reflex-rush/index.html")
    page.wait_for_selector("#start-btn")
    page.click("#start-btn")
    page.wait_for_timeout(1500)  # let at least one target spawn
    shot(page, OUT, "reflex-rush")
    browser.close()
    pw.stop()
    return errors


def smoke_fusion_rush():
    pw, browser, page, errors = open_page("minijogos/fusion-rush/index.html")
    page.wait_for_selector("#start-btn")
    page.click("#start-btn")
    canvas = page.locator("#arena")
    box = canvas.bounding_box()
    page.mouse.move(box["x"] + box["width"] / 2, box["y"] + 20)
    page.mouse.down()
    page.mouse.up()
    page.wait_for_timeout(700)  # let the dropped ball fall and settle
    shot(page, OUT, "fusion-rush")
    browser.close()
    pw.stop()
    return errors


def smoke_gerador_titulo():
    pw, browser, page, errors = open_page("ferramentas/gerador-titulo-seo/index.html")
    page.fill("#produto", "Produto Teste")
    page.fill("#atributos", "atributo um, atributo dois")
    page.wait_for_timeout(150)
    shot(page, OUT, "gerador-titulo-seo")
    browser.close()
    pw.stop()
    return errors


def smoke_assinador_mtr():
    # ~5MB page: pdf-lib/fontkit/pdf.js are embedded as base64 and decoded
    # on load, so give the loading overlay real time to disappear before
    # treating the page as ready.
    pw, browser, page, errors = open_page("ferramentas/assinador-mtr/index.html", viewport=(1200, 800))
    page.wait_for_selector("#loading", state="hidden", timeout=15000)
    page.fill("#name", "Teste Smoke")
    page.click('.stylecard[data-style="Allura"]')
    page.wait_for_timeout(200)
    shot(page, OUT, "assinador-mtr")
    browser.close()
    pw.stop()
    return errors


def smoke_gerador_relatorio():
    pw, browser, page, errors = open_page("ferramentas/gerador-relatorio-fotografico/index.html", viewport=(1400, 900))
    png_bytes = bytes.fromhex(
        "89504e470d0a1a0a0000000d49484452000000010000000108020000009077"
        "53de0000000c4944415478da6360606000000005000162a0eea50000000049454e44ae426082"
    )
    page.set_input_files("#file1", [{"name": "teste.png", "mimeType": "image/png", "buffer": png_bytes}])
    page.wait_for_timeout(300)
    shot(page, OUT, "gerador-relatorio-fotografico")
    browser.close()
    pw.stop()
    return errors


CHECKS = {
    "reflex-rush": smoke_reflex_rush,
    "fusion-rush": smoke_fusion_rush,
    "gerador-titulo-seo": smoke_gerador_titulo,
    "assinador-mtr": smoke_assinador_mtr,
    "gerador-relatorio-fotografico": smoke_gerador_relatorio,
}

if __name__ == "__main__":
    failed = False
    for name, fn in CHECKS.items():
        errors = fn()
        status = "OK" if not errors else "ERRORS: " + "; ".join(errors)
        print(f"{name}: {status}")
        if errors:
            failed = True
    print("screenshots ->", OUT)
    sys.exit(1 if failed else 0)
