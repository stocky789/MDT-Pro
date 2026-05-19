# MDT Pro public docs site

This folder is a small static documentation site for public MDT Pro users. It is intentionally plain HTML/CSS so it can be hosted from GitHub Pages without adding a docs framework or another Node app.

## Preview locally

From the repo root:

```bash
python3 -m http.server 4173 --directory docs
```

Open:

```text
http://localhost:4173
```

## Publish with GitHub Pages

Recommended simple setup:

1. Copy or keep this `docs/` folder in the public `stocky789/MDT-Pro` repo.
2. In GitHub, open **Settings -> Pages**.
3. Set the source to **Deploy from a branch**.
4. Pick the branch you publish from and set the folder to `/docs`.
5. Save.

That gives you a public URL similar to:

```text
https://stocky789.github.io/MDT-Pro/
```

## Content boundary

The docs are written for the public release. Do not document private backend implementation details here. It is fine to say that MDT Cloud login requires an account at `mdt.stockhosting.com.au` and currently supports Policing Redefined only.

## Screenshots

Screenshots under `assets/screenshots/` were captured from the local MDT dev server (`node dev-server.js`) so the page shows the actual current interface instead of mock art.
