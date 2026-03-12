async function getInjurySection (data = {}, isList = false) {
  const language = await getLanguage()
  const section = document.createElement('div')
  section.classList.add('section', 'injurySection')
  if (isList) section.classList.add('searchResponseWrapper')

  const labels = language.reports?.sections?.injury || {}

  const title = document.createElement('div')
  title.classList.add(isList ? 'searchResponseSectionTitle' : 'title')
  title.innerHTML = labels.title || 'Injury Details'
  section.appendChild(title)

  if (!isList) {
    const recentIdsRow = document.createElement('div')
    recentIdsRow.classList.add('inputWrapper', 'injuryRecentIdsRow')
    const recentIdsLabel = document.createElement('label')
    recentIdsLabel.textContent = labels.selectFromRecentIds || 'Select injured party (Recent IDs)'
    const recentIdsList = document.createElement('div')
    recentIdsList.className = 'injuryRecentIdsList'
    recentIdsRow.appendChild(recentIdsLabel)
    recentIdsRow.appendChild(recentIdsList)
    section.appendChild(recentIdsRow)
    ;(async function () {
      try {
        const res = await fetch('/data/recentIds')
        const recentIds = await res.json()
        recentIdsList.innerHTML = ''
        if (recentIds && recentIds.length > 0) {
          for (const entry of recentIds) {
            const btn = document.createElement('button')
            btn.type = 'button'
            btn.className = 'injuryRecentIdItem'
            btn.dataset.pedName = entry.Name || ''
            btn.textContent = entry.Name || '—'
            if (entry.Type) {
              const span = document.createElement('span')
              span.className = 'injuryRecentIdType'
              span.textContent = ` (${entry.Type})`
              btn.appendChild(span)
            }
            btn.addEventListener('click', async function () {
              const name = (btn.dataset.pedName || '').trim()
              if (!name) return
              const pedNameInput = section.querySelector('#injurySectionInjuredPartyInput')
              if (pedNameInput) pedNameInput.value = name
              await applyInjuryGameDataToSection(section, name, labels)
            })
            recentIdsList.appendChild(btn)
          }
        } else {
          recentIdsList.textContent = labels.noRecentIds || 'No recent IDs. Collect an ID from a ped (e.g. traffic stop) to show them here.'
        }
      } catch (e) {
        recentIdsList.textContent = labels.recentIdsError || 'Could not load Recent IDs.'
      }
    })()
  }

  const injuryTypeSuggestions = ['Gunshot', 'Fall', 'Stab wound', 'Blunt trauma', 'Assault (unarmed)', 'Burns', 'Vehicle impact', 'Explosion', 'Less lethal', 'Drowning', 'Animal attack']
  const severitySuggestions = ['Minor', 'Moderate', 'Serious', 'Critical', 'Fatal']
  const treatmentSuggestions = ['First aid on scene', 'EMS on scene', 'Transported to hospital', 'Pronounced deceased on scene', 'DOA', 'Refused treatment']

  const fields = [
    { id: 'injurySectionInjuredPartyInput', key: 'InjuredPartyName', label: labels.injuredParty || 'Injured party' },
    { id: 'injurySectionInjuryTypeInput', key: 'InjuryType', label: labels.injuryType || 'Injury type', datalist: injuryTypeSuggestions },
    { id: 'injurySectionSeverityInput', key: 'Severity', label: labels.severity || 'Severity', datalist: severitySuggestions },
    { id: 'injurySectionTreatmentInput', key: 'Treatment', label: labels.treatment || 'Treatment', datalist: treatmentSuggestions },
    { id: 'injurySectionContextInput', key: 'IncidentContext', label: labels.incidentContext || 'Incident context', tag: 'textarea' },
    { id: 'injurySectionLinkedReportInput', key: 'LinkedReportId', label: labels.linkedReportId || 'Linked report ID' }
  ]

  const wrapper = document.createElement('div')
  wrapper.classList.add('inputWrapper', 'grid')

  for (const f of fields) {
    const cell = document.createElement('div')
    const lbl = document.createElement('label')
    lbl.htmlFor = f.id
    lbl.innerHTML = f.label
    const input = f.tag === 'textarea'
      ? document.createElement('textarea')
      : document.createElement('input')
    input.id = f.id
    if (input.tagName === 'INPUT') input.type = 'text'
    input.disabled = isList
    if (input.tagName === 'INPUT') input.autocomplete = 'off'
    if (f.datalist && f.datalist.length > 0 && input.tagName === 'INPUT') {
      const listId = f.id + '-list'
      const datalist = document.createElement('datalist')
      datalist.id = listId
      f.datalist.forEach((opt) => {
        const o = document.createElement('option')
        o.value = opt
        datalist.appendChild(o)
      })
      cell.appendChild(datalist)
      input.setAttribute('list', listId)
    }
    input.value = data?.[f.key] || ''
    if (f.tag === 'textarea') cell.classList.add('fullWidth')
    cell.appendChild(lbl)
    cell.appendChild(input)
    wrapper.appendChild(cell)
  }

  section.appendChild(wrapper)

  if (!isList) {
    const gameDataRow = document.createElement('div')
    gameDataRow.classList.add('inputWrapper', 'injuryGameDataRow')
    const importBtn = document.createElement('button')
    importBtn.type = 'button'
    importBtn.className = 'button importInjuryFromGameBtn'
    importBtn.textContent = labels.importFromGame || 'Import from game'
    const gameDataNote = document.createElement('div')
    gameDataNote.className = 'injuryGameDataNote'
    gameDataNote.setAttribute('aria-live', 'polite')
    gameDataRow.appendChild(importBtn)
    gameDataRow.appendChild(gameDataNote)
    section.appendChild(gameDataRow)

    importBtn.addEventListener('click', async function () {
      const pedNameInput = section.querySelector('#injurySectionInjuredPartyInput')
      const pedName = (pedNameInput?.value || '').trim()
      await applyInjuryGameDataToSection(section, pedName || null, labels, gameDataNote)
    })
  }

  return section
}

async function applyInjuryGameDataToSection (section, pedName, labels, gameDataNoteEl) {
  const noteEl = gameDataNoteEl || section.querySelector('.injuryGameDataNote')
  const url = pedName
    ? `/data/injuryGameData?pedName=${encodeURIComponent(pedName)}`
    : '/data/injuryGameData'
  try {
    const res = await fetch(url)
    const data = await res.json()
    if (!data || (data.Source === undefined && data.InjuryType == null && data.Severity == null && data.Health == null)) {
      if (noteEl) noteEl.textContent = labels.noGameData || 'No in-game data for this person. Ensure they are nearby or use DamageTrackerFramework.'
      return
    }
    if (data.InjuryType) {
      const typeInput = section.querySelector('#injurySectionInjuryTypeInput')
      if (typeInput) typeInput.value = data.InjuryType
    }
    if (data.Severity) {
      const severitySelect = section.querySelector('#injurySectionSeverityInput')
      if (severitySelect) severitySelect.value = data.Severity
    }
    if (data.Treatment) {
      const treatmentInput = section.querySelector('#injurySectionTreatmentInput')
      if (treatmentInput) treatmentInput.value = data.Treatment
    }
    section.dataset.gameInjurySnapshot = JSON.stringify(data)
    if (noteEl) {
      noteEl.textContent = data.Source === 'DamageTracker'
        ? (labels.basedOnDamageTracker || 'Based on in-game damage (DamageTrackerFramework).')
        : (labels.basedOnHealth || 'Based on current health/armor.')
    }
  } catch (e) {
    if (noteEl) noteEl.textContent = labels.importError || 'Could not load game data.'
  }
}
