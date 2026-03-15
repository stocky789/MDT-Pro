/**
 * ALPR plugin – subscribes to ALPR hits from the in-game scanner and shows popups.
 * Requires ALPR to be enabled in-game (config.alprEnabled).
 */
;(function () {
  function escapeHtml(s) {
    if (s == null || typeof s !== 'string') return ''
    const div = document.createElement('div')
    div.textContent = s
    return div.innerHTML
  }

  async function showAlprPopup(hit, autoCloseSec) {
    const lang = (typeof getLanguage === 'function') ? await getLanguage() : {}
    const alpr = (lang && lang.alpr) || {}
    const plateRaw = String(hit && hit.plate != null ? hit.plate : '').trim()
    const plate = escapeHtml(plateRaw)
    const owner = escapeHtml(String(hit && hit.owner != null ? hit.owner : ''))
    const model = escapeHtml(String(hit && hit.modelDisplayName != null ? hit.modelDisplayName : ''))
    const flags = Array.isArray(hit && hit.flags) ? hit.flags : []
    const flagsHtml = flags.map((f) => `<span class="alpr-flag">${escapeHtml(String(f))}</span>`).join('')
    const duration = autoCloseSec > 0 ? autoCloseSec * 1000 : -1

    const showDismiss = duration < 0
    const wrapper = document.createElement('div')
    wrapper.className = 'alpr-popup'
    wrapper.setAttribute('role', 'alert')
    wrapper.innerHTML = `
      <div class="alpr-popup-header">
        <span class="alpr-popup-icon" aria-hidden="true">⚠</span>
        <span class="alpr-popup-title">${alpr.alertTitle || 'ALPR Alert'}</span>
        <span class="alpr-popup-plate">${plate || '–'}</span>
        ${showDismiss ? '<button type="button" class="alpr-popup-dismiss" aria-label="Dismiss">×</button>' : ''}
      </div>
      <div class="alpr-popup-body">
        <div class="alpr-popup-row"><span class="alpr-popup-label">${alpr.owner || 'Owner'}</span><span class="alpr-popup-value">${owner || '–'}</span></div>
        <div class="alpr-popup-row"><span class="alpr-popup-label">${alpr.model || 'Model'}</span><span class="alpr-popup-value">${model || '–'}</span></div>
        ${flags.length ? `<div class="alpr-popup-flags">${flagsHtml}</div>` : ''}
        <button type="button" class="alpr-popup-btn">${alpr.openVehicleLookup || 'Open Vehicle Lookup'}</button>
      </div>
      ${duration > 0 ? '<div class="alpr-popup-timer"></div>' : ''}
    `

    const btn = wrapper.querySelector('.alpr-popup-btn')
    if (btn) {
      if (!plateRaw) btn.disabled = true
      btn.addEventListener('click', function () {
        if (!plateRaw) return
        if (typeof sessionStorage !== 'undefined') {
          sessionStorage.setItem('alprVehicleSearchPlate', plateRaw)
        }
        if (typeof openWindow === 'function') {
          openWindow('vehicleSearch')
        }
        wrapper.remove()
      })
    }
    const dismiss = wrapper.querySelector('.alpr-popup-dismiss')
    if (dismiss) {
      dismiss.addEventListener('click', function () { wrapper.remove() })
    }

    const container = document.querySelector('.overlay .notifications') || document.body
    const createdAt = Date.now()
    wrapper.dataset.alprCreated = String(createdAt)
    container.appendChild(wrapper)

    // Keep only the most recent 8 ALPR popups; older ones are removed by the 2-minute auto-dismiss
    const popups = container.querySelectorAll('.alpr-popup')
    const maxAlprPopups = 8
    if (popups.length > maxAlprPopups) {
      for (let i = 0; i < popups.length - maxAlprPopups; i++) {
        popups[i].remove()
      }
    }

    if (duration > 0) {
      const timer = wrapper.querySelector('.alpr-popup-timer')
      if (timer) {
        timer.style.transition = `width ${duration}ms linear`
        requestAnimationFrame(() => { timer.style.width = '100%' })
      }
      setTimeout(() => {
        if (wrapper.parentNode) wrapper.remove()
      }, duration)
    }

    // Auto-dismiss after 2 minutes with a slow fade so the list doesn't fill the screen and new alerts stay visible
    const autoDismissMs = 2 * 60 * 1000
    const fadeOutMs = 1500
    setTimeout(() => {
      if (!wrapper.parentNode) return
      wrapper.classList.add('alpr-popup--fade-out')
      setTimeout(() => {
        if (wrapper.parentNode) wrapper.remove()
      }, fadeOutMs)
    }, autoDismissMs)
  }

  async function checkInGameAlprStatus() {
    try {
      const res = await fetch('/config')
      const config = await res.json()
      if (config && config.alprEnabled === false) {
        const lang = (typeof getLanguage === 'function') ? await getLanguage() : {}
        const msg = (lang && lang.alpr && lang.alpr.inGameNotEnabled) || 'In-game ALPR is not enabled.'
        if (typeof showNotification === 'function') {
          showNotification(msg, 'warning', -1)
        }
      }
    } catch (_) { /* config fetch failed, skip heads-up */ }
  }

  function init() {
    const container = document.querySelector('.overlay .notifications')
    if (!container) return
    if (typeof getConfig !== 'function') return

    checkInGameAlprStatus()

    // Interval-based cleanup: remove ALPR popups older than 2 minutes.
    // Per-popup setTimeout can be throttled when the tab is in the background, so this ensures cleanup runs when the timer fires (even if delayed).
    const autoDismissMs = 2 * 60 * 1000
    const fadeOutMs = 1500
    setInterval(function () {
      const popups = document.querySelectorAll('.alpr-popup:not(.alpr-popup--fade-out)')
      const now = Date.now()
      popups.forEach(function (popup) {
        const created = parseInt(popup.dataset.alprCreated, 10)
        if (!isNaN(created) && now - created >= autoDismissMs) {
          popup.classList.add('alpr-popup--fade-out')
          setTimeout(function () {
            if (popup.parentNode) popup.remove()
          }, fadeOutMs)
        }
      })
    }, 15000)

    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:'
    const ws = new WebSocket(`${protocol}//${location.host}/ws`)
    ws.onopen = function () { ws.send('alprSubscribe') }
    ws.onmessage = async function (event) {
      let data
      try {
        data = JSON.parse(event.data)
      } catch {
        return
      }
      if (!data || data.request !== 'alprSubscribe' || !data.response) return
      const config = await getConfig()
      const duration = config && typeof config.alprPopupDuration === 'number' ? config.alprPopupDuration : 0
      await showAlprPopup(data.response, duration)
    }
    ws.onclose = function () {}
    ws.onerror = function () {}
  }

  if (document.readyState === 'complete' || document.readyState === 'interactive') {
    setTimeout(init, 100)
  } else {
    window.addEventListener('load', function () { setTimeout(init, 100) })
  }
})()
