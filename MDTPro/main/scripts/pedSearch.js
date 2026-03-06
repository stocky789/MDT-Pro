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
  const recentIds = await (await fetch('/data/recentIds')).json()

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
  const history = await (
    await fetch('/data/searchHistory', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: 'ped',
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

async function performSearch(query) {
  const language = await getLanguage()
  if (!query) {
    topWindow.showNotification(
      language.pedSearch.notifications.emptySearchInput,
      'warning'
    )
    return
  }
  const response = await (
    await fetch('/data/specificPed', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: query,
    })
  ).json()

  if (!response) {
    topWindow.showNotification(
      language.pedSearch.notifications.pedNotFound,
      'warning'
    )
    return
  }

  // Alert notifications for wanted/probation/parole/advisory
  if (response.IsWanted) {
    topWindow.showNotification(
      `${language.pedSearch.notifications?.wanted || 'WANTED'}: ${response.Name} \u2014 ${response.WarrantText}`,
      'warning',
      -1
    )
  }
  if (response.IsOnProbation) {
    topWindow.showNotification(
      `${language.pedSearch.notifications?.advisory || 'ADVISORY'}: ${response.Name} ${language.pedSearch.notifications?.isOnProbation || 'is on probation'}`,
      'warning',
      8000
    )
  }
  if (response.IsOnParole) {
    topWindow.showNotification(
      `${language.pedSearch.notifications?.advisory || 'ADVISORY'}: ${response.Name} ${language.pedSearch.notifications?.isOnParole || 'is on parole'}`,
      'warning',
      8000
    )
  }
  if (response.AdvisoryText) {
    topWindow.showNotification(
      `${language.pedSearch.notifications?.advisory || 'ADVISORY'}: ${response.AdvisoryText}`,
      'info',
      8000
    )
  }

  document.title = `${language.pedSearch.static.title}: ${response.Name}`

  document.querySelector('.searchResponseWrapper').classList.remove('hidden')

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
      case 'Arrests':
        el.parentElement.classList.add('clickable')
        el.parentElement.onclick = () =>
          openPedAsOffenderInReport(
            key == 'Citations' ? 'citation' : 'arrest',
            response.Name
          )
        el.innerHTML =
          response[key].length > 0
            ? response[key].map((item) => `<li>${item.name}</li>`).join('')
            : await getLanguageValue(null)
        break
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

  // Vehicles owned by this ped
  const vehiclesResponse = await (
    await fetch('/data/pedVehicles', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: response.Name,
    })
  ).json()

  document
    .querySelectorAll(
      '.searchResponseWrapper .vehiclesOwned, .searchResponseWrapper .vehiclesOwnedTitle'
    )
    .forEach((el) => el.remove())

  if (vehiclesResponse.length > 0) {
    const sectionTitle = document.createElement('div')
    sectionTitle.classList.add('searchResponseSectionTitle', 'vehiclesOwnedTitle')
    sectionTitle.innerHTML =
      language.pedSearch.static?.vehiclesOwnedTitle || 'Vehicles Owned'
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

  // Reports involving this ped
  const reportsResponse = await (
    await fetch('/data/pedReports', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: response.Name,
    })
  ).json()

  document
    .querySelectorAll(
      '.searchResponseWrapper .pedReports, .searchResponseWrapper .pedReportsTitle'
    )
    .forEach((el) => el.remove())

  const totalReports =
    reportsResponse.citations.length +
    reportsResponse.arrests.length +
    reportsResponse.incidents.length

  if (totalReports > 0) {
    const sectionTitle = document.createElement('div')
    sectionTitle.classList.add('searchResponseSectionTitle', 'pedReportsTitle')
    sectionTitle.innerHTML =
      language.pedSearch.static?.reportsTitle || 'Associated Reports'
    document.querySelector('.searchResponseWrapper').appendChild(sectionTitle)

    const reportsSection = document.createElement('div')
    reportsSection.classList.add('inputWrapper', 'grid', 'pedReports')
    document.querySelector('.searchResponseWrapper').appendChild(reportsSection)

    const allReports = [
      ...reportsResponse.citations.map((r) => ({ ...r, type: 'citation' })),
      ...reportsResponse.arrests.map((r) => ({ ...r, type: 'arrest' })),
      ...reportsResponse.incidents.map((r) => ({ ...r, type: 'incident' })),
    ].sort((a, b) => new Date(b.TimeStamp) - new Date(a.TimeStamp))

    for (const report of allReports) {
      const el = document.createElement('div')
      el.classList.add('clickable')
      el.addEventListener('click', () =>
        openIdInReport(report.Id, report.type)
      )
      const label = document.createElement('label')
      label.innerHTML =
        report.type.charAt(0).toUpperCase() + report.type.slice(1)
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
  await loadSearchHistory()
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
