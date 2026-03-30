;(async function () {
  const config = await getConfig()
  if (config.updateDomWithLanguageOnLoad) await updateDomWithLanguage('callout')
  await applyCalloutCadPlaceholders()
})()

const calloutEventWs = new WebSocket(`ws://${location.host}/ws`)
calloutEventWs.onopen = () => calloutEventWs.send('calloutEvent')

const CAD_PRESETS = [
  { value: '10-8 | Available', label: '10-8 — Available' },
  { value: '10-97 | En route', label: '10-97 — En route' },
  { value: '10-23 | On scene', label: '10-23 — On scene' },
  { value: '10-95 | Traffic stop', label: '10-95 — Traffic stop' },
  { value: '10-7 | Out of service', label: '10-7 — Out of service' },
  { value: '10-6 | Busy', label: '10-6 — Busy' },
]

function escapeHtmlAttr(s) {
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/"/g, '&quot;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}

/** Coerce plugin JSON (Pascal/camel, string/number) to LSPDFR CalloutAcceptanceState int. */
function normalizeCalloutAcceptanceState(data) {
  const v = data?.AcceptanceState ?? data?.acceptanceState
  if (v == null || v === '') return 0
  const n = typeof v === 'number' ? v : Number(v)
  return Number.isFinite(n) ? n : 0
}

function updateCadUnitReadout(text) {
  const el = document.getElementById('cadUnitStatusReadout')
  if (!el) return
  const t = text != null ? String(text).trim() : ''
  el.textContent = t.length ? t : '—'
}

async function applyCalloutCadPlaceholders() {
  const language = await getLanguage()
  const input = document.getElementById('cadUnitCustomInput')
  if (input)
    input.placeholder =
      language.callout?.static?.cad?.customPlaceholder || 'Custom status (overrides preset when filled)'
}

function wireCalloutCadPanel() {
  const btn = document.getElementById('cadUnitSetStatusBtn')
  const sel = document.getElementById('cadUnitPresetSelect')
  if (!btn || !sel || btn.dataset.wired === '1') return
  btn.dataset.wired = '1'
  sel.innerHTML = CAD_PRESETS.map(
    (p) => `<option value="${escapeHtmlAttr(p.value)}">${escapeHtmlAttr(p.label)}</option>`,
  ).join('')

  btn.addEventListener('click', async () => {
    const language = await getLanguage()
    const input = document.getElementById('cadUnitCustomInput')
    const custom = input?.value?.trim() ?? ''
    const status = custom || sel.value
    if (!status) {
      if (typeof showNotification === 'function') {
        showNotification(language.callout?.actions?.error || 'Action failed.', 'warning')
      }
      return
    }
    try {
      const res = await fetch('/post/cadUnitStatus', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status }),
      })
      const json = await res.json().catch(() => ({}))
      if (json.success && typeof showNotification === 'function') {
        showNotification(
          language.callout?.static?.cad?.statusUpdated || 'Unit status updated.',
          'checkMark',
        )
      } else if (typeof showNotification === 'function') {
        showNotification(json.error || `HTTP ${res.status}`, 'warning')
      }
    } catch (e) {
      if (typeof showNotification === 'function') showNotification(String(e.message || e), 'warning')
    }
  })
}

wireCalloutCadPanel()

function getCalloutStatusLabel(state, language) {
  const statusLabels = language.callout?.status || {}
  if (state === 0) return statusLabels.pending || 'Pending'
  if (state === 1) return statusLabels.responded || 'Responded'
  if (state === 2) return statusLabels.enRoute || 'En Route'
  if (state === 3) return statusLabels.finished || 'Finished'
  return statusLabels.unknown || '—'
}

