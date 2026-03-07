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
  }

  await loadNearbyVehicles()
  await loadSearchHistory()
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

async function performSearch(query) {
  const language = await getLanguage()
  if (!query) {
    topWindow.showNotification(
      language.vehicleSearch.notifications.emptySearchInput,
      'warning'
    )
    return
  }
  const response = await (
    await fetch('/data/specificVehicle', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: query,
    })
  ).json()

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
      case 'InsuranceExpiration':
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
      case 'VinStatus':
        el.value = await getLanguageValue(response[key])
        if (response[key] === 'Scratched') el.style.color = 'var(--color-warning)'
        break
      case 'Color':
        if (!response[key]) {
          el.parentElement.classList.add('hidden')
          break
        }
        el.parentElement.classList.remove('hidden')
        const color = `rgb(${response[key].split('-')[0]}, ${
          response[key].split('-')[1]
        }, ${response[key].split('-')[2]})`
        el.style.backgroundColor = color
        el.style.height = '19px'
        break
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

  // Vehicle search records (contraband from PR vehicle search)
  const searchRecordsResponse = await (
    await fetch('/data/vehicleSearchByPlate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(response.LicensePlate ?? ''),
    })
  ).json()

  document
    .querySelectorAll(
      '.searchResponseWrapper .vehicleSearchRecordsSection, .searchResponseWrapper .vehicleSearchRecordsTitle'
    )
    .forEach((el) => el?.remove())

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

  if (!canModifyBOLOs && bolos.length === 0) {
    const hint = document.createElement('div')
    hint.classList.add('boloHint')
    hint.textContent = language.vehicleSearch?.static?.boloVehicleRequired || 'Vehicle must be nearby to add or remove BOLOs.'
    boloSection.appendChild(hint)
  } else {
    if (!canModifyBOLOs && bolos.length > 0) {
      const hint = document.createElement('div')
      hint.classList.add('boloHint')
      hint.textContent = language.vehicleSearch?.static?.boloVehicleRequired || 'Vehicle must be nearby to add or remove BOLOs.'
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
    if (canModifyBOLOs) {
      const addBtn = document.createElement('button')
      addBtn.type = 'button'
      addBtn.classList.add('boloAddBtn')
      addBtn.textContent = language.vehicleSearch?.static?.addBOLO || 'Add BOLO'
      addBtn.addEventListener('click', () => showAddBOLOModal(response, language, performSearch))
      boloSection.appendChild(addBtn)
    }
  }
  if (boloPlaceholder) {
    boloPlaceholder.appendChild(boloSection)
  }

  if (searchRecordsResponse && searchRecordsResponse.length > 0) {
    const sectionTitle = document.createElement('div')
    sectionTitle.classList.add('searchResponseSectionTitle', 'vehicleSearchRecordsTitle')
    sectionTitle.innerHTML = language.vehicleSearch?.static?.searchResultsTitle || 'Search Results (Contraband)'
    document.querySelector('.searchResponseWrapper').appendChild(sectionTitle)

    const recordsSection = document.createElement('div')
    recordsSection.classList.add('inputWrapper', 'grid', 'vehicleSearchRecordsSection')
    for (const r of searchRecordsResponse) {
      const el = document.createElement('div')
      const label = document.createElement('label')
      label.textContent = r.ItemType || 'Item'
      if (r.ItemLocation) label.textContent += ` (${r.ItemLocation})`
      const input = document.createElement('input')
      input.type = 'text'
      input.disabled = true
      input.value = r.Description || r.DrugType || '-'
      el.appendChild(label)
      el.appendChild(input)
      recordsSection.appendChild(el)
    }
    document.querySelector('.searchResponseWrapper').appendChild(recordsSection)
  }

  // Reload search history after successful search
  await loadNearbyVehicles()
  await loadSearchHistory()
}

async function showAddBOLOModal(vehicleResponse, language, onSuccess) {
  const reason = prompt(language.vehicleSearch?.static?.boloReasonPrompt || 'Enter BOLO reason:')
  if (reason == null || !reason.trim()) return
  const expiresDays = prompt(language.vehicleSearch?.static?.boloExpiresPrompt || 'Expires in how many days? (default 7):', '7')
  let days = 7
  if (expiresDays != null && expiresDays.trim()) {
    const n = parseInt(expiresDays, 10)
    if (!isNaN(n) && n > 0) days = n
  }
  const expires = new Date()
  expires.setDate(expires.getDate() + days)
  const res = await (await fetch('/post/addBOLO', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      LicensePlate: vehicleResponse.LicensePlate,
      Reason: reason.trim(),
      ExpiresAt: expires.toISOString(),
      IssuedBy: 'LSPD'
    })
  })).json()
  if (res && res.success) {
    topWindow.showNotification(language.vehicleSearch?.notifications?.boloAdded || 'BOLO added.', 'checkMark')
    if (typeof onSuccess === 'function') await onSuccess(vehicleResponse.LicensePlate)
  } else {
    topWindow.showNotification(res?.error || 'Failed to add BOLO.', 'warning')
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
