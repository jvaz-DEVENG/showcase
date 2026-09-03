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


def smoke_mata_barata():
    pw, browser, page, errors = open_page("minijogos/mata-barata/index.html")
    page.wait_for_selector("#start-btn")
    page.click("#start-btn")
    page.wait_for_timeout(1000)  # let at least one bug pop up
    hole = page.locator(".hole.up").first
    try:
        hole.click(timeout=500, force=True)
    except Exception:
        pass
    shot(page, OUT, "mata-barata")
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


def smoke_bubble_crane():
    pw, browser, page, errors = open_page("minijogos/bubble-crane/index.html")
    page.wait_for_selector("#start-btn")
    page.click("#start-btn")
    page.wait_for_timeout(400)
    # pega o primeiro bloco fora de ordem com a garra e empurra pra direita
    dom_index = page.evaluate(
        """() => {
            const bs = [...document.querySelectorAll('.box')];
            const slot = {};
            bs.forEach(b => { slot[+b.querySelector('i').textContent] = +b.querySelector('b').textContent; });
            for (let i = 0; i < bs.length - 1; i++) {
                if (slot[i] > slot[i + 1]) {
                    return bs.findIndex(b => +b.querySelector('i').textContent === i);
                }
            }
            return -1;
        }"""
    )
    if dom_index >= 0:
        page.locator(".box").nth(dom_index).click()
        page.wait_for_timeout(350)
        page.click("#btn-right")
        page.wait_for_timeout(700)  # deixa a troca/animação do guindaste rodar
    shot(page, OUT, "bubble-crane")
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


def smoke_cosmic_crush():
    """Match-3: navigate every screen, then play a few real drag-swaps."""
    pw, browser, page, errors = open_page("minijogos/cosmic-crush/index.html")
    page.wait_for_timeout(500)
    # every screen has to mount without throwing
    for label in ["Loja", "Voltar", "Perfil", "Voltar", "Conquistas", "Voltar",
                  "Como jogar", "Voltar"]:
        try:
            page.locator("button", has_text=label).first.click(timeout=2000)
            page.wait_for_timeout(200)
        except Exception:
            pass
    page.locator("button", has_text="Jogar fase").first.click()
    page.wait_for_timeout(400)
    box = page.locator("#arena").bounding_box()
    cell = box["width"] / 8.0
    for i in range(24):  # blind swaps; some land, enough to exercise the engine
        r, c = i % 7, (i * 3) % 7
        x1, y1 = box["x"] + (c + .5) * cell, box["y"] + (r + .5) * cell
        x2, y2 = x1 + cell, y1
        page.mouse.move(x1, y1)
        page.mouse.down()
        page.mouse.move(x2, y2, steps=3)
        page.mouse.up()
        page.wait_for_timeout(90)
    shot(page, OUT, "cosmic-crush")
    browser.close()
    pw.stop()
    return errors


def smoke_sprite_animator():
    """Gera uma spritesheet 6x6 na hora e confere que a ferramenta a corta e toca."""
    import math, tempfile
    from pathlib import Path as _P
    from PIL import Image, ImageDraw

    F, COLS, ROWS = 96, 6, 6
    sheet = Image.new("RGBA", (F * COLS, F * ROWS), (0, 0, 0, 0))
    d = ImageDraw.Draw(sheet)
    for i in range(COLS * ROWS):
        ox, oy = (i % COLS) * F, (i // COLS) * F
        cx, cy, t = ox + F / 2, oy + F / 2, i / (COLS * ROWS)
        r = 15 + 4 * math.sin(t * 2 * math.pi)
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(244, 180, 0, 255))
        for k in range(3):
            a = t * 2 * math.pi + k * 2 * math.pi / 3
            sx, sy = cx + 33 * math.cos(a), cy + 33 * math.sin(a)
            d.ellipse([sx - 6, sy - 6, sx + 6, sy + 6], fill=(30, 58, 95, 255))
    folha = _P(tempfile.gettempdir()) / "smoke_spritesheet_6x6.png"
    sheet.save(folha)

    pw, browser, page, errors = open_page("ferramentas/sprite-animator/index.html",
                                          viewport=(1280, 900))
    page.locator("#file").set_input_files(str(folha))
    page.wait_for_timeout(1200)
    # a deteccao automatica tem que achar os 36 frames sozinha
    info = page.locator("#frameInfo").inner_text()
    if "/ 35" not in info:
        errors.append(f"grade nao detectada: esperava 36 frames, veio {info!r}")
    page.locator("#playBtn").click()
    page.wait_for_timeout(600)
    if page.locator("#playBtn").is_disabled():
        errors.append("botao de tocar continuou desabilitado depois de carregar a folha")
    shot(page, OUT, "sprite-animator")
    browser.close()
    pw.stop()
    return errors


CHECKS = {
    "reflex-rush": smoke_reflex_rush,
    "mata-barata": smoke_mata_barata,
    "fusion-rush": smoke_fusion_rush,
    "bubble-crane": smoke_bubble_crane,
    "cosmic-crush": smoke_cosmic_crush,
    "gerador-titulo-seo": smoke_gerador_titulo,
    "assinador-mtr": smoke_assinador_mtr,
    "gerador-relatorio-fotografico": smoke_gerador_relatorio,
    "sprite-animator": smoke_sprite_animator,
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