function renderCalloutCard(data, index, language, config) {
  const state = normalizeCalloutAcceptanceState(data)
  const statusLabel = getCalloutStatusLabel(state, language)
  const address = `${(data.Location?.Postal || '').trim()} ${(data.Location?.Street || '').trim()}`.trim() || '—'
  const hasId = !!data.Id
  const ciLabel = language.callout?.actions?.sendToCi || 'Send to Callout Interface'
  const ciPh =
    language.callout?.actions?.sendToCiPlaceholder ||
    'Message for the in-game CI log (no color codes; newlines OK)'
  const ciBlock = hasId
    ? `
        <div class="calloutCiSend">
          <span class="calloutCiSendLabel">${ciLabel.replace(/</g, '&lt;')}</span>
          <textarea class="calloutCiTextarea" aria-label="${escapeHtmlAttr(ciLabel)}" placeholder="${escapeHtmlAttr(ciPh)}"></textarea>
          <div class="calloutCiActions">
            <button type="button" class="calloutActionBtn calloutSendCiBtn" data-action="sendCi">${ciLabel.replace(/</g, '&lt;')}</button>
          </div>
        </div>`
    : ''
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
        ${ciBlock}
      </div>
    </div>
  `
}

async function postCalloutAction(body, language) {
  const res = await fetch('/post/calloutAction', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  let json = {}
  try {
    json = await res.json()
  } catch {
    json = {}
  }
  if (json.success && typeof showNotification === 'function') {
    showNotification(language.callout?.actions?.success || 'Status updated.', 'checkMark')
  } else if (typeof showNotification === 'function') {
    showNotification(json.error || `Request failed (${res.status})`, 'warning')
  }
}

calloutEventWs.onmessage = async (event) => {
  const language = await getLanguage()
  const config = await getConfig()
  const payload = JSON.parse(event.data).response
  updateCadUnitReadout(payload?.cadUnitStatus)

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
    card.querySelector('.calloutMetaVal').innerHTML = metaParts.map((p) => `<div class="calloutMetaRow">${p}</div>`).join('') || ''
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
        body: JSON.stringify({ x: data.Coords[0], y: data.Coords[1] }),
      })
      if (res.ok && typeof showNotification === 'function') {
        showNotification(language.callout?.actions?.gpsSuccess || 'GPS set to callout.', 'checkMark')
      } else if (typeof showNotification === 'function') {
        const t = await res.text().catch(() => '')
        showNotification(t || `GPS failed (${res.status})`, 'warning')
      }
    })
  })

  containerEl.querySelectorAll('.calloutAcceptBtn, .calloutEnRouteBtn').forEach((btn) => {
    btn.addEventListener('click', async function () {
      const action = this.dataset.action
      const card = this.closest('.calloutCard')
      const idx = parseInt(card?.dataset?.index ?? '-1', 10)
      const data = callouts[idx]
      const calloutId = data?.Id
      if (!calloutId) {
        if (typeof showNotification === 'function') {
          showNotification('Callout id missing — update MDT Pro plugin / refresh.', 'warning')
        }
        return
      }
      await postCalloutAction({ action, calloutId }, language)
    })
  })

  containerEl.querySelectorAll('.calloutSendCiBtn').forEach((btn) => {
    btn.addEventListener('click', async function () {
      const card = this.closest('.calloutCard')
      const idx = parseInt(card?.dataset?.index ?? '-1', 10)
      const data = callouts[idx]
      const calloutId = data?.Id
      const ta = card?.querySelector('.calloutCiTextarea')
      const message = ta?.value?.trim() ?? ''
      if (!calloutId) {
        if (typeof showNotification === 'function') {
          showNotification('Callout id missing — update MDT Pro plugin / refresh.', 'warning')
        }
        return
      }
      if (!message) {
        if (typeof showNotification === 'function') {
          showNotification(language.callout?.actions?.error || 'Enter a message.', 'warning')
        }
        return
      }
      const res = await fetch('/post/calloutAction', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ action: 'sendMessage', calloutId, message }),
      })
      let json = {}
      try {
        json = await res.json()
      } catch {
        json = {}
      }
      if (json.success && typeof showNotification === 'function') {
        showNotification(language.callout?.actions?.sendToCiSuccess || 'Message sent.', 'checkMark')
        if (ta) ta.value = ''
      } else if (typeof showNotification === 'function') {
        showNotification(json.error || `Send failed (${res.status})`, 'warning')
      }
    })
  })
}
