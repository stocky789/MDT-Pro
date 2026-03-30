;(async function () {
  const config = await getConfig()
  if (config.updateDomWithLanguageOnLoad)
    await updateDomWithLanguage('vehicleSearch')

  const alprPlate = (typeof sessionStorage !== 'undefined' && sessionStorage.getItem('alprVehicleSearchPlate')) || null
  if (alprPlate && typeof alprPlate === 'string') {
    if (typeof sessionStorage !== 'undefined') sessionStorage.removeItem('alprVehicleSearchPlate')
    const trimmed = alprPlate.trim()
    if (trimmed) {
      const input = document.querySelector('.searchInputWrapper #vehicleSearchInput')
      if (input) {
        input.value = trimmed
        try {
          await performSearch(trimmed)
        } catch {
          /* performSearch shows its own error */
        }
      }
    }
  } else {
    try {
      const ctxRes = await fetch('/data/contextVehicle')
      if (ctxRes.ok) {
        const ctx = await ctxRes.json()
        const plate = ctx && typeof ctx.LicensePlate === 'string' ? ctx.LicensePlate.trim() : ''
        const input = document.querySelector('.searchInputWrapper #vehicleSearchInput')
        if (plate && input && !input.value.trim()) {
          input.value = plate
          await performSearch(plate)
        }
      }
    } catch {
      /* ignore — plugin may be offline or no context vehicle */
    }
  }

  await loadNearbyVehicles()
  await loadSearchHistory()

  // Auto-refresh nearby vehicles and search history for real-time updates (3s to avoid excessive server/game load)
  const REFRESH_INTERVAL_MS = 3000
  let refreshTimer = null
  function startRefreshTimer() {
    if (refreshTimer) return
    refreshTimer = setInterval(async () => {
      if (document.hidden) return
      await loadNearbyVehicles()
      await loadSearchHistory()
    }, REFRESH_INTERVAL_MS)
  }
  function stopRefreshTimer() {
    if (refreshTimer) {
      clearInterval(refreshTimer)
      refreshTimer = null
    }
  }
  document.addEventListener('visibilitychange', () => {
    document.hidden ? stopRefreshTimer() : startRefreshTimer()
  })
  if (!document.hidden) startRefreshTimer()
  window.addEventListener('pagehide', stopRefreshTimer)
})()

document
  .querySelector('.searchInputWrapper #vehicleSearchInput')
  .addEventListener('keydown', async function (e) {
    if (e.key == 'Enter') {
      e.preventDefault()
      document.querySelector('.searchInputWrapper button').click()
    }
  })

document
  .querySelector('.searchInputWrapper button')
  .addEventListener('click', async function () {
    if (this.classList.contains('loading')) return
    showLoadingOnButton(this)

    this.blur()
    await performSearch(
      document
        .querySelector('.searchInputWrapper #vehicleSearchInput')
        .value.trim()
    )

    hideLoadingOnButton(this)
  })

document
  .querySelector('.nearbyPlatesRefresh')
  .addEventListener('click', async function () {
    if (this.classList.contains('loading')) return
    showLoadingOnButton(this)
    await loadNearbyVehicles()
    hideLoadingOnButton(this)
  })

async function loadNearbyVehicles() {
  const language = await getLanguage()
  const nearbyVehicles = await (
    await fetch('/data/nearbyVehicles', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: '5',
    })
  ).json()

  const wrapper = document.querySelector('.nearbyPlatesWrapper')
  const list = document.querySelector('.nearbyPlatesList')
  list.innerHTML = ''

  if (!nearbyVehicles || nearbyVehicles.length === 0) {
    wrapper.classList.remove('hidden')
    const emptyEl = document.createElement('div')
    emptyEl.classList.add('searchCount')
    emptyEl.innerHTML =
      language.vehicleSearch.notifications.noNearbyVehicles ||
      'No nearby vehicles found.'
    list.appendChild(emptyEl)
    return
  }

  wrapper.classList.remove('hidden')

  for (const vehicle of nearbyVehicles) {
    const item = document.createElement('button')
    if (vehicle.IsStolen) item.classList.add('stolen')
    const model = vehicle.ModelDisplayName ? ` - ${vehicle.ModelDisplayName}` : ''
    const distance =
      vehicle.Distance != null ? ` (${vehicle.Distance.toFixed(1)}m)` : ''
    item.innerHTML = `${vehicle.LicensePlate}${model}${distance}`
    item.addEventListener('click', function () {
      document.querySelector('.searchInputWrapper #vehicleSearchInput').value =
        vehicle.LicensePlate
      document.querySelector('.searchInputWrapper button').click()
    })
    list.appendChild(item)
  }
}

