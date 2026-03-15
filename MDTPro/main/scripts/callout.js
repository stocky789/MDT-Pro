;(async function () {
  const config = await getConfig()
  if (config.updateDomWithLanguageOnLoad) await updateDomWithLanguage('callout')
})()

const calloutEventWs = new WebSocket(`ws://${location.host}/ws`)
calloutEventWs.onopen = () => calloutEventWs.send('calloutEvent')

function getCalloutStatusLabel(state, language) {
  const statusLabels = language.callout?.status || {}
  if (state === 0) return statusLabels.pending || 'Pending'
  if (state === 1) return statusLabels.responded || 'Responded'
  if (state === 2) return statusLabels.enRoute || 'En Route'
  if (state === 3) return statusLabels.finished || 'Finished'
  return statusLabels.unknown || '—'
}

function renderCalloutCard(data, index, language, config) {
  const state = data.AcceptanceState
  const statusLabel = getCalloutStatusLabel(state, language)
  const address = `${(data.Location?.Postal || '').trim()} ${(data.Location?.Street || '').trim()}`.trim() || '—'
  return `
    <div class="calloutCard ${index === 0 ? 'calloutCard-expanded' : ''}" data-index="${index}">
      <button type="button" class="calloutCardHeader" aria-expanded="${index === 0}">
        <span class="calloutCardName">${(data.Name || '—').replace(/</g, '&lt;')}</span>
        <span class="calloutCardStatus calloutStatus${state ?? 0}">${statusLabel}</span>
        <span class="calloutCardAddress">${address.replace(/</g, '&lt;')}</span>
        <span class="calloutCardChevron" aria-hidden="true">▼</span>
      </button>
      <div class="calloutCardBody" ${index !== 0 ? 'hidden' : ''}>
        <ul class="calloutDetails">
          <li class="calloutDetailRow">
            <span class="calloutDetailLabel" data-language="address">Address</span>
            <span class="calloutDetailValue calloutAddressVal">—</span>
          </li>
          <li class="calloutDetailRow">
            <span class="calloutDetailLabel" data-language="area">Area</span>
            <span class="calloutDetailValue calloutAreaVal">—</span>
          </li>
          <li class="calloutDetailRow">
            <span class="calloutDetailLabel" data-language="county">County</span>
            <span class="calloutDetailValue calloutCountyVal">—</span>
          </li>
          <li class="calloutDetailRow">
            <span class="calloutDetailLabel" data-language="priority">Priority</span>
            <span class="calloutDetailValue calloutPriorityVal">—</span>
          </li>
          <li class="calloutDetailRow calloutDetailFull calloutMessageRow">
            <span class="calloutDetailLabel" data-language="callout.calloutInfo.message">Message</span>
            <span class="calloutDetailValue calloutMessageVal">—</span>
          </li>
          <li class="calloutDetailRow calloutDetailFull calloutAdvisoryRow">
            <span class="calloutDetailLabel" data-language="callout.calloutInfo.advisory">Advisory</span>
            <span class="calloutDetailValue calloutAdvisoryVal">—</span>
          </li>
        </ul>
        <div class="calloutMeta calloutMetaVal"></div>
        <div class="calloutActions">
          <button type="button" class="calloutActionBtn calloutSetGpsBtn" data-action="setGps">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/><circle cx="12" cy="10" r="3"/></svg>
            ${language.callout?.actions?.setGps || 'Set GPS'}
          </button>
          ${state === 0 ? `<button type="button" class="calloutActionBtn calloutAcceptBtn" data-action="accept">${language.callout?.actions?.accept || 'Accept'}</button>` : ''}
          ${state === 1 ? `<button type="button" class="calloutActionBtn calloutEnRouteBtn" data-action="enRoute">${language.callout?.status?.enRoute || 'En Route'}</button>` : ''}
        </div>
      </div>
    </div>
  `
}

