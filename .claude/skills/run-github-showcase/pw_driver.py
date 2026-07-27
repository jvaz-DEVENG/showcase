"""
Reusable Playwright driver for GITHUB-SHOWCASE's static HTML projects.

Every project in this repo is a standalone index.html (HTML+CSS+JS, no
build step, no dev server) opened directly via file://. This module is
NOT a CLI you run standalone -- it's a small helper library that
per-project driver scripts import (see SKILL.md for real, run-verified
examples for each project).

Requirements already satisfied in this environment -- no setup needed:
  python -c "from playwright.sync_api import sync_playwright as s; s().start().chromium.launch().close()"
should succeed silently. Playwright's Python package AND its Chromium
browser are already installed. Do not run `playwright install`.
"""
import time
from pathlib import Path
from playwright.sync_api import sync_playwright

# .claude/skills/run-github-showcase/pw_driver.py -> repo root is 3 up
REPO_ROOT = Path(__file__).resolve().parents[3]


def open_page(rel_html_path, viewport=(460, 720), headless=True):
    """Launch Chromium and open a project's index.html via file://.
    Returns (playwright, browser, page, errors) -- errors is a list that
    accumulates console.error / pageerror text as they happen; check it
    after your interaction to confirm nothing threw."""
    html_path = (REPO_ROOT / rel_html_path).resolve()
    if not html_path.exists():
        raise FileNotFoundError(html_path)

    pw = sync_playwright().start()
    browser = pw.chromium.launch(headless=headless)
    page = browser.new_page(viewport={"width": viewport[0], "height": viewport[1]})

    errors = []
    page.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)
    page.on("pageerror", lambda e: errors.append(f"pageerror: {e}"))

    page.goto(f"file:///{html_path.as_posix()}")
    return pw, browser, page, errors


def shot(page, out_dir, name):
    """Screenshot to <out_dir>/<name>.png, creating out_dir if needed."""
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    path = out_dir / f"{name}.png"
    page.screenshot(path=str(path))
    return path


def watch_debug(page, predicate, timeout_s=30, poll_s=0.1, on_tick=None):
    """Poll window.__debug() until predicate(state) is True or timeout.

    Only works if the page under test temporarily exposes
    `window.__debug = () => ({...internal state...})` -- see the
    "Catching fast/rare in-game events" section in SKILL.md. Returns the
    matching state dict, or None on timeout.

    Pass on_tick(elapsed_seconds) if you need to keep driving input
    (e.g. clicking/dropping) while waiting for the event to occur.
    """
    t0 = time.time()
    while time.time() - t0 < timeout_s:
        if on_tick:
            on_tick(time.time() - t0)
        state = page.evaluate("window.__debug ? window.__debug() : null")
        if state and predicate(state):
            return state
        time.sleep(poll_s)
    return None
