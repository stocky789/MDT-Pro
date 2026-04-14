/**
 * Maintainer-only: downloads full-body ped stills from docs.fivem.net into MDTPro/images/peds/.
 * The game/browser MDT does not load these URLs at runtime — run scripts/processPedPortraitCatalogue.mjs after to face-crop into the final bundle.
 * Model names are parsed from FiveM's official docs repo (ped-models.md).
 *
 * Run from repo root: node scripts/fetchPedCatalogueImages.mjs
 * Requires Node 18+ (global fetch).
 */
import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const ROOT = path.join(__dirname, '..')
const OUT_DIR = path.join(ROOT, 'MDTPro', 'images', 'peds')
const MD_URL =
  'https://raw.githubusercontent.com/citizenfx/fivem-docs/master/content/docs/game-references/ped-models.md'
const CONCURRENCY = 10
const FAIL_LOG = path.join(__dirname, 'pedCatalogue_fetch_failures.txt')

/** Docs used to be plain lines; current fivem-docs uses HTML grids with <strong>model</strong>. */
function parseModelNames (md) {
  const set = new Set()
  const reStrong = /<strong>([a-z][a-z0-9_]*)<\/strong>/gi
  let m
  while ((m = reStrong.exec(md)) !== null) {
    set.add(m[1].toLowerCase())
  }
  // Plain-text fallback (older commits / forks)
  const reLine = /^\s*([a-z][a-z0-9_]+)\s+\d+\s+prop/i
  for (const line of md.split(/\r?\n/)) {
    const lm = line.match(reLine)
    if (lm) set.add(lm[1].toLowerCase())
  }
  return [...set].sort()
}

async function fetchText (url) {
  const res = await fetch(url, { redirect: 'follow' })
  if (!res.ok) throw new Error(`${url} -> ${res.status}`)
  return res.text()
}

async function downloadToFile (url, dest) {
  const res = await fetch(url, { redirect: 'follow' })
  if (!res.ok) return false
  const buf = Buffer.from(await res.arrayBuffer())
  if (buf.length < 200) return false
  fs.writeFileSync(dest, buf)
  return true
}

async function runPool (items, limit, worker) {
  let i = 0
  const runners = Array.from({ length: limit }, async () => {
    while (i < items.length) {
      const idx = i++
      await worker(items[idx], idx)
    }
  })
  await Promise.all(runners)
}

async function main () {
  fs.mkdirSync(OUT_DIR, { recursive: true })
  console.log('Fetching ped list from FiveM docs repo...')
  const md = await fetchText(MD_URL)
  const names = parseModelNames(md)
  console.log(`Parsed ${names.length} model names.`)

  const failures = []
  let ok = 0
  let skipped = 0

  await runPool(names, CONCURRENCY, async (name) => {
    const webp = path.join(OUT_DIR, `${name}.webp`)
    const png = path.join(OUT_DIR, `${name}.png`)
    if (fs.existsSync(webp) && fs.statSync(webp).size > 200) {
      skipped++
      return
    }
    if (fs.existsSync(png) && fs.statSync(png).size > 200) {
      skipped++
      return
    }
    const webpUrl = `https://docs.fivem.net/peds/${name}.webp`
    const pngUrl = `https://docs.fivem.net/peds/${name}.png`
    if (await downloadToFile(webpUrl, webp)) {
      ok++
      return
    }
    if (await downloadToFile(pngUrl, png)) {
      ok++
      return
    }
    failures.push(name)
  })

  if (failures.length) {
    fs.writeFileSync(
      FAIL_LOG,
      failures.sort().join('\n') + '\n',
      'utf8'
    )
    console.warn(`Failed to download ${failures.length} models (see ${path.relative(ROOT, FAIL_LOG)}).`)
  } else if (fs.existsSync(FAIL_LOG)) {
    fs.unlinkSync(FAIL_LOG)
  }

  console.log(`Done. New OK: ${ok}, skipped (already present): ${skipped}, failed: ${failures.length}`)
}

main().catch((e) => {
  console.error(e)
  process.exit(1)
})