calloutEventWs.onmessage = async (event) => {
  const language = await getLanguage()
  const config = await getConfig()
  const payload = JSON.parse(event.data).response
  const callouts = payload?.callouts ?? (payload?.Location ? [payload] : [])
  const emptyEl = document.getElementById('calloutEmpty')
  const containerEl = document.getElementById('calloutCardsContainer')

  if (!callouts || callouts.length === 0) {
    if (emptyEl) emptyEl.classList.remove('hidden')
    if (containerEl) {
      containerEl.classList.add('hidden')
      containerEl.innerHTML = ''
    }
    const timeline = document.getElementById('calloutTimeline')
    if (timeline) timeline.textContent = ''
    return
  }

  if (emptyEl) emptyEl.classList.add('hidden')
  if (containerEl) containerEl.classList.remove('hidden')

  const current = callouts[0]
  const timelineEl = document.getElementById('calloutTimeline')
  if (timelineEl) {
    const parts = []
    if (current.DisplayedTime) parts.push(`${language.callout?.status?.displayed || 'Displayed'}: ${new Date(current.DisplayedTime).toLocaleTimeString()}`)
    if (current.AcceptedTime) parts.push(`${language.callout?.status?.responded || 'Responded'}: ${new Date(current.AcceptedTime).toLocaleTimeString()}`)
    if (current.FinishedTime) parts.push(`${language.callout?.status?.finished || 'Finished'}: ${new Date(current.FinishedTime).toLocaleTimeString()}`)
    timelineEl.textContent = parts.join('  •  ') || '—'
  }

  containerEl.innerHTML = callouts.map((c, i) => renderCalloutCard(c, i, language, config)).join('')

  for (let i = 0; i < callouts.length; i++) {
    const data = callouts[i]
    const card = containerEl.children[i]
    const countyVal = await getLanguageValue(data.Location?.County)
    card.querySelector('.calloutAddressVal').textContent = `${(data.Location?.Postal || '').trim()} ${(data.Location?.Street || '').trim()}`.trim() || '—'
    card.querySelector('.calloutAreaVal').textContent = data.Location?.Area || '—'
    card.querySelector('.calloutCountyVal').textContent = countyVal || '—'
    card.querySelector('.calloutPriorityVal').textContent = data.Priority || language.callout?.defaultPriority || '—'

    const msgRow = card.querySelector('.calloutMessageRow')
    const advRow = card.querySelector('.calloutAdvisoryRow')
    const msgEl = card.querySelector('.calloutMessageVal')
    const advEl = card.querySelector('.calloutAdvisoryVal')
    if (data.Message) {
      msgEl.innerHTML = removeGTAColorCodesFromString(data.Message)
      msgRow?.classList.remove('hidden')
    } else {
      msgRow?.classList.add('hidden')
    }
    if (data.Advisory) {
      advEl.innerHTML = removeGTAColorCodesFromString(data.Advisory)
      advRow?.classList.remove('hidden')
    } else {
      advRow?.classList.add('hidden')
    }

    const agencyStr = config.showAgencyInCalloutInfo ? ` (${data.Agency || ''})` : ''
    const metaParts = []
    metaParts.push(`${language.callout?.calloutInfo?.displayedTime || 'Displayed'}: ${new Date(data.DisplayedTime).toLocaleString()}`)
    if (data.AcceptedTime) {
      metaParts.push(`${language.callout?.calloutInfo?.unit || 'Unit'} ${data.Callsign || ''}${agencyStr}  ${language.callout?.calloutInfo?.acceptedTime || 'Accepted'}: ${new Date(data.AcceptedTime).toLocaleString()}`)
    }
    if (data.AdditionalMessages?.length) {
      for (const m of data.AdditionalMessages) metaParts.push(removeGTAColorCodesFromString(m))
    }
    if (data.FinishedTime) {
      metaParts.push(`${language.callout?.calloutInfo?.finishedTime || 'Finished'}: ${new Date(data.FinishedTime).toLocaleString()}`)
    }
    card.querySelector('.calloutMetaVal').innerHTML = metaParts.map(p => `<div class="calloutMetaRow">${p}</div>`).join('') || ''
  }

  containerEl.querySelectorAll('.calloutCardHeader').forEach((btn) => {
    btn.addEventListener('click', function () {
      const card = this.closest('.calloutCard')
      const body = card.querySelector('.calloutCardBody')
      const isExpanded = body.hidden === false
      card.classList.toggle('calloutCard-expanded', !isExpanded)
      body.hidden = isExpanded
      btn.setAttribute('aria-expanded', !isExpanded)
    })
  })

  containerEl.querySelectorAll('.calloutSetGpsBtn').forEach((btn) => {
    btn.addEventListener('click', async function () {
      const card = this.closest('.calloutCard')
      const idx = parseInt(card.dataset.index, 10)
      const data = callouts[idx]
      if (!data?.Coords?.length) return
      const res = await fetch('/post/setGpsWaypoint', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ x: data.Coords[0], y: data.Coords[1] })
      })
      if (res.ok && typeof showNotification === 'function') {
        showNotification(language.callout?.actions?.gpsSuccess || 'GPS set to callout.', 'checkMark')
      }
    })
  })

  containerEl.querySelectorAll('.calloutAcceptBtn, .calloutEnRouteBtn').forEach((btn) => {
    btn.addEventListener('click', async function () {
      const action = this.dataset.action
      const res = await fetch('/post/calloutAction', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ action })
      })
      const json = await res.json().catch(() => ({}))
      if (json.success && typeof showNotification === 'function') {
        showNotification(language.callout?.actions?.success || 'Status updated.', 'checkMark')
      } else if (json?.error && typeof showNotification === 'function') {
        showNotification(json.error, 'warning')
      }
    })
  })
}
