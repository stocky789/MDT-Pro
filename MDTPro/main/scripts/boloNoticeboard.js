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
          window.open('/page/vehicleSearch', '_blank')
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

  // Create BOLO modal
  const createBtn = document.querySelector('.boloCreateBtn')
  const modal = document.getElementById('createBoloModal')
  const form = document.getElementById('createBoloForm')
  const plateInput = document.getElementById('createBoloPlate')
  const cancelBtn = document.querySelector('.boloModalCancel')

  function openCreateBoloModal () {
    if (modal) modal.classList.remove('hidden')
    if (plateInput) plateInput.focus()
    if (form) form.reset()
    if (document.getElementById('createBoloExpires')) {
      document.getElementById('createBoloExpires').value = '7'
    }
  }

  function closeCreateBoloModal () {
    if (modal) modal.classList.add('hidden')
  }

  if (createBtn) createBtn.addEventListener('click', openCreateBoloModal)
  if (cancelBtn) cancelBtn.addEventListener('click', closeCreateBoloModal)
  if (modal) {
    modal.addEventListener('click', (e) => {
      if (e.target === modal) closeCreateBoloModal()
    })
  }

  if (form) {
    form.addEventListener('submit', async (e) => {
      e.preventDefault()
      const plate = plateInput?.value?.trim()
      const model = document.getElementById('createBoloModel')?.value?.trim()
      const reason = document.getElementById('createBoloReason')?.value?.trim()
      const expiresDays = parseInt(document.getElementById('createBoloExpires')?.value || '7', 10)
      if (!plate || !reason) return
      const expires = new Date()
      expires.setDate(expires.getDate() + (isNaN(expiresDays) || expiresDays < 1 ? 7 : expiresDays))
      const submitBtn = form.querySelector('.boloModalSubmit')
      const topWindow = window.opener || (window.parent !== window ? window.parent : null)
      if (submitBtn) submitBtn.disabled = true
      try {
        const res = await (await fetch('/post/addBOLO', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            LicensePlate: plate,
            Reason: reason,
            ExpiresAt: expires.toISOString(),
            IssuedBy: 'LSPD',
            ModelDisplayName: model || undefined
          })
        })).json()
        if (res && res.success) {
          if (topWindow && typeof topWindow.showNotification === 'function') {
            topWindow.showNotification(lang.boloCreated || 'BOLO created.', 'checkMark')
          }
          closeCreateBoloModal()
          await loadBolos()
        } else {
          if (topWindow && typeof topWindow.showNotification === 'function') {
            topWindow.showNotification(res?.error || 'Failed to create BOLO.', 'warning')
          }
        }
      } catch (_) {
        if (topWindow && typeof topWindow.showNotification === 'function') {
          topWindow.showNotification('Failed to create BOLO.', 'warning')
        }
      }
      if (submitBtn) submitBtn.disabled = false
    })
  }

  await loadBolos()
})()
