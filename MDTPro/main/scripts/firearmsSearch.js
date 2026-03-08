function escapeHtml(s) {
  if (s == null || s === undefined) return ''
  const d = document.createElement('div')
  d.textContent = String(s)
  return d.innerHTML
}

;(async function () {
  const config = await getConfig()
  if (config.updateDomWithLanguageOnLoad)
    await updateDomWithLanguage('firearmsSearch')

  await loadRecentOwners()
})()

const searchInput = document.querySelector('.searchInputWrapper #firearmsSearchInput')
const searchButton = document.querySelector('.searchInputWrapper button')

if (searchInput) {
  searchInput.addEventListener('keydown', async function (e) {
    if (e.key == 'Enter') {
      e.preventDefault()
      searchButton?.click()
    }
  })
}

if (searchButton) {
  searchButton.addEventListener('click', async function () {
    if (this.classList.contains('loading')) return
    showLoadingOnButton(this)
    this.blur()
    await performSearch(searchInput?.value?.trim() ?? '')
    hideLoadingOnButton(this)
  })
}

async function loadRecentOwners() {
  let owners = []
  try {
    const resp = await fetch('/data/recentFirearmOwners')
    if (resp.ok) owners = await resp.json()
  } catch {
    owners = []
  }
  const wrapper = document.querySelector('.recentOwnersWrapper')
  const list = document.querySelector('.recentOwnersList')
  if (!list) return
  list.innerHTML = ''

  if (!Array.isArray(owners) || owners.length === 0) {
    if (wrapper) wrapper.classList.add('hidden')
    return
  }

  if (wrapper) wrapper.classList.remove('hidden')
  for (const name of owners) {
    const item = document.createElement('button')
    item.textContent = name
    item.addEventListener('click', function () {
      if (searchInput) searchInput.value = name
      document.querySelector('.searchInputWrapper button')?.click()
    })
    list.appendChild(item)
  }
}

async function performSearch(query) {
  const language = await getLanguage()
  if (!query) {
    topWindow.showNotification(
      language.firearmsSearch?.notifications?.emptySearchInput ||
        'Enter serial number or owner name.',
      'warning'
    )
    return
  }

  document.querySelector('.searchResponseWrapper').classList.add('hidden')
  const resultEl = document.querySelector('.firearmsResult')
  resultEl.innerHTML = ''

  // Try serial first, then owner name
  let bySerial
  try {
    const resp = await fetch('/data/firearmBySerial', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(query),
    })
    bySerial = resp.ok ? await resp.json() : null
  } catch (err) {
    bySerial = null
    topWindow.showNotification(
      language.firearmsSearch?.notifications?.searchError || 'Search failed. Please try again.',
      'warning'
    )
    return
  }

  if (bySerial && (bySerial.Id > 0 || (bySerial.WeaponModelHash && bySerial.OwnerPedName))) {
    document.querySelector('.searchResponseWrapper').classList.remove('hidden')
    document.title = `Firearms Check: ${bySerial.SerialNumber || query}`
    renderFirearmCard(resultEl, bySerial)
    return
  }

  let byOwner
  try {
    const resp = await fetch('/data/firearmsForPed', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(query),
    })
    byOwner = resp.ok ? await resp.json() : null
  } catch (err) {
    byOwner = null
    topWindow.showNotification(
      language.firearmsSearch?.notifications?.searchError || 'Search failed. Please try again.',
      'warning'
    )
    return
  }

  if (byOwner && Array.isArray(byOwner) && byOwner.length > 0) {
    document.querySelector('.searchResponseWrapper').classList.remove('hidden')
    document.title = `Firearms Check: ${query}`
    const title = document.createElement('div')
    title.className = 'firearmsOwnerTitle'
    title.textContent = `Firearms for ${query}`
    resultEl.appendChild(title)
    for (const f of byOwner) {
      renderFirearmCard(resultEl, f)
    }
  } else {
    topWindow.showNotification(
      language.firearmsSearch?.notifications?.notFound ||
        'No firearm or owner found.',
      'warning'
    )
    document.title = 'Firearms Check'
  }
}

function formatDateSafe(isoString) {
  if (isoString == null || isoString === '') return '—'
  const d = new Date(isoString)
  return isNaN(d.getTime()) ? '—' : d.toLocaleString()
}

function sanitizeModelIdForUrl(modelId) {
  if (modelId == null || typeof modelId !== 'string') return ''
  return modelId.trim().toUpperCase().replace(/[^A-Z0-9_-]/g, '') || ''
}

function renderFirearmCard(container, f) {
  const card = document.createElement('div')
  card.className = 'firearmCard'
  const name = f.WeaponDisplayName || f.Description || f.WeaponModelId || `Weapon (${f.WeaponModelHash || 0})`
  const serial = f.IsSerialScratched ? 'Scratched' : (f.SerialNumber || 'N/A')
  const stolen = f.IsStolen ? ' [STOLEN]' : ''
  const ownerVal = f.OwnerPedName || ''
  card.innerHTML = `
    <div class="firearmRow firearmRowWithImage">
      <div class="firearmImageWrapper">
        <img class="firearmImage" src="" alt="" data-model-id="">
      </div>
      <div class="firearmDetails">
        <div class="firearmRow">
          <label>Serial</label>
          <span>${escapeHtml(serial)}</span>
        </div>
        <div class="firearmRow">
          <label>Model</label>
          <span class="${f.IsStolen ? 'stolen' : ''}">${escapeHtml(name)}${escapeHtml(stolen)}</span>
        </div>
        <div class="firearmRow">
          <label>Owner</label>
          <span class="clickable" data-owner="${escapeHtml(ownerVal)}">${escapeHtml(ownerVal || '—')}</span>
        </div>
        <div class="firearmRow">
          <label>Source</label>
          <span>${escapeHtml(f.Source || '—')}</span>
        </div>
        <div class="firearmRow">
          <label>Last seen</label>
          <span>${escapeHtml(formatDateSafe(f.LastSeenAt))}</span>
        </div>
      </div>
    </div>
  `
  const ownerSpan = card.querySelector('[data-owner]')
  if (ownerSpan && ownerSpan.dataset.owner) {
    ownerSpan.addEventListener('click', () =>
      openFirearmsSearch(ownerSpan.dataset.owner)
    )
  }
  const img = card.querySelector('.firearmImage')
  const modelId = sanitizeModelIdForUrl(f.WeaponModelId)
  if (img && modelId) {
    img.dataset.modelId = modelId
    img.alt = name
    img.src = `https://docs-backend.fivem.net/weapons/${modelId}.png`
    img.onerror = () => { img.style.display = 'none' }
  } else if (img) {
    img.style.display = 'none'
  }
  container.appendChild(card)
}
