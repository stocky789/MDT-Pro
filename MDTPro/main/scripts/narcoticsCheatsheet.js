/**
 * Narcotics & Drugs Cheat Sheet – builds the cheat sheet content DOM.
 * Comprehensive reference: schedules, slang, indicators, charges, safety.
 */
function buildNarcoticsCheatsheetContent () {
  const wrap = (tag, content, cls = '') => {
    const el = document.createElement(tag)
    if (cls) el.className = cls
    if (typeof content === 'string') el.innerHTML = content
    else if (content) el.appendChild(content)
    return el
  }
  const section = (title, content) => {
    const s = wrap('div', null, 'narcoticsCheatsheetSection')
    s.appendChild(wrap('h3', title, 'narcoticsCheatsheetSectionTitle'))
    if (typeof content === 'string') {
      const div = document.createElement('div')
      div.innerHTML = content
      s.appendChild(div)
    } else if (content) s.appendChild(content)
    return s
  }
  const table = (rows) => {
    const t = document.createElement('table')
    t.className = 'narcoticsCheatsheetTable'
    rows.forEach((r, i) => {
      const tr = document.createElement('tr')
      const cellTag = i === 0 ? 'th' : 'td'
      r.forEach((c) => {
        const cell = document.createElement(cellTag)
        cell.innerHTML = c
        tr.appendChild(cell)
      })
      t.appendChild(tr)
    })
    return t
  }
  const ul = (items) => {
    const u = document.createElement('ul')
    items.forEach((i) => {
      const li = document.createElement('li')
      li.innerHTML = i
      u.appendChild(li)
    })
    return u
  }
  const grid = (items, cols = 2) => {
    const g = document.createElement('div')
    g.className = 'narcoticsCheatsheetGrid'
    g.style.gridTemplateColumns = `repeat(${cols}, 1fr)`
    items.forEach((i) => {
      const d = document.createElement('div')
      d.className = 'narcoticsCheatsheetGridItem'
      d.innerHTML = i
      g.appendChild(d)
    })
    return g
  }

  const container = document.createElement('div')
  container.className = 'narcoticsCheatsheetInner'

  // ─── Drug Schedules ─────────────────────────────────────────────────
  container.appendChild(section('Drug schedules', table([
    ['Schedule', 'Definition', 'Examples'],
    ['I', 'No accepted medical use, high abuse potential', 'Heroin, LSD, Cannabis*, Ecstasy/MDMA, Peyote, Psilocybin, DMT, Cocaine base (crack), Illicit GHB, Synthetic cannabinoids (K2/Spice), Synthetic cathinones'],
    ['II', 'High abuse potential, severe dependence risk', 'Cocaine (salt), Methamphetamine, PCP, Fentanyl, Oxycodone, Hydrocodone, Amphetamine/Adderall, Methylphenidate/Ritalin, Morphine, Methadone, Codeine'],
    ['III', 'Moderate abuse potential, accepted medical use', 'Ketamine, Anabolic steroids, FDA-approved GHB products, Some codeine/opiate combinations (limited strength), Tylenol w/ codeine'],
    ['IV', 'Low abuse potential, low dependence', 'Xanax, Valium, Ativan, Tramadol, Ambien (zolpidem), Soma, Rohypnol (flunitrazepam)'],
    ['V', 'Lowest abuse potential, cough/antidiarrheal', 'Lyrica, Lomotil, Cough syrups with limited codeine']
  ])))
  const scheduleNote = document.createElement('p')
  scheduleNote.className = 'narcoticsCheatsheetNote'
  scheduleNote.textContent = '* Cannabis scheduling vs retail rules vary by jurisdiction; treat as a controlled substance for illicit sale/possession in RP as your server prefers.'
  container.querySelector('.narcoticsCheatsheetSection:last-child').appendChild(scheduleNote)

  // ─── Drug → Schedule Quick Reference ───────────────────────────────
  container.appendChild(section('Quick: drug → schedule', grid([
    '<strong>Schedule I:</strong> Heroin, LSD, Ecstasy, Peyote, Psilocybin, DMT, Cannabis (many schedules), Crack (cocaine base), GHB (illicit), K2, bath salts',
    '<strong>Schedule II:</strong> Powder cocaine, Meth, PCP, Fentanyl, OxyContin, Vicodin, Adderall, Ritalin, Morphine, Codeine',
    '<strong>Schedule III:</strong> Ketamine, Steroids, Some codeine combos (e.g. Tylenol w/ codeine)',
    '<strong>Schedule IV:</strong> Xanax, Valium, Tramadol, Rohypnol, Soma'
  ], 2)))

  // ─── Street slang (short) ──────────────────────────────────────────
  container.appendChild(section('Street names', `
    <div class="narcoticsCheatsheetGrid narcoticsCheatsheetSlangGrid">
      <div><strong>Heroin:</strong> H, smack, horse, junk, black tar, boy</div>
      <div><strong>Cocaine:</strong> Coke, blow, snow, flake, crack, rock</div>
      <div><strong>Meth:</strong> Ice, crystal, glass, crank, Tina, shards</div>
      <div><strong>Fentanyl:</strong> Fetty, China girl, apache, blues, M30 (fake pills)</div>
      <div><strong>Weed:</strong> Pot, bud, ganja, grass, reefer, dope (sometimes)</div>
      <div><strong>MDMA:</strong> Molly, E, X, ecstasy, rolls</div>
      <div><strong>LSD:</strong> Acid, tabs, blotter</div>
      <div><strong>PCP:</strong> Angel dust, wet, sherm, fry</div>
      <div><strong>Syrup:</strong> Lean, drank, sizzurp</div>
      <div><strong>Xanax:</strong> Bars, z-bars, footballs</div>
      <div><strong>GHB:</strong> G, liquid G (not MDMA)</div>
      <div><strong>Ketamine:</strong> K, Special K, vitamin K</div>
    </div>
  `))

  // ─── Physical & Behavioral Indicators ──────────────────────────────
  container.appendChild(section('Signs at a glance', `
    <table class="narcoticsCheatsheetTable">
      <tr><th>Substance</th><th>Pupils</th><th>Behavior / Signs</th></tr>
      <tr><td>Stimulants (coke, meth)</td><td>Dilated</td><td>Hyperactivity, talkativeness, paranoia, grinding teeth, no sleep</td></tr>
      <tr><td>Depressants (heroin, opioids)</td><td>Pinpoint</td><td>Drowsiness, nodding off, slurred speech, slow respiration</td></tr>
      <tr><td>Cannabis</td><td>Bloodshot, dilated</td><td>Red eyes, odor, slowed reaction, dry mouth</td></tr>
      <tr><td>Hallucinogens (LSD, mushrooms)</td><td>Dilated</td><td>Altered perception, disorientation, dilated pupils</td></tr>
      <tr><td>Benzos (Xanax, Valium)</td><td>Normal/dilated</td><td>Slurred speech, drowsiness, stumbling</td></tr>
      <tr><td>Fentanyl</td><td>Pinpoint</td><td>Extreme drowsiness, respiratory depression, unresponsive</td></tr>
    </table>
  `))

  // ─── Quantity Thresholds (CA / General) ────────────────────────────
  container.appendChild(section('Sale vs personal use', `
    <ul>
      <li><strong>Marijuana:</strong> &lt;28.5g personal; &gt;28g can support sale charge</li>
      <li><strong>Cocaine/Heroin/Meth:</strong> Quantity, packaging (baggies, scales), cash, multiple doses suggest sale</li>
      <li><strong>Pills:</strong> Quantity beyond personal use (e.g. hundreds), no prescription</li>
      <li><strong>General:</strong> Scales, baggies, large cash, divided portions = possession for sale / trafficking</li>
    </ul>
  `))

  // ─── Common Charges Quick Reference ────────────────────────────────
  container.appendChild(section('Charge severity', `
    <div class="narcoticsCheatsheetCharges">
      <div><strong>Possession</strong> — misd. / felony</div>
      <div><strong>For sale / transport / trafficking</strong> — felony, years</div>
      <div><strong>Manufacturing meth</strong> — felony, long sentence</div>
      <div><strong>Grow weed</strong> — misd. or felony</div>
      <div><strong>Paraphernalia / under influence</strong> — misd.</div>
    </div>
  `))

  // ─── Paraphernalia Identification ──────────────────────────────────
  container.appendChild(section('Paraphernalia', ul([
    '<strong>Smoking:</strong> Pipes, bongs, one-hitters, rolling papers, blunt wraps',
    '<strong>Injection:</strong> Needles, syringes, spoons, tourniquets, cotton',
    '<strong>Snorting:</strong> Mirrors, razor blades, straws, rolled bills',
    '<strong>General:</strong> Scales, baggies, pill bottles (unlabeled), cut straws',
    '<strong>Meth:</strong> Glass pipes, lightbulbs, lithium batteries, pseudoephedrine packaging'
  ])))

  // ─── FENTANYL SAFETY ───────────────────────────────────────────────
  const fentanylSection = section('Fentanyl — stay safe', `
    <div class="narcoticsCheatsheetAlert narcoticsCheatsheetFentanyl">
      <ul>
        <li>Gloves (nitrile) if drugs may be present; mask if powder / dust visible</li>
        <li>Narcan for opioid OD—not for brief skin touch. If unwell: fresh air, wash, medical</li>
        <li>Often mixed into coke, meth, heroin — don’t sniff, taste, or stir up powder</li>
      </ul>
    </div>
  `)
  fentanylSection.classList.add('narcoticsCheatsheetSectionFentanyl')
  container.appendChild(fentanylSection)

  // ─── Evidence & Chain of Custody ───────────────────────────────────
  container.appendChild(section('Evidence', ul([
    'Bag, label (when/where/who/case), photo qty & packaging',
    'PER in MDT for seizures; log every handoff'
  ])))

  // ─── Search Considerations ─────────────────────────────────────────
  container.appendChild(section('Search quick', ul([
    '<strong>PC:</strong> plain view, odor, consent, SIA',
    '<strong>Car:</strong> inventory, some states—MJ smell',
    '<strong>Person:</strong> Terry pat → full search if cuffed',
    '<strong>Miranda:</strong> custodial interview only'
  ])))

  return container
}
