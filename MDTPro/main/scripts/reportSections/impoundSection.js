async function getImpoundSection (data = {}, isList = false) {
  const language = await getLanguage()
  const section = document.createElement('div')
  section.classList.add('section', 'impoundSection')

  const labels = language.reports?.sections?.impound || {}
  const title = document.createElement('div')
  title.classList.add('title')
  title.innerHTML = labels.title || 'Vehicle & Impound Details'
  section.appendChild(title)

  // Nearby vehicles row (create form only): click to prefill plate, model, owner, VIN
  if (!isList) {
    const nearbyRow = document.createElement('div')
    nearbyRow.classList.add('impoundNearbyRow', 'inputWrapper')
    const nearbyLabel = document.createElement('div')
    nearbyLabel.classList.add('impoundNearbyLabel')
    nearbyLabel.textContent = labels.nearbyVehiclesTitle || 'Nearby vehicles'
    const nearbyHeader = document.createElement('div')
    nearbyHeader.classList.add('impoundNearbyHeader')
    const refreshBtn = document.createElement('button')
    refreshBtn.type = 'button'
    refreshBtn.classList.add('impoundNearbyRefresh')
    refreshBtn.textContent = labels.refreshNearby || 'Refresh'
    const listEl = document.createElement('div')
    listEl.classList.add('impoundNearbyList')
    nearbyHeader.appendChild(refreshBtn)
    nearbyRow.appendChild(nearbyLabel)
    nearbyRow.appendChild(nearbyHeader)
    nearbyRow.appendChild(listEl)
    section.appendChild(nearbyRow)

    async function loadNearbyForImpound () {
      listEl.innerHTML = ''
      try {
        const res = await fetch('/data/nearbyVehicles', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: '8'
        })
        const list = (res.ok ? await res.json() : null) || []
        if (!Array.isArray(list) || list.length === 0) {
          listEl.textContent = labels.noNearbyVehicles || 'No vehicles detected nearby.'
          return
        }
        for (const vehicle of list) {
          const btn = document.createElement('button')
          btn.type = 'button'
          btn.classList.add('impoundNearbyItem')
          if (vehicle.IsStolen) btn.classList.add('stolen')
          const model = vehicle.ModelDisplayName ? ` — ${vehicle.ModelDisplayName}` : ''
          const dist = vehicle.Distance != null ? ` (${vehicle.Distance.toFixed(1)}m)` : ''
          btn.textContent = `${vehicle.LicensePlate || ''}${model}${dist}`
          btn.addEventListener('click', async function () {
            const plate = vehicle.LicensePlate
            if (!plate) return
            try {
              const vRes = await fetch('/data/specificVehicle', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(plate)
              })
              const v = vRes.ok ? await vRes.json() : null
              if (!v) return
              // Query from document so we always target the current create-page form (section may be stale if DOM was re-rendered)
              const container = document.querySelector('.createPage .reportInformation')
              const plateInput = container?.querySelector('#impoundSectionPlateInput')
              const modelInput = container?.querySelector('#impoundSectionModelInput')
              const ownerInput = container?.querySelector('#impoundSectionOwnerInput')
              const vinInput = container?.querySelector('#impoundSectionVinInput')
              if (plateInput) plateInput.value = v.LicensePlate || ''
              if (modelInput) modelInput.value = v.ModelDisplayName || v.ModelName || ''
              if (ownerInput) ownerInput.value = v.Owner || ''
              if (vinInput) vinInput.value = v.VehicleIdentificationNumber || v.VinStatus || ''
              if (typeof topWindow !== 'undefined' && topWindow.showNotification) {
                topWindow.showNotification(labels.prefilledFromNearby || 'Vehicle details filled from nearby.', 'info')
              }
            } catch (_) {}
          })
          listEl.appendChild(btn)
        }
      } catch (_) {
        listEl.textContent = labels.noNearbyVehicles || 'No vehicles detected nearby.'
      }
    }

    refreshBtn.addEventListener('click', function () {
      if (refreshBtn.classList.contains('loading')) return
      refreshBtn.classList.add('loading')
      loadNearbyForImpound().then(() => refreshBtn.classList.remove('loading'))
    })
    loadNearbyForImpound()
  }

  // GTA 5 lore: two LSPD Auto Impound locations (Mission Row & Davis)
  const IMPOUND_LOTS = [
    'LSPD Auto Impound — Mission Row (Sinner St & Vespucci Blvd)',
    'LSPD Auto Impound — Davis (Roy Lowenstein Blvd & Innocence Blvd)'
  ]
  // GTA 5 lore: tow companies in Los Santos (Camel Towing, Davis Towing / Davis Towing Impound)
  const TOW_COMPANIES = ['', 'Camel Towing', 'Davis Towing']

  // Recent IDs for Person at fault (create form only)
  if (!isList) {
    const recentIdsRow = document.createElement('div')
    recentIdsRow.classList.add('inputWrapper', 'impoundRecentIdsRow')
    const recentIdsLabel = document.createElement('label')
    recentIdsLabel.textContent = labels.selectFromRecentIds || 'Select person at fault (Recent IDs)'
    const recentIdsList = document.createElement('div')
    recentIdsList.className = 'impoundRecentIdsList'
    recentIdsRow.appendChild(recentIdsLabel)
    recentIdsRow.appendChild(recentIdsList)
    section.appendChild(recentIdsRow)
    ;(async function () {
      try {
        const res = await fetch('/data/recentIds')
        const recentIds = res.ok ? await res.json() : []
        recentIdsList.innerHTML = ''
        if (recentIds && recentIds.length > 0) {
          for (const entry of recentIds) {
            const btn = document.createElement('button')
            btn.type = 'button'
            btn.className = 'impoundRecentIdItem'
            btn.textContent = entry.Name || '—'
            if (entry.Type) {
              const span = document.createElement('span')
              span.className = 'impoundRecentIdType'
              span.textContent = ` (${entry.Type})`
              btn.appendChild(span)
            }
            btn.addEventListener('click', function () {
              const name = (entry.Name || '').trim()
              if (!name) return
              const inp = document.querySelector('.createPage .reportInformation #impoundSectionPersonAtFaultInput')
              if (inp) inp.value = name
            })
            recentIdsList.appendChild(btn)
          }
        } else {
          recentIdsList.textContent = labels.noRecentIds || 'No recent IDs. Collect an ID from a ped to show them here.'
        }
      } catch (e) {
        recentIdsList.textContent = labels.recentIdsError || 'Could not load Recent IDs.'
      }
    })()
  }

  // If creating new report and no lot set, randomize which impound lot
  const resolvedData = { ...data }
  if (!resolvedData.ImpoundLot && !isList) {
    resolvedData.ImpoundLot = IMPOUND_LOTS[Math.floor(Math.random() * IMPOUND_LOTS.length)]
  }

  const fields = [
    { id: 'impoundSectionPersonAtFaultInput', key: 'PersonAtFaultName', label: labels.personAtFault || 'Person at fault' },
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
