;(async function () {
  const config = await getConfig()
  if (config.updateDomWithLanguageOnLoad)
    await updateDomWithLanguage('firearmsSearch')

  await loadRecentOwners()
})()

const searchInput = document.querySelector('.searchInputWrapper #firearmsSearchInput')

searchInput.addEventListener('keydown', async function (e) {
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
    await performSearch(searchInput.value.trim())
    hideLoadingOnButton(this)
  })

async function loadRecentOwners() {
  const owners = await (await fetch('/data/recentFirearmOwners')).json()
  const wrapper = document.querySelector('.recentOwnersWrapper')
  const list = document.querySelector('.recentOwnersList')
  list.innerHTML = ''

  if (!owners || owners.length === 0) {
    wrapper.classList.add('hidden')
    return
  }

  wrapper.classList.remove('hidden')
  for (const name of owners) {
    const item = document.createElement('button')
    item.textContent = name
    item.addEventListener('click', function () {
      searchInput.value = name
      document.querySelector('.searchInputWrapper button').click()
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
  const bySerial = await (
    await fetch('/data/firearmBySerial', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: query,
    })
  ).json()

  if (bySerial && bySerial.Id) {
    document.querySelector('.searchResponseWrapper').classList.remove('hidden')
    document.title = `Firearms Check: ${bySerial.SerialNumber || query}`
    renderFirearmCard(resultEl, bySerial)
    return
  }

  const byOwner = await (
    await fetch('/data/firearmsForPed', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: query,
    })
  ).json()

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

function renderFirearmCard(container, f) {
  const card = document.createElement('div')
  card.className = 'firearmCard'
  // Prefer API-provided WeaponDisplayName (from game native), fallback to PR Description, then WeaponModelId
  const name = f.WeaponDisplayName || f.Description || f.WeaponModelId || `Weapon (${f.WeaponModelHash})`
  const serial = f.SerialNumber || 'N/A'
  const stolen = f.IsStolen ? ' [STOLEN]' : ''
  card.innerHTML = `
    <div class="firearmRow firearmRowWithImage">
      <div class="firearmImageWrapper">
        <img class="firearmImage" src="" alt="" data-model-id="${(f.WeaponModelId || '').replace(/"/g, '&quot;')}">
      </div>
      <div class="firearmDetails">
        <div class="firearmRow">
          <label>Serial</label>
          <span>${serial}</span>
        </div>
        <div class="firearmRow">
          <label>Model</label>
          <span class="${f.IsStolen ? 'stolen' : ''}">${name}${stolen}</span>
        </div>
        <div class="firearmRow">
          <label>Owner</label>
          <span class="clickable" data-owner="${f.OwnerPedName || ''}">${f.OwnerPedName || '—'}</span>
        </div>
        <div class="firearmRow">
          <label>Source</label>
          <span>${f.Source || '—'}</span>
        </div>
        <div class="firearmRow">
          <label>Last seen</label>
          <span>${f.LastSeenAt ? new Date(f.LastSeenAt).toLocaleString() : '—'}</span>
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
  // Try FiveM weapon image URL (same pattern as vehicles); 404 → remove image
  const img = card.querySelector('.firearmImage')
  const modelId = (f.WeaponModelId || '').trim().toLowerCase()
  if (img && modelId) {
    img.src = `https://docs.fivem.net/weapons/${modelId}.webp`
    img.onerror = () => { img.parentElement?.remove() }
  } else if (img) {
    img.parentElement?.remove()
  }
  container.appendChild(card)
}
