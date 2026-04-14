/**
 * Face-crop ped portraits into MDTPro/images/peds/ (game model names, lowercase).
 *
 * Pipeline (default run):
 *  1) Re-crop every existing .webp/.png in the output dir (e.g. after fetchPedCatalogueImages.mjs full-body downloads).
 *  2) Ingest repo-root peds/*.jpg with bracket names [model][d][t].jpg — one output per file:
 *     `{model}__{d}_{t}.webp`. Bracket d/t may follow **hair** (component 2) or **face** (0) depending on the source; the MDT tries both pairs at runtime. When d and t are 0, also writes `{model}.webp` (same bytes) for legacy fallback.
 *
 * Usage (repo root):
 *   npm install
 *   node scripts/processPedPortraitCatalogue.mjs --clean
 *   node scripts/fetchPedCatalogueImages.mjs   # optional: vanilla full-body inputs
 *   node scripts/processPedPortraitCatalogue.mjs
 *
 * Flags:
 *   --clean              Delete all *.webp and *.png under MDTPro/images/peds before processing.
 *   --skip-reprocess-out Skip step 1 (only ingest JPGs).
 *   --skip-jpgs          Skip step 2 (only re-crop files already in output dir).
 *
 * Requires: Node 18+, sharp (npm install in repo root).
 */
import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'
import sharp from 'sharp'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const ROOT = path.join(__dirname, '..')
const OUT_DIR = path.join(ROOT, 'MDTPro', 'images', 'peds')
const JPG_DIR = path.join(ROOT, 'peds')

/** Fraction of source height to keep from the top (full-body catalogue stills). */
const TOP_FRACTION = 0.38
/** Square output size in pixels. */
const OUTPUT_SIZE = 512
const BRACKET_JPG = /^\[([a-z0-9_]+)\]\[(\d+)\]\[(\d+)\]\.(jpe?g)$/i

function parseArgs (argv) {
  return {
    clean: argv.includes('--clean'),
    skipReprocessOut: argv.includes('--skip-reprocess-out'),
    skipJpgs: argv.includes('--skip-jpgs')
  }
}

function ensureDir (dir) {
  fs.mkdirSync(dir, { recursive: true })
}

function cleanOutputDir () {
  ensureDir(OUT_DIR)
  for (const name of fs.readdirSync(OUT_DIR)) {
    const lower = name.toLowerCase()
    if (lower.endsWith('.webp') || lower.endsWith('.png')) {
      fs.unlinkSync(path.join(OUT_DIR, name))
    }
  }
}

/** Aligned with server ImageAPIResponse.IsSafePedCatalogueBaseName (lowercase basenames only here). */
function isSafePortraitBasename (base) {
  if (!base || base.length > 96) return false
  const idx = base.indexOf('__')
  if (idx < 0) {
    return /^[a-z0-9_]+$/.test(base)
  }
  const model = base.slice(0, idx)
  const rest = base.slice(idx + 2)
  if (model.length < 1 || rest.length < 3) return false
  if (rest.includes('__')) return false
  const us = rest.indexOf('_')
  if (us < 1 || us >= rest.length - 1) return false
  if (rest.indexOf('_', us + 1) >= 0) return false
  if (!/^[a-z0-9_]+$/.test(model)) return false
  const d = rest.slice(0, us)
  const t = rest.slice(us + 1)
  return /^\d+$/.test(d) && /^\d+$/.test(t)
}

/**
 * Full-body or tall still -> top band -> square face-oriented crop.
 */
async function cropPortraitToWebp (inputPath, outputPath) {
  const img = sharp(inputPath)
  const meta = await img.metadata()
  const w = meta.width || 1
  const h = meta.height || 1
  const cropH = Math.max(1, Math.floor(h * TOP_FRACTION))
  const tmp = outputPath + '.tmp.webp'
  await sharp(inputPath)
    .extract({ left: 0, top: 0, width: w, height: cropH })
    .resize(OUTPUT_SIZE, OUTPUT_SIZE, { fit: 'cover', position: 'top' })
    .webp({ quality: 86 })
    .toFile(tmp)
  try {
    if (fs.existsSync(outputPath)) fs.unlinkSync(outputPath)
    fs.renameSync(tmp, outputPath)
  } catch (e) {
    try {
      if (fs.existsSync(tmp)) fs.unlinkSync(tmp)
    } catch { /* ignore */ }
    throw e
  }
}

async function reprocessAllInOutDir () {
  const names = fs.existsSync(OUT_DIR) ? fs.readdirSync(OUT_DIR) : []
  const targets = names.filter(n => {
    const l = n.toLowerCase()
    return l.endsWith('.webp') || l.endsWith('.png')
  })
  let ok = 0
  for (const name of targets) {
    const inPath = path.join(OUT_DIR, name)
    const base = path.basename(name, path.extname(name)).toLowerCase()
    if (!isSafePortraitBasename(base)) {
      console.warn(`skip (unsafe basename): ${name}`)
      continue
    }
    const outWebp = path.join(OUT_DIR, `${base}.webp`)
    try {
      const st = fs.statSync(inPath)
      if (st.size < 200) continue
      await cropPortraitToWebp(inPath, outWebp)
      if (outWebp !== inPath && fs.existsSync(inPath)) fs.unlinkSync(inPath)
      ok++
      console.log(`reprocess -> ${base}.webp`)
    } catch (e) {
      console.warn(`reprocess failed ${name}:`, e.message || e)
    }
  }
  return ok
}

function collectJpgSources () {
  if (!fs.existsSync(JPG_DIR)) return []
  const out = []
  for (const name of fs.readdirSync(JPG_DIR)) {
    const m = name.match(BRACKET_JPG)
    if (!m) continue
    out.push({
      model: m[1].toLowerCase(),
      d: parseInt(m[2], 10),
      t: parseInt(m[3], 10),
      filePath: path.join(JPG_DIR, name)
    })
  }
  return out
}

async function ingestJpgs () {
  const sources = collectJpgSources()
  let ok = 0
  for (const e of sources) {
    const base = `${e.model}__${e.d}_${e.t}`
    const outWebp = path.join(OUT_DIR, `${base}.webp`)
    try {
      await cropPortraitToWebp(e.filePath, outWebp)
      console.log(`jpg -> ${base}.webp`)
      ok++
      if (e.d === 0 && e.t === 0) {
        const legacy = path.join(OUT_DIR, `${e.model}.webp`)
        await fs.promises.copyFile(outWebp, legacy)
        console.log(`jpg -> ${e.model}.webp (copy of __0_0)`)
      }
    } catch (err) {
      console.warn(`jpg ingest failed ${e.filePath}:`, err.message || err)
    }
  }
  return ok
}

async function main () {
  const args = parseArgs(process.argv.slice(2))
  ensureDir(OUT_DIR)

  if (args.clean) {
    console.log('Cleaning', OUT_DIR)
    cleanOutputDir()
  }

  let n1 = 0
  let n2 = 0
  if (!args.skipReprocessOut) {
    console.log('Step 1: re-crop existing webp/png in output dir')
    n1 = await reprocessAllInOutDir()
    console.log(`Step 1 done: ${n1} file(s)`)
  }
  if (!args.skipJpgs) {
    console.log('Step 2: ingest peds/*.jpg (bracket names; one variant file per JPG)')
    n2 = await ingestJpgs()
    console.log(`Step 2 done: ${n2} file(s)`)
  }
  console.log(`Finished. Output: ${OUT_DIR}`)
}

main().catch((e) => {
  console.error(e)
  process.exit(1)
})
