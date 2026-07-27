---
name: run-github-showcase
description: Build, run, and drive the GITHUB-SHOWCASE minigames/tools (Reflex Rush, Fusion Rush, Gerador de Título SEO). Use when asked to run, test, screenshot, or verify any project in this repo, smoke-test the whole showcase, or catch a fast/rare in-game event (e.g. an animation that only shows for <1s) on screen.
---

This repo is a collection of standalone `index.html` projects (HTML+CSS+JS,
no build step, no dev server — each is opened directly via `file://`).
Drive them with `.claude/skills/run-github-showcase/pw_driver.py`, a small
Python+Playwright helper module (there's no `chromium-cli` in this
environment, so this module fills that role). All paths below are
relative to the repo root (`D:\GITHUB-SHOWCASE`).

## Prerequisites

Already satisfied in this environment — nothing to install. Verify with:

```bash
python -c "from playwright.sync_api import sync_playwright as s; b=s().start().chromium.launch(); b.close()"
```

If that ever fails with a missing-module error: `pip install playwright`
then `python -m playwright install chromium`. Not needed here — both the
package and the browser are already present.

## Setup / Build

None. Every project is static HTML/CSS/JS with zero dependencies —
opening the file *is* the build.

## Run (agent path)

### Quick smoke test (all projects)

The fastest way to confirm nothing is broken: opens each project, does
one representative interaction, screenshots it, and checks for
console/page errors.

```bash
python .claude/skills/run-github-showcase/smoke_all.py
# optional: python .claude/skills/run-github-showcase/smoke_all.py <out_dir>
```

Exits non-zero if any project logged a console error. Screenshots land
in `.claude/skills/run-github-showcase/_smoke_shots/` by default (git-
ignored — treat as scratch, not an artifact to commit).

### Driving a specific project

`pw_driver.py` exposes `open_page(rel_html_path)` → `(pw, browser, page,
errors)` and `shot(page, out_dir, name)`. Write a short script per
interaction you need. Three verified patterns, one per project:

**Reflex Rush** — targets are real DOM elements (`.target`), so click
them directly. They vanish fast, so wrap the click in try/except:

```python
import sys, time
sys.path.insert(0, ".claude/skills/run-github-showcase")
from pw_driver import open_page, shot

pw, browser, page, errors = open_page("minijogos/reflex-rush/index.html")
page.wait_for_selector("#start-btn")
page.click("#start-btn")
t0 = time.time()
while time.time() - t0 < 6:
    targets = page.locator(".target")
    if targets.count() > 0:
        try:
            targets.first.click(timeout=200, force=True)
        except Exception:
            pass
    page.wait_for_timeout(80)
shot(page, "_shots", "reflex-rush")
print(page.locator("#score-val").inner_text(), errors)
browser.close(); pw.stop()
```

**Fusion Rush** — no DOM targets, everything is drawn on `#arena`
(canvas). Drop a ball by clicking inside the canvas's bounding box:

```python
import sys
sys.path.insert(0, ".claude/skills/run-github-showcase")
from pw_driver import open_page, shot

pw, browser, page, errors = open_page("minijogos/fusion-rush/index.html")
page.wait_for_selector("#start-btn")
page.click("#start-btn")
box = page.locator("#arena").bounding_box()
page.mouse.move(box["x"] + box["width"] / 2, box["y"] + 20)
page.mouse.down(); page.mouse.up()
page.wait_for_timeout(700)
shot(page, "_shots", "fusion-rush")
browser.close(); pw.stop()
```

**Gerador de Título SEO** — a plain form, fill and read:

```python
import sys
sys.path.insert(0, ".claude/skills/run-github-showcase")
from pw_driver import open_page, shot

pw, browser, page, errors = open_page("ferramentas/gerador-titulo-seo/index.html")
page.fill("#produto", "Garrafa Termica Inox")
page.fill("#atributos", "1 litro, tampa rosca, mantem temperatura")
page.click('button.tab-btn[data-mk="amazon"]')  # switch marketplace tab
print(page.locator("#result").inner_text())
shot(page, "_shots", "gerador-titulo-seo")
browser.close(); pw.stop()
```

## Run (human path)

Just open the `index.html` in any browser — double-click it, or
`start minijogos/fusion-rush/index.html` on Windows. No server needed.

## Catching fast/rare in-game events

Some in-game moments are too short and too rare for screenshot-by-
time-interval to reliably catch (Fusion Rush's cat paw animation is
~0.76s inside a 3–6s cycle — sampling every few seconds mostly misses
it). This is what actually worked, verified this session on Fusion
Rush:

1. Temporarily add a debug hook at the end of the page's `<script>`,
   right before the closing `})();`, exposing whatever internal state
   you need:
   ```js
   window.__debug = () => ({ pawState, pawSide, pawReach, catMood, failStreak, catSteals });
   ```
2. Poll it with `pw_driver.watch_debug(page, predicate, on_tick=...)` —
   `on_tick` lets you keep driving input (e.g. dropping balls) while
   waiting; it screenshots/returns as soon as `predicate(state)` is true.
3. **Remove the hook before committing.** It's a debug-only shim, not
   part of the shipped game — leaving it in is dead code in a public
   repo.

This is also the only way to inspect canvas-only games at all beyond
pixel screenshots — there's no DOM/accessibility tree for anything
drawn on `<canvas>`.

## Gotchas

- **Canvas games have no DOM to query.** Fusion Rush's balls, cat, and
  paw are all `ctx.fill`/`ctx.arc` calls — `page.locator()` can't see
  any of it. Either read pixels (screenshot + `PIL` color sampling —
  unreliable, gradients cause false positives) or use the `window.__debug`
  hook above. The hook is far more reliable; prefer it.
- **Fast-disappearing DOM targets (Reflex Rush) need `force=True` and a
  short `timeout`** on `.click()` — Playwright's normal actionability
  wait can lose the race against a target that's about to be removed
  from the DOM, and throws instead of just missing.
- **`file://` paths on Windows**: build them with
  `Path(...).resolve().as_posix()` and prefix `file:///` (three
  slashes) — passing a raw Windows backslash path to `page.goto`
  doesn't resolve correctly.

## Troubleshooting

No failures hit this session — `smoke_all.py` and all three per-project
scripts above ran clean on the first real attempt (after fixing one
actual game bug they surfaced — a paw-direction sign error in Fusion
Rush, unrelated to the driver itself).