async function getImpoundSection (data = {}, isList = false) {
  const language = await getLanguage()
  const section = document.createElement('div')
  section.classList.add('section', 'impoundSection')
  if (isList) section.classList.add('searchResponseWrapper')

  const title = document.createElement('div')
  title.classList.add(isList ? 'searchResponseSectionTitle' : 'title')
  title.innerHTML = language.reports?.sections?.impound?.title || 'Vehicle & Impound Details'

  // GTA 5 lore: two LSPD Auto Impound locations (Mission Row & Davis)
  const IMPOUND_LOTS = [
    'LSPD Auto Impound — Mission Row (Sinner St & Vespucci Blvd)',
    'LSPD Auto Impound — Davis (Roy Lowenstein Blvd & Innocence Blvd)'
  ]
  // GTA 5 lore: tow companies in Los Santos (Camel Towing, Davis Towing / Davis Towing Impound)
  const TOW_COMPANIES = ['', 'Camel Towing', 'Davis Towing']

  const labels = language.reports?.sections?.impound || {}
  // If creating new report and no lot set, randomize which impound lot
  const resolvedData = { ...data }
  if (!resolvedData.ImpoundLot && !isList) {
    resolvedData.ImpoundLot = IMPOUND_LOTS[Math.floor(Math.random() * IMPOUND_LOTS.length)]
  }

  const fields = [
    { id: 'impoundSectionPlateInput', key: 'LicensePlate', label: labels.licensePlate || 'License Plate' },
    { id: 'impoundSectionModelInput', key: 'VehicleModel', label: labels.model || 'Model' },
    { id: 'impoundSectionOwnerInput', key: 'Owner', label: labels.owner || 'Owner' },
    { id: 'impoundSectionVinInput', key: 'Vin', label: labels.vin || 'VIN' },
    { id: 'impoundSectionReasonInput', key: 'ImpoundReason', label: labels.impoundReason || 'Impound Reason', tag: 'select', options: ['', 'Stolen recovery', 'Abandoned', 'Evidence', 'Traffic violation', 'No insurance', 'Other'] },
    { id: 'impoundSectionTowInput', key: 'TowCompany', label: labels.towCompany || 'Tow Company', tag: 'select', options: TOW_COMPANIES },
    { id: 'impoundSectionLotInput', key: 'ImpoundLot', label: labels.impoundLot || 'Impound Lot', readOnly: true }
  ]

  const wrapper = document.createElement('div')
  wrapper.classList.add('inputWrapper', 'grid')

  for (const f of fields) {
    const cell = document.createElement('div')
    const lbl = document.createElement('label')
    lbl.htmlFor = f.id
    lbl.innerHTML = f.label
    const input = (f.tag === 'select' || f.options) && !f.readOnly
      ? document.createElement('select')
      : document.createElement('input')
    input.id = f.id
    if (input.tagName === 'INPUT') {
      input.type = 'text'
      if (f.readOnly) {
        input.readOnly = true
        input.classList.add('impoundLotReadOnly')
      }
    }
    input.disabled = isList
    if (f.options && !f.readOnly) {
      f.options.forEach((opt) => {
        const o = document.createElement('option')
        o.value = opt
        o.textContent = opt === '' ? '' : (opt || '—')
        input.appendChild(o)
      })
    }
    input.value = resolvedData?.[f.key] || ''
    cell.appendChild(lbl)
    cell.appendChild(input)
    if (f.readOnly) cell.classList.add('fullWidth')
    wrapper.appendChild(cell)
  }
  section.appendChild(title)
  section.appendChild(wrapper)
  return section
}
