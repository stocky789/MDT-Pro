;(async function () {
  const config = await getConfig()
  if (config.updateDomWithLanguageOnLoad)
    await updateDomWithLanguage('pedSearch')

  await loadRecentIds()
  await loadSearchHistory()

  document
    .querySelector('.clearSearchHistoryBtn')
    .addEventListener('click', async function () {
      await fetch('/post/clearSearchHistory', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: 'ped',
      })
      await loadSearchHistory()
    })

  // Auto-refresh Recent IDs and search history for real-time updates (3s to avoid excessive server/game load)
  const REFRESH_INTERVAL_MS = 3000
  let refreshTimer = null
  function startRefreshTimer() {
    if (refreshTimer) return
    refreshTimer = setInterval(async () => {
      if (document.hidden) return
      await loadRecentIds()
      await loadSearchHistory()
      filterPersonLists(document.querySelector('.searchInputWrapper #pedSearchInput')?.value?.trim?.())
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

const searchInput = document.querySelector('.searchInputWrapper #pedSearchInput')

searchInput.addEventListener('keydown', async function (e) {
  if (e.key == 'Enter') {
    e.preventDefault()
    document.querySelector('.searchInputWrapper button').click()
  }
})

searchInput.addEventListener('input', function () {
  filterPersonLists(this.value.trim())
})

function filterPersonLists(searchText) {
  const q = (searchText || '').toLowerCase()
  const lists = [
    document.querySelector('.recentIdsList'),
    document.querySelector('.searchHistoryList'),
  ]
  const wrappers = [
    document.querySelector('.recentIdsWrapper'),
    document.querySelector('.searchHistoryWrapper'),
  ]
  lists.forEach((list, i) => {
    if (!list || !wrappers[i]) return
    let visibleCount = 0
    for (const item of list.children) {
      const name = (item.dataset.searchName || item.textContent || '').toLowerCase()
      const show = !q || name.includes(q)
      item.style.display = show ? '' : 'none'
      if (show) visibleCount++
    }
    if (wrappers[i].classList.contains('hidden')) return
    wrappers[i].style.display = q && visibleCount === 0 ? 'none' : ''
  })
}

document
  .querySelector('.searchInputWrapper button')
  .addEventListener('click', async function () {
    if (this.classList.contains('loading')) return
    showLoadingOnButton(this)

    this.blur()
    await performSearch(
      document.querySelector('.searchInputWrapper #pedSearchInput').value.trim()
    )

    hideLoadingOnButton(this)
  })

async function loadRecentIds() {
  let recentIds = []
  try {
    const res = await fetch('/data/recentIds')
    recentIds = res.ok ? await res.json() : []
  } catch (e) {
    recentIds = []
  }

  // Dev server fallback: when on localhost:3010 (dev server) and no data, show placeholder names so you don't have to remember them
  const isDevServer = (typeof window !== 'undefined' && window.location && (window.location.port === '3010') && (window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1'))
  if (isDevServer && (!recentIds || recentIds.length === 0)) {
    recentIds = [
      { Name: 'John Doe', Type: 'State ID' },
      { Name: 'Jane Smith', Type: 'State ID' },
      { Name: 'Mike Johnson', Type: 'State ID' },
      { Name: 'Sarah Davis', Type: 'State ID' },
      { Name: 'Tom Miller', Type: 'State ID' },
      { Name: 'Alice Brown', Type: 'State ID' },
      { Name: 'Bob Wilson', Type: 'State ID' },
    ]
  }

  const wrapper = document.querySelector('.recentIdsWrapper')
  const list = document.querySelector('.recentIdsList')
  list.innerHTML = ''

  if (!recentIds || recentIds.length === 0) {
    wrapper.classList.add('hidden')
    return
  }

  wrapper.classList.remove('hidden')

  for (const entry of recentIds) {
    const item = document.createElement('button')
    item.dataset.searchName = entry.Name
    item.innerHTML = `${entry.Name} <span class="searchCount">(${entry.Type})</span>`
    item.addEventListener('click', async function () {
      document.querySelector('.searchInputWrapper #pedSearchInput').value = entry.Name
      document.querySelector('.searchInputWrapper button').click()
    })
    list.appendChild(item)
  }
}

async function loadSearchHistory() {
  const res = await fetch('/data/searchHistory', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: 'ped',
  })
  const history = res.ok ? await res.json().catch(() => []) : []

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
    item.dataset.searchName = entry.ResultName
    item.textContent = entry.ResultName
    item.addEventListener('click', async function () {
      document.querySelector('.searchInputWrapper #pedSearchInput').value =
        entry.ResultName
      document.querySelector('.searchInputWrapper button').click()
    })
    list.appendChild(item)
  }
}

let _pedSearchSeq = 0
let _pedSearchAbort = null

async function performSearch(query) {
  _pedSearchSeq++
  const thisSearch = _pedSearchSeq
  _pedSearchAbort?.abort()
  _pedSearchAbort = new AbortController()
  const signal = _pedSearchAbort.signal
  const stale = () => thisSearch !== _pedSearchSeq

  const language = await getLanguage()
  if (stale()) return
  const notifs = language?.pedSearch?.notifications || {}
  if (!query) {
    topWindow.showNotification(
      notifs.emptySearchInput || 'Enter a name to search.',
      'warning'
    )
    return
  }

  let response
  try {
    const res = await fetch('/data/specificPed', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: query,
      signal,
    })
    if (stale()) return
    response = res.ok ? await res.json().catch(() => null) : null
  } catch (e) {
    if (e?.name === 'AbortError') return
    throw e
  }
  if (stale()) return

  if (!response) {
    topWindow.showNotification(
      notifs.pedNotFound || 'Person not found.',
      'warning'
    )
    return
  }

  try {
  // Alert notifications for wanted/probation/parole/advisory
  if (response.IsWanted) {
    topWindow.showNotification(
      `${notifs.wanted || 'WANTED'}: ${response.Name} \u2014 ${response.WarrantText}`,
      'warning',
      -1
    )
  }
  if (response.IsOnProbation) {
    topWindow.showNotification(
      `${notifs.advisory || 'ADVISORY'}: ${response.Name} ${notifs.isOnProbation || 'is on probation'}`,
      'warning',
      8000
    )
  }
  if (response.IsOnParole) {
    topWindow.showNotification(
      `${notifs.advisory || 'ADVISORY'}: ${response.Name} ${notifs.isOnParole || 'is on parole'}`,
      'warning',
      8000
    )
  }
  if (response.AdvisoryText) {
    topWindow.showNotification(
      `${notifs.advisory || 'ADVISORY'}: ${response.AdvisoryText}`,
      'info',
      8000
    )
  }

  document.title = `${(language?.pedSearch?.static?.title) || 'Person Search'}: ${response.Name}`

  document.querySelector('.searchResponseWrapper').classList.remove('hidden')

  // ID photo: use FiveM ped model image when ModelName is available (vanilla GTA peds)
  const photoImg = document.getElementById('pedIdPhotoImg')
  const photoPlaceholder = document.querySelector('.pedIdPhotoPlaceholder')
  if (photoImg && photoPlaceholder) {
    const modelName = (response.ModelName || '').trim().toLowerCase()
    if (modelName) {
      photoImg.src = `https://docs.fivem.net/peds/${modelName}.webp`
      photoImg.classList.remove('hidden')
      photoImg.alt = response.Name || ''
      photoPlaceholder.classList.add('hidden')
      photoImg.onerror = () => {
        photoImg.classList.add('hidden')
        photoImg.removeAttribute('src')
        photoPlaceholder.classList.remove('hidden')
      }
    } else {
      photoImg.classList.add('hidden')
      photoImg.removeAttribute('src')
      photoPlaceholder.classList.remove('hidden')
    }
  }

  for (const key of Object.keys(response)) {
    const el = document.querySelector(
      `.searchResponseWrapper [data-property="${key}"]`
    )
    if (!el) continue
    switch (key) {
      case 'Birthday':
        el.value = new Date(response[key]).toLocaleDateString()
        document.querySelector(
          '.searchResponseWrapper [data-property="Age"]'
        ).value = Math.abs(
          new Date(
            Date.now() - new Date(response[key]).getTime()
          ).getFullYear() - 1970
        )
        break
      case 'IsWanted':
        el.value = response[key]
          ? `${language.values.wanted} ${response.WarrantText}`
          : language.values.notWanted
        el.style.color = getColorForValue(response[key])
        break
      case 'AdvisoryText':
        el.value = removeGTAColorCodesFromString(response[key])
        if (response[key] != undefined) el.style.color = 'var(--color-error)'
        break
      case 'LicenseExpiration':
      case 'WeaponPermitExpiration':
      case 'HuntingPermitExpiration':
      case 'FishingPermitExpiration':
        el.value = await getLanguageValue(response[key])
        el.value =
          response[key] == null
            ? await getLanguageValue(response[key])
            : new Date(response[key]).toLocaleDateString()

        if (
          response[key] != null &&
          new Date(response[key]).getTime() < Date.now()
        ) {
          el.style.color = 'var(--color-warning)'
        }
        break
      case 'WeaponPermitType':
        el.value = await getLanguageValue(
          response.WeaponPermitStatus == 'Valid' ? response[key] : null
        )
        break
      case 'Citations':
      case 'Arrests': {
        const arr = Array.isArray(response[key]) ? response[key] : []
        el.parentElement.classList.add('clickable')
        el.parentElement.onclick = () =>
          openPedAsOffenderInReport(
            key == 'Citations' ? 'citation' : 'arrest',
            response.Name
          )
        el.innerHTML =
          arr.length > 0
            ? arr.map((item) => `<li>${item?.name ?? ''}</li>`).join('')
            : await getLanguageValue(null)
        break
      }
      default:
        el.value = await getLanguageValue(response[key])
        el.style.color = getColorForValue(response[key])
    }
  }

  // ID History
  const idHistoryTitle = document.querySelector('.searchResponseWrapper .idHistoryTitle')
  const idHistorySection = document.querySelector('.searchResponseWrapper .idHistory')
  idHistorySection.innerHTML = ''
  const idHistory = response.IdentificationHistory
  if (idHistory && idHistory.length > 0) {
    idHistoryTitle.classList.remove('hidden')
    idHistorySection.classList.remove('hidden')
    for (const entry of idHistory) {
      const el = document.createElement('div')
      const label = document.createElement('label')
      label.textContent = entry.Type
      const input = document.createElement('input')
      input.type = 'text'
      input.disabled = true
      input.value = new Date(entry.Timestamp).toLocaleString()
      el.appendChild(label)
      el.appendChild(input)
      idHistorySection.appendChild(el)
    }
  } else {
    idHistoryTitle.classList.add('hidden')
    idHistorySection.classList.add('hidden')
  }

  // Create Report buttons
  document.querySelector('.searchResponseWrapper .createCitationBtn').onclick = () =>
    openPedAsOffenderInReport('citation', response.Name)
  document.querySelector('.searchResponseWrapper .createArrestBtn').onclick = () =>
    openPedAsOffenderInReport('arrest', response.Name)
  const createInjuryBtn = document.querySelector('.searchResponseWrapper .createInjuryReportBtn')
  if (createInjuryBtn) {
    createInjuryBtn.onclick = () =>
      openReportWithPrefill('injury', { pedName: response.Name, source: 'pedSearch' })
  }

  if (stale()) return

  // Drug records for this ped
  let drugsResponse
  try {
    const dr = await fetch('/data/drugsByOwner', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(response.Name ?? ''),
      signal,
    })
    if (stale()) return
    drugsResponse = await dr.json()
  } catch (e) {
    if (e?.name === 'AbortError') return
    throw e
  }
  if (stale()) return

  document
    .querySelectorAll(
      '.searchResponseWrapper .drugRecordsSection, .searchResponseWrapper .drugRecordsTitle'
    )
    .forEach((el) => el?.remove())

  if (drugsResponse && drugsResponse.length > 0) {
    const sectionTitle = document.createElement('div')
    sectionTitle.classList.add('searchResponseSectionTitle', 'drugRecordsTitle')
    sectionTitle.innerHTML =
      language?.pedSearch?.static?.drugRecordsTitle || 'Substance History'
    document.querySelector('.searchResponseWrapper').appendChild(sectionTitle)

    const drugsSection = document.createElement('div')
    drugsSection.classList.add('inputWrapper', 'grid', 'drugRecordsSection')
    for (const d of drugsResponse) {
      const el = document.createElement('div')
      const label = document.createElement('label')
      label.textContent = d.DrugType || 'Unknown'
      const input = document.createElement('input')
      input.type = 'text'
      input.disabled = true
      input.value = `${d.Source || ''} - ${d.Description || ''}`
      el.appendChild(label)
      el.appendChild(input)
      drugsSection.appendChild(el)
    }
    document.querySelector('.searchResponseWrapper').appendChild(drugsSection)
  }

  // Vehicles owned by this ped
  let vehiclesResponse
  try {
    const vr = await fetch('/data/pedVehicles', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: response.Name,
      signal,
    })
    if (stale()) return
    vehiclesResponse = await vr.json()
  } catch (e) {
    if (e?.name === 'AbortError') return
    throw e
  }
  if (stale()) return

  document
    .querySelectorAll(
      '.searchResponseWrapper .vehiclesOwned, .searchResponseWrapper .vehiclesOwnedTitle'
    )
    .forEach((el) => el.remove())

  if (vehiclesResponse && vehiclesResponse.length > 0) {
    const sectionTitle = document.createElement('div')
    sectionTitle.classList.add('searchResponseSectionTitle', 'vehiclesOwnedTitle')
    sectionTitle.innerHTML =
      language?.pedSearch?.static?.vehiclesOwnedTitle || 'Vehicles Owned'
    document.querySelector('.searchResponseWrapper').appendChild(sectionTitle)

    const vehiclesSection = document.createElement('div')
    vehiclesSection.classList.add('inputWrapper', 'grid', 'vehiclesOwned')
    document.querySelector('.searchResponseWrapper').appendChild(vehiclesSection)

    for (const vehicle of vehiclesResponse) {
      const el = document.createElement('div')
      el.classList.add('clickable')
      el.addEventListener('click', () =>
        openInVehicleSearch(vehicle.LicensePlate)
      )
      const label = document.createElement('label')
      label.innerHTML = vehicle.LicensePlate
      const input = document.createElement('input')
      input.type = 'text'
      input.disabled = true
      input.value = vehicle.ModelDisplayName || vehicle.LicensePlate
      if (vehicle.IsStolen) input.style.color = 'var(--color-error)'
      el.appendChild(label)
      el.appendChild(input)
      vehiclesSection.appendChild(el)
    }
  }

  // Registered Firearms (from pat-down / dead body search)
  let firearmsResponse
  try {
    const fr = await fetch('/data/firearmsForPed', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(response.Name ?? ''),
      signal,
    })
    if (stale()) return
    firearmsResponse = await fr.json()
  } catch (e) {
    if (e?.name === 'AbortError') return
    throw e
  }
  if (stale()) return

  document
    .querySelectorAll(
      '.searchResponseWrapper .registeredFirearms, .searchResponseWrapper .registeredFirearmsTitle'
    )
    .forEach((el) => el.remove())

  if (firearmsResponse && firearmsResponse.length > 0) {
    const sectionTitle = document.createElement('div')
    sectionTitle.classList.add('searchResponseSectionTitle', 'registeredFirearmsTitle')
    sectionTitle.innerHTML =
      language?.pedSearch?.static?.registeredFirearmsTitle || 'Registered Firearms'
    document.querySelector('.searchResponseWrapper').appendChild(sectionTitle)

    const firearmsSection = document.createElement('div')
    firearmsSection.classList.add('inputWrapper', 'grid', 'registeredFirearms')
    document.querySelector('.searchResponseWrapper').appendChild(firearmsSection)

    for (const firearm of firearmsResponse) {
      const el = document.createElement('div')
      el.classList.add('clickable')
      el.addEventListener('click', () => {
        const lookupKey = firearm.IsSerialScratched ? (firearm.OwnerPedName || response.Name) : (firearm.SerialNumber || firearm.OwnerPedName || response.Name)
        openFirearmsSearch(lookupKey)
      })
      const label = document.createElement('label')
      label.textContent = firearm.IsSerialScratched ? 'Scratched' : (firearm.SerialNumber || 'N/A')
      const input = document.createElement('input')
      input.type = 'text'
      input.disabled = true
      const name = firearm.WeaponDisplayName || firearm.Description || firearm.WeaponModelId || `Weapon (${firearm.WeaponModelHash})`
      input.value = firearm.IsStolen ? `${name} [STOLEN]` : name
      if (firearm.IsStolen) input.style.color = 'var(--color-error)'
      el.appendChild(label)
      el.appendChild(input)
      firearmsSection.appendChild(el)
    }
  }

  // Reports involving this ped
  let reportsResponse
  try {
    const rr = await fetch('/data/pedReports', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: response.Name,
      signal,
    })
    if (stale()) return
    reportsResponse = await rr.json()
  } catch (e) {
    if (e?.name === 'AbortError') return
    throw e
  }
  if (stale()) return

  document
    .querySelectorAll(
      '.searchResponseWrapper .pedReports, .searchResponseWrapper .pedReportsTitle'
    )
    .forEach((el) => el.remove())

  const citations = Array.isArray(reportsResponse?.citations) ? reportsResponse.citations : []
  const arrests = Array.isArray(reportsResponse?.arrests) ? reportsResponse.arrests : []
  const incidents = Array.isArray(reportsResponse?.incidents) ? reportsResponse.incidents : []
  const propertyEvidence = Array.isArray(reportsResponse?.propertyEvidence) ? reportsResponse.propertyEvidence : []
  const injuries = Array.isArray(reportsResponse?.injuries) ? reportsResponse.injuries : []
  const impounds = Array.isArray(reportsResponse?.impounds) ? reportsResponse.impounds : []
  const totalReports = citations.length + arrests.length + incidents.length + propertyEvidence.length + injuries.length + impounds.length

  if (totalReports > 0) {
    const sectionTitle = document.createElement('div')
    sectionTitle.classList.add('searchResponseSectionTitle', 'pedReportsTitle')
    sectionTitle.innerHTML =
      language?.pedSearch?.static?.reportsTitle || 'Associated Reports'
    document.querySelector('.searchResponseWrapper').appendChild(sectionTitle)

    const reportsSection = document.createElement('div')
    reportsSection.classList.add('inputWrapper', 'grid', 'pedReports')
    document.querySelector('.searchResponseWrapper').appendChild(reportsSection)

    const allReports = [
      ...citations.map((r) => ({ ...r, type: 'citation' })),
      ...arrests.map((r) => ({ ...r, type: 'arrest' })),
      ...incidents.map((r) => ({ ...r, type: 'incident' })),
      ...propertyEvidence.map((r) => ({ ...r, type: 'propertyEvidence' })),
      ...injuries.map((r) => ({ ...r, type: 'injury' })),
      ...impounds.map((r) => ({ ...r, type: 'impound' })),
    ].sort((a, b) => new Date(b.TimeStamp) - new Date(a.TimeStamp))

    for (const report of allReports) {
      const el = document.createElement('div')
      el.classList.add('clickable')
      el.addEventListener('click', () =>
        openIdInReport(report.Id, report.type)
      )
      const label = document.createElement('label')
      const typeLabel = report.type === 'propertyEvidence' ? 'Property & Evidence' : report.type === 'injury' ? 'Injury' : report.type === 'impound' ? 'Impound' : report.type.charAt(0).toUpperCase() + report.type.slice(1)
      label.innerHTML = typeLabel
      const input = document.createElement('input')
      input.type = 'text'
      input.disabled = true
      input.value = `${report.Id} - ${new Date(report.TimeStamp).toLocaleDateString()}`
      input.style.color = `var(--color-${statusColorMap[report.Status]})`
      el.appendChild(label)
      el.appendChild(input)
      reportsSection.appendChild(el)
    }
  }

  // Reload search history after successful search
  if (!stale()) await loadSearchHistory()
  } catch (e) {
    if (e?.name === 'AbortError') return
    throw e
  }
}

function getColorForValue(value) {
  switch (value) {
    case true:
    case 'Revoked':
    case 'Unlicensed':
    case 'Suspended':
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
