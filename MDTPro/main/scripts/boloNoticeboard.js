;(async function () {
  const config = await getConfig()
  if (config.updateDomWithLanguageOnLoad) await updateDomWithLanguage('boloNoticeboard')

  const language = await getLanguage()
  const lang = language.boloNoticeboard || {}

  const refreshBtn = document.querySelector('.boloRefreshBtn')
  const emptyEl = document.querySelector('.boloEmpty')
  const cardList = document.querySelector('.boloCardList')
  if (!refreshBtn || !emptyEl || !cardList) return

  function escapeHtml (text) {
    if (text == null) return ''
    const div = document.createElement('div')
    div.textContent = text
    return div.innerHTML
  }

  async function loadBolos () {
    let list
    try {
      const res = await fetch('/data/activeBolos', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' })
      if (!res.ok) throw new Error(res.statusText)
      list = await res.json()
    } catch (_) {
      emptyEl?.classList.remove('hidden')
      if (cardList) cardList.innerHTML = ''
      return
    }
    if (!Array.isArray(list) || list.length === 0) {
      emptyEl?.classList.remove('hidden')
      if (cardList) cardList.innerHTML = ''
      return
    }
    emptyEl?.classList.add('hidden')
    if (cardList) cardList.innerHTML = ''
    for (const entry of list) {
      const bolos = Array.isArray(entry.BOLOs) ? entry.BOLOs : []
      const card = document.createElement('div')
      card.classList.add('boloCard')
      if (entry.IsStolen) card.classList.add('stolen')

      const header = document.createElement('div')
      header.classList.add('boloCardHeader')
      const plateSpan = document.createElement('span')
      plateSpan.classList.add('boloCardPlate')
      plateSpan.textContent = entry.LicensePlate || '—'
      header.appendChild(plateSpan)
      if (entry.ModelDisplayName) {
        const modelSpan = document.createElement('span')
        modelSpan.classList.add('boloCardModel')
        modelSpan.textContent = entry.ModelDisplayName
        header.appendChild(modelSpan)
      }
      if (entry.IsStolen) {
        const stolenBadge = document.createElement('span')
        stolenBadge.classList.add('boloCardStolenBadge')
        stolenBadge.textContent = lang.stolenBadge || 'STOLEN'
        header.appendChild(stolenBadge)
      }
      const actions = document.createElement('div')
      actions.classList.add('boloCardActions')
      const viewBtn = document.createElement('button')
      viewBtn.type = 'button'
      viewBtn.textContent = lang.viewInVehicleSearch || 'View in Vehicle Search'
      viewBtn.addEventListener('click', () => {
        const plate = entry.LicensePlate != null ? String(entry.LicensePlate).trim() : ''
        if (typeof sessionStorage !== 'undefined' && plate !== '') {
          sessionStorage.setItem('alprVehicleSearchPlate', plate)
        }
        const parent = window.opener || (window.parent !== window ? window.parent : null)
        if (parent && typeof parent.openWindow === 'function') {
          parent.openWindow('vehicleSearch')
        } else {
          window.open('/page/vehicleSearch.html', '_blank')
        }
      })
      actions.appendChild(viewBtn)
      header.appendChild(actions)
      card.appendChild(header)

      const entryList = document.createElement('ul')
      entryList.classList.add('boloEntryList')
      for (const b of bolos) {
        const reason = b.Reason || 'Unknown'
        const issuedBy = b.IssuedBy || ''
        const exp = b.ExpirationDate || b.ExpiresAt || b.Expires
        const expStr = exp ? new Date(exp).toLocaleDateString() : '—'
        const li = document.createElement('li')
        li.classList.add('boloEntry')
        li.innerHTML = `<span class="boloEntryReason">${escapeHtml(reason)}</span><div class="boloEntryMeta">${issuedBy ? escapeHtml(issuedBy) + ' · ' : ''}${escapeHtml(lang.expires || 'Expires')} ${escapeHtml(expStr)}</div>`
        entryList.appendChild(li)
      }
      card.appendChild(entryList)
      if (cardList) cardList.appendChild(card)
    }
  }

  refreshBtn.addEventListener('click', async () => {
    refreshBtn.disabled = true
    await loadBolos()
    refreshBtn.disabled = false
  })

  await loadBolos()
})()
