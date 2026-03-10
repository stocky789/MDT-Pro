;(async function () {
  const config = await getConfig()
  if (config.updateDomWithLanguageOnLoad) await updateDomWithLanguage('callout')
})()

const calloutEventWs = new WebSocket(`ws://${location.host}/ws`)
calloutEventWs.onopen = () => calloutEventWs.send('calloutEvent')

function getCalloutStatusLabel(state, language) {
  const statusLabels = language.callout?.status || {}
  if (state === 0) return statusLabels.pending || 'Pending'
  if (state === 1) return statusLabels.accepted || 'Accepted'
  if (state === 2) return statusLabels.enRoute || 'En Route'
  if (state === 3) return statusLabels.finished || 'Finished'
  return statusLabels.unknown || '—'
}

calloutEventWs.onmessage = async (event) => {
  const language = await getLanguage()
  const config = await getConfig()
  const data = JSON.parse(event.data).response
  const emptyEl = document.getElementById('calloutEmpty')
  const cardEl = document.getElementById('calloutCard')

  if (!data || !data.Location) {
    if (emptyEl) emptyEl.classList.remove('hidden')
    if (cardEl) cardEl.classList.add('hidden')
    const badge = document.getElementById('calloutStatusBadge')
    const timeline = document.getElementById('calloutTimeline')
    if (badge) badge.textContent = '—'
    if (timeline) timeline.textContent = ''
    return
  }

  if (emptyEl) emptyEl.classList.add('hidden')
  if (cardEl) cardEl.classList.remove('hidden')

  // Status bar
  const statusBadge = document.getElementById('calloutStatusBadge')
  const timelineEl = document.getElementById('calloutTimeline')
  const state = data.AcceptanceState
  const statusLabel = getCalloutStatusLabel(state, language)
  if (statusBadge) {
    statusBadge.textContent = statusLabel
    statusBadge.className = 'calloutStatusBadge calloutStatus' + (state ?? 0)
  }
  if (timelineEl) {
    const parts = []
    if (data.DisplayedTime) parts.push(`${language.callout?.status?.displayed || 'Displayed'}: ${new Date(data.DisplayedTime).toLocaleTimeString()}`)
    if (data.AcceptedTime) parts.push(`${language.callout?.status?.accepted || 'Accepted'}: ${new Date(data.AcceptedTime).toLocaleTimeString()}`)
    if (data.FinishedTime) parts.push(`${language.callout?.status?.finished || 'Finished'}: ${new Date(data.FinishedTime).toLocaleTimeString()}`)
    timelineEl.textContent = parts.join('  •  ') || '—'
  }

  // List details
  const countyVal = await getLanguageValue(data.Location.County)
  document.getElementById('calloutAddress').textContent = `${data.Location.Postal || ''} ${data.Location.Street || ''}`.trim() || '—'
  document.getElementById('calloutArea').textContent = data.Location.Area || '—'
  document.getElementById('calloutCounty').textContent = countyVal || '—'
  document.getElementById('calloutPriority').textContent = data.Priority || language.callout?.defaultPriority || '—'

  const msgRow = document.getElementById('calloutMessageRow')
  const advRow = document.getElementById('calloutAdvisoryRow')
  const msgEl = document.getElementById('calloutMessage')
  const advEl = document.getElementById('calloutAdvisory')
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

  // Meta (displayed, accepted, finished times)
  const agencyStr = config.showAgencyInCalloutInfo ? ` (${data.Agency || ''})` : ''
  const metaParts = []
  metaParts.push(`${language.callout?.calloutInfo?.displayedTime || 'Displayed'}: ${new Date(data.DisplayedTime).toLocaleString()}`)
  if (data.AcceptedTime) {
    metaParts.push(`${language.callout?.calloutInfo?.unit || 'Unit'} ${data.Callsign || ''}${agencyStr}  ${language.callout?.calloutInfo?.acceptedTime || 'Accepted'}: ${new Date(data.AcceptedTime).toLocaleString()}`)
  }
  if (data.AdditionalMessages && data.AdditionalMessages.length > 0) {
    for (const m of data.AdditionalMessages) {
      metaParts.push(removeGTAColorCodesFromString(m))
    }
  }
  if (data.FinishedTime) {
    metaParts.push(`${language.callout?.calloutInfo?.finishedTime || 'Finished'}: ${new Date(data.FinishedTime).toLocaleString()}`)
  }
  const metaEl = document.getElementById('calloutMeta')
  metaEl.innerHTML = metaParts.map(p => `<div class="calloutMetaRow">${p}</div>`).join('') || ''
}