async function loadSearchHistory() {
  const history = await (
    await fetch('/data/searchHistory', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: 'vehicle',
    })
  ).json()

  const wrapper = document.querySelector('.searchHistoryWrapper')
  const list = document.querySelector('.searchHistoryList')
  list.innerHTML = ''

  if (history.length === 0) {
    wrapper.classList.add('hidden')
    return
  }

  wrapper.classList.remove('hidden')

  for (const entry of history) {
    const item = document.createElement('button')
    item.innerHTML = `${entry.ResultName} <span class="searchCount">(${entry.SearchCount})</span>`
    item.addEventListener('click', async function () {
      document.querySelector('.searchInputWrapper #vehicleSearchInput').value =
        entry.ResultName
      document.querySelector('.searchInputWrapper button').click()
    })
    list.appendChild(item)
  }
}

let integrationStopEventsProviderPromise = null
function getStopEventsProvider() {
  if (!integrationStopEventsProviderPromise) {
    integrationStopEventsProviderPromise = fetch('/integration')
      .then((r) => (r.ok ? r.json() : {}))
      .then((j) => (j && typeof j.stopEventsProvider === 'string' && j.stopEventsProvider) || 'none')
      .catch(() => 'none')
  }
  return integrationStopEventsProviderPromise
}

async function performSearch(query) {
  const language = await getLanguage()
  const stopEventsProvider = await getStopEventsProvider()
  const stpStopIntegrationActive = stopEventsProvider === 'StopThePed'
  if (!query) {
    topWindow.showNotification(
      language.vehicleSearch.notifications.emptySearchInput,
      'warning'
    )
    return
  }
  const res = await fetch('/data/specificVehicle', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: query,
  })
  const response = res.ok ? await res.json().catch(() => null) : null

  if (!response) {
    topWindow.showNotification(
      language.vehicleSearch.notifications.vehicleNotFound,
      'warning'
    )
    return
  }

  // Alert notification for stolen vehicles
  if (response.IsStolen) {
    topWindow.showNotification(
      `${language.vehicleSearch.notifications?.stolen || 'ALERT'}: ${language.vehicleSearch.notifications?.vehicleStolen || 'Vehicle'} ${response.LicensePlate} ${language.vehicleSearch.notifications?.reportedStolen || 'reported STOLEN'}`,
      'error',
      -1
    )
  }

  document.title = `${language.vehicleSearch.static.title}: ${response.LicensePlate}`

  document.querySelector('.searchResponseWrapper').classList.remove('hidden')

  for (const key of Object.keys(response)) {
    const el = document.querySelector(
      `.searchResponseWrapper [data-property="${key}"]`
    )
    if (!el) continue
    switch (key) {
      case 'RegistrationExpiration':
      case 'InsuranceExpiration': {
        el.value = await getLanguageValue(response[key])
        el.value =
          response[key] == null
            ? await getLanguageValue(response[key])
            : new Date(response[key]).toLocaleDateString()

        if (stpStopIntegrationActive) {
          const statusKey =
            key === 'RegistrationExpiration'
              ? 'RegistrationStatus'
              : 'InsuranceStatus'
          const statusNorm = String(response[statusKey] ?? '')
            .trim()
            .toLowerCase()
          const expMs =
            response[key] != null ? new Date(response[key]).getTime() : NaN
          const looksPast = !Number.isNaN(expMs) && expMs < Date.now()
          const warnFromDate = looksPast && statusNorm !== 'valid'
          const warnFromStatus = /expired|revoked|suspended|invalid|none/i.test(
            statusNorm
          )
          if (warnFromDate || warnFromStatus) {
            el.style.color = 'var(--color-warning)'
          }
        } else if (
          response[key] != null &&
          new Date(response[key]).getTime() < Date.now()
        ) {
          el.style.color = 'var(--color-warning)'
        }
        break
      }
      case 'VinStatus':
        el.value = await getLanguageValue(response[key])
        if (response[key] === 'Scratched') el.style.color = 'var(--color-warning)'
        break
      case 'Color': {
        const raw = response[key]
        if (!raw) {
          el.parentElement.classList.add('hidden')
          break
        }
        el.parentElement.classList.remove('hidden')
        const parts = String(raw)
          .split('-')
          .map((s) => s.trim())
        const rgbTriplet =
          parts.length === 3 &&
          parts.every((p) => /^\d{1,3}$/.test(p)) &&
          parts.every((p) => {
            const n = Number(p)
            return n >= 0 && n <= 255
          })
        if (rgbTriplet) {
          const [r, g, b] = parts
          el.textContent = ''
          el.style.backgroundColor = `rgb(${r}, ${g}, ${b})`
          el.style.height = '19px'
        } else {
          el.style.backgroundColor = ''
          el.style.height = ''
          el.textContent = raw
        }
        break
      }
      case 'ModelDisplayName':
        el.parentElement.querySelector('img')?.remove()
        const imageEl = document.createElement('img')
        imageEl.src = `https://docs.fivem.net/vehicles/${response.ModelName.toLowerCase()}.webp`
        imageEl.onerror = () => imageEl.remove()
        el.parentElement.appendChild(imageEl)
        el.value = response[key]
        break
      case 'Owner':
        el.value = await getLanguageValue(response[key])
        if (response[key] && response[key] != 'Government') {
          el.parentElement.classList.add('clickable')
          el.parentElement.onclick = () => openInPedSearch(response[key])
        } else {
          el.parentElement.classList.remove('clickable')
          el.parentElement.onclick = null
        }
        break
      default:
        el.value = await getLanguageValue(response[key])
        el.style.color = getColorForValue(response[key])
    }
  }

  document
    .querySelectorAll(
      '.searchResponseWrapper .vehicleSearchRecordsSection, .searchResponseWrapper .vehicleSearchRecordsTitle, .searchResponseWrapper .impoundActionSection, .searchResponseWrapper .impoundReportsSection, .searchResponseWrapper .impoundReportsTitle'
    )
    .forEach((el) => el?.remove())

  // Vehicle search records (contraband from PR vehicle search)
  let searchRecordsResponse = []
  try {
    const res = await fetch('/data/vehicleSearchByPlate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(response.LicensePlate ?? ''),
    })
    if (res.ok) searchRecordsResponse = await res.json()
  } catch (_) {}
  if (!Array.isArray(searchRecordsResponse)) searchRecordsResponse = []

  // BOLOs section (Be On the Look-Out)
  const boloPlaceholder = document.querySelector('.searchResponseWrapper .boloSectionPlaceholder')
  if (boloPlaceholder) {
    boloPlaceholder.innerHTML = ''
  }
  const bolos = Array.isArray(response.BOLOs) ? response.BOLOs : []
  const canModifyBOLOs = response.CanModifyBOLOs === true
  const boloSection = document.createElement('div')
  boloSection.classList.add('boloSection')
  const boloTitle = document.createElement('div')
  boloTitle.classList.add('searchResponseSectionTitle', 'boloTitle')
  boloTitle.innerHTML = language.vehicleSearch?.static?.bolosTitle || 'BOLOs (Be On the Look-Out)'
  boloSection.appendChild(boloTitle)

  if (!canModifyBOLOs && bolos.length > 0) {
    const hint = document.createElement('div')
    hint.classList.add('boloHint')
    hint.textContent = language.vehicleSearch?.static?.boloRemoveVehicleRequired || 'Vehicle must be nearby to remove BOLOs.'
    boloSection.appendChild(hint)
  }

  if (bolos.length > 0) {
    const boloList = document.createElement('div')
    boloList.classList.add('boloList')
    for (const b of bolos) {
      const reason = b.Reason || 'Unknown'
      const issuedBy = b.IssuedBy || ''
      const exp = b.ExpirationDate || b.ExpiresAt || b.Expires
      const expStr = exp ? new Date(exp).toLocaleDateString() : '-'
      const row = document.createElement('div')
      row.classList.add('boloRow')
      const info = document.createElement('div')
      info.classList.add('boloInfo')
      info.innerHTML = `<strong>${escapeHtml(reason)}</strong>${issuedBy ? ` &mdash; ${escapeHtml(issuedBy)}` : ''} (expires ${escapeHtml(expStr)})`
      row.appendChild(info)
      if (canModifyBOLOs) {
        const removeBtn = document.createElement('button')
        removeBtn.type = 'button'
        removeBtn.classList.add('boloRemoveBtn')
        removeBtn.textContent = language.vehicleSearch?.static?.removeBOLO || 'Remove'
        removeBtn.addEventListener('click', async () => {
          removeBtn.disabled = true
          const res = await (await fetch('/post/removeBOLO', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ LicensePlate: response.LicensePlate, Reason: reason })
          })).json()
          if (res && res.success) {
            topWindow.showNotification(language.vehicleSearch?.notifications?.boloRemoved || 'BOLO removed.', 'checkMark')
            await performSearch(response.LicensePlate)
          } else {
            topWindow.showNotification(res?.error || 'Failed to remove BOLO.', 'warning')
            removeBtn.disabled = false
          }
        })
        row.appendChild(removeBtn)
      }
      boloList.appendChild(row)
    }
    boloSection.appendChild(boloList)
  }

  const addBtn = document.createElement('button')
  addBtn.type = 'button'
  addBtn.classList.add('boloAddBtn')
  addBtn.textContent = language.vehicleSearch?.static?.addBOLO || 'Add BOLO'
  addBtn.addEventListener('click', () => showAddBOLOModal(response, language, performSearch))
  boloSection.appendChild(addBtn)
  if (boloPlaceholder) {
    boloPlaceholder.appendChild(boloSection)
  }

  // Previous impound reports for this vehicle (by plate) — persisted in SQL, so re-encounters show history
  let impoundReports = []
  try {
    const plate = (response.LicensePlate != null && response.LicensePlate !== '') ? response.LicensePlate : ''
    const res = await fetch('/data/impoundReportsByPlate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(plate),
    })
    if (res.ok) {
      const data = await res.json()
      impoundReports = Array.isArray(data) ? data : []
    }
  } catch (_) {
    impoundReports = []
  }
  if (impoundReports.length > 0) {
    const impoundReportsTitle = document.createElement('div')
    impoundReportsTitle.classList.add('searchResponseSectionTitle', 'impoundReportsTitle')
    impoundReportsTitle.innerHTML =
      language.vehicleSearch?.static?.impoundReportsTitle || 'Impound Reports'
    document.querySelector('.searchResponseWrapper').appendChild(impoundReportsTitle)

    const impoundReportsSection = document.createElement('div')
    impoundReportsSection.classList.add('inputWrapper', 'grid', 'impoundReportsSection')
    document.querySelector('.searchResponseWrapper').appendChild(impoundReportsSection)

    const openFn =
      (typeof topWindow !== 'undefined' && typeof topWindow.openIdInReport === 'function')
        ? topWindow.openIdInReport
        : (typeof openIdInReport === 'function' ? openIdInReport : null)

    for (const r of impoundReports) {
      const row = document.createElement('div')
      row.classList.add('clickable')
      if (openFn) {
        row.addEventListener('click', () => openFn(r.Id, 'impound'))
      }

      const label = document.createElement('label')
      label.textContent = language.reports?.list?.reportType?.impound || 'Impound'

      const input = document.createElement('input')
      input.type = 'text'
      input.disabled = true
      const dateStr = r.TimeStamp ? new Date(r.TimeStamp).toLocaleDateString() : ''
      const reason = r.ImpoundReason || ''
      input.value = `${r.Id || ''}${dateStr ? ` - ${dateStr}` : ''}${reason ? ` - ${reason}` : ''}`

      row.appendChild(label)
      row.appendChild(input)
      impoundReportsSection.appendChild(row)
    }
  }

  const impoundSection = document.createElement('div')
  impoundSection.classList.add('impoundActionSection', 'searchResponseSectionTitle')
  const impoundBtn = document.createElement('button')
  impoundBtn.type = 'button'
  impoundBtn.classList.add('createImpoundBtn')
  impoundBtn.textContent = language.vehicleSearch?.createImpoundReport || 'Create Impound Report'
  impoundBtn.addEventListener('click', () => {
    const fn = (typeof topWindow !== 'undefined' && topWindow.openReportWithPrefill) ? topWindow.openReportWithPrefill : (typeof openReportWithPrefill === 'function' ? openReportWithPrefill : null)
    if (fn) {
      fn('impound', {
        source: 'vehicleSearch',
        vehiclePlate: response.LicensePlate,
        vehicleData: {
          LicensePlate: response.LicensePlate,
          ModelDisplayName: response.ModelDisplayName,
          ModelName: response.ModelName,
          Owner: response.Owner,
          VehicleIdentificationNumber: response.VehicleIdentificationNumber,
          VinStatus: response.VinStatus
        }
      })
    }
  })
  impoundSection.appendChild(impoundBtn)
  document.querySelector('.searchResponseWrapper').appendChild(impoundSection)

  if (searchRecordsResponse && searchRecordsResponse.length > 0) {
    const sectionTitle = document.createElement('div')
    sectionTitle.classList.add('searchResponseSectionTitle', 'vehicleSearchRecordsTitle')
    sectionTitle.innerHTML = language.vehicleSearch?.static?.searchResultsTitle || 'Search Results (Contraband)'
    document.querySelector('.searchResponseWrapper').appendChild(sectionTitle)

    const recordsSection = document.createElement('div')
    recordsSection.classList.add('inputWrapper', 'grid', 'vehicleSearchRecordsSection')
    const seen = new Set()
    for (const r of searchRecordsResponse) {
      const key = `${r.ItemType || ''}|${r.Description || ''}|${r.DrugType || ''}|${r.ItemLocation || ''}`
      if (seen.has(key)) continue
      seen.add(key)
      const el = document.createElement('div')
      const isWeapon = !!(r.WeaponModelId || (r.ItemType && /weapon|firearm|gun/i.test(r.ItemType)))
      if (isWeapon) el.classList.add('clickable')
      const label = document.createElement('label')
      label.textContent = r.ItemType || 'Item'
      if (r.ItemLocation) label.textContent += ` (${r.ItemLocation})`
      const input = document.createElement('input')
      input.type = 'text'
      input.disabled = true
      input.value = r.Description || r.DrugType || '-'
      el.appendChild(label)
      el.appendChild(input)
      if (isWeapon) {
        const lookupKey = (r.Description && r.Description.trim()) || r.WeaponModelId || response?.LicensePlate || ''
        el.addEventListener('click', () => openFirearmsSearch(lookupKey))
      }
      recordsSection.appendChild(el)
    }
    document.querySelector('.searchResponseWrapper').appendChild(recordsSection)
  }

  // Reload search history after successful search
  await loadNearbyVehicles()
  await loadSearchHistory()
}

function showAddBOLOModal(vehicleResponse, language, onSuccess) {
  const modal = document.getElementById('addBoloModal')
  const form = document.getElementById('addBoloForm')
  const plateInput = document.getElementById('addBoloPlate')
  const reasonInput = document.getElementById('addBoloReason')
  const expiresInput = document.getElementById('addBoloExpires')
  const cancelBtn = document.querySelector('.addBoloModalCancel')
  if (!modal || !form) return
  plateInput.value = vehicleResponse?.LicensePlate || ''
  reasonInput.value = ''
  expiresInput.value = '7'
  modal.classList.remove('hidden')
  reasonInput.focus()
  function closeModal() {
    modal.classList.add('hidden')
  }
  const cancelHandler = () => {
    cancelBtn.removeEventListener('click', cancelHandler)
    form.onsubmit = null
    modal.onclick = null
    closeModal()
  }
  form.onsubmit = async (e) => {
    e.preventDefault()
    const reason = reasonInput?.value?.trim()
    if (!reason) return
    const expiresDays = parseInt(expiresInput?.value || '7', 10)
    const days = isNaN(expiresDays) || expiresDays < 1 ? 7 : Math.min(365, Math.max(1, expiresDays))
    const expires = new Date()
    expires.setDate(expires.getDate() + days)
    const submitBtn = form.querySelector('.addBoloModalSubmit')
    if (submitBtn) submitBtn.disabled = true
    try {
      const res = await (await fetch('/post/addBOLO', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          LicensePlate: vehicleResponse.LicensePlate,
          Reason: reason,
          ExpiresAt: expires.toISOString(),
          IssuedBy: 'LSPD',
          ModelDisplayName: vehicleResponse?.ModelDisplayName || undefined
        })
      })).json()
      if (res && res.success) {
        if (typeof topWindow !== 'undefined' && typeof topWindow.showNotification === 'function') {
          topWindow.showNotification(language.vehicleSearch?.notifications?.boloAdded || 'BOLO added.', 'checkMark')
        }
        cancelHandler()
        if (typeof onSuccess === 'function') await onSuccess(vehicleResponse.LicensePlate)
      } else {
        if (typeof topWindow !== 'undefined' && typeof topWindow.showNotification === 'function') {
          topWindow.showNotification(res?.error || 'Failed to add BOLO.', 'warning')
        }
      }
    } catch (_) {
      if (typeof topWindow !== 'undefined' && typeof topWindow.showNotification === 'function') {
        topWindow.showNotification('Failed to add BOLO.', 'warning')
      }
    }
    if (submitBtn) submitBtn.disabled = false
  }
  cancelBtn.addEventListener('click', cancelHandler)
  modal.onclick = (e) => {
    if (e.target === modal) cancelHandler()
  }
}

function escapeHtml(s) {
  if (s == null) return ''
  const d = document.createElement('div')
  d.textContent = s
  return d.innerHTML
}

function getColorForValue(value) {
  switch (value) {
    case true:
    case 'Revoked':
    case 'None':
      return 'var(--color-error)'
    case false:
    case 'Valid':
      return 'var(--color-success)'
    case 'Expired':
      return 'var(--color-warning)'
    default:
      return 'var(--color-text-primary)'
  }
}
