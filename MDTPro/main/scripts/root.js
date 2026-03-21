const isInIframe = window.self !== window.top
const topWindow = isInIframe ? window.top : window
const topDoc = isInIframe ? window.top.document : document

if (!isInIframe) {
  localStorage.removeItem('config')
  localStorage.removeItem('language')
  localStorage.removeItem('citationOptions')
  localStorage.removeItem('arrestOptions')
  localStorage.removeItem('seizureOptions')
}

function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms))
}

async function getConfig() {
  const lsConfig = localStorage.getItem('config')
  if (lsConfig) {
    return JSON.parse(lsConfig)
  }
  const config = await (await fetch('/config')).json()
  localStorage.setItem('config', JSON.stringify(config))
  return config
}

async function getLanguage() {
  const lsLanguage = localStorage.getItem('language')
  if (lsLanguage) {
    return JSON.parse(lsLanguage)
  }
  const language = await (await fetch('/language')).json()
  localStorage.setItem('language', JSON.stringify(language))
  return language
}

async function getCitationOptions() {
  const lsCitationOptions = localStorage.getItem('citationOptions')
  if (lsCitationOptions) {
    return JSON.parse(lsCitationOptions)
  }
  const citationOptions = await (await fetch('/citationOptions')).json()
  localStorage.setItem('citationOptions', JSON.stringify(citationOptions))
  return citationOptions
}

async function getArrestOptions() {
  const lsArrestOptions = localStorage.getItem('arrestOptions')
  if (lsArrestOptions) {
    return JSON.parse(lsArrestOptions)
  }
  const arrestOptions = await (await fetch('/arrestOptions')).json()
  localStorage.setItem('arrestOptions', JSON.stringify(arrestOptions))
  return arrestOptions
}

async function getSeizureOptions() {
  const lsSeizureOptions = localStorage.getItem('seizureOptions')
  if (lsSeizureOptions) {
    return JSON.parse(lsSeizureOptions)
  }
  const seizureOptions = await (await fetch('/seizureOptions')).json()
  localStorage.setItem('seizureOptions', JSON.stringify(seizureOptions))
  return seizureOptions
}

function traverseObject(obj, callback, path = []) {
  for (const key in obj) {
    if (obj.hasOwnProperty(key)) {
      if (typeof obj[key] === 'object' && obj[key] !== null) {
        traverseObject(obj[key], callback, [...path, key])
      } else {
        callback(key, obj[key], path)
      }
    }
  }
}

const keepLoadingOnButton = new Object()

async function showLoadingOnButton(button) {
  keepLoadingOnButton[button] = true
  await sleep(50)
  if (keepLoadingOnButton[button]) button.classList.add('loading')
}

function hideLoadingOnButton(button) {
  button.classList.remove('loading')
  delete keepLoadingOnButton[button]
}

/**
 * Displays a notification message on the screen with customizable icon, color, and duration.
 * If a notification with the same message already exists, it replaces the old one.
 * If duration is negative, the notification will persist until manually closed.
 * The function should only be called from the top window context, to ensure the notification gets deleted even if the current context no longer exists.
 *
 * @param {string} message - The notification message to display.
 * @param {'warning'|'info'|'error'|'question'|'checkMark'|'minus'} [icon='info'] - The icon type to display with the notification.
 * @param {number} [duration=4000] - Duration in milliseconds before the notification disappears. If negative, notification stays until closed.
 */
function showNotification(message, icon = 'info', duration = 4000) {
  const color =
    {
      warning: 'warning',
      info: 'info',
      error: 'error',
      question: 'info',
      checkMark: 'success',
      minus: 'error',
    }[icon] || 'info'

  const wrapperEl = document.createElement('div')
  wrapperEl.classList.add('notification')
  wrapperEl.style.backgroundColor = `var(--color-${color}-half)`
  wrapperEl.style.border = `1px solid var(--color-${color})`

  const iconTitleWrapperEl = document.createElement('div')
  iconTitleWrapperEl.classList.add('iconTitleWrapper')

  const iconEl = document.createElement('div')
  iconEl.classList.add('icon')
  iconEl.innerHTML =
    topDoc.querySelector(`.iconAccess .notificationIcons .${icon}`)
      ?.innerHTML ??
    topDoc.querySelector(`.iconAccess .notificationIcons .info`).innerHTML

  const titleEl = document.createElement('div')
  titleEl.classList.add('title')
  titleEl.innerHTML = message

  const timerBarEl = document.createElement('div')
  timerBarEl.classList.add('timerBar')
  timerBarEl.style.transition = `width ${duration}ms linear`
  timerBarEl.style.backgroundColor = `var(--color-${color})`

  iconTitleWrapperEl.appendChild(iconEl)
  iconTitleWrapperEl.appendChild(titleEl)
  wrapperEl.appendChild(iconTitleWrapperEl)
  wrapperEl.appendChild(timerBarEl)
  let replacesOldNotification = false
  for (const notification of topDoc.querySelectorAll(
    '.overlay .notifications .notification'
  )) {
    if (notification.querySelector('.title').innerHTML == message) {
      notification.replaceWith(wrapperEl)
      wrapperEl.style.animation = 'none'
      replacesOldNotification = true
    }
  }
  if (!replacesOldNotification)
    topDoc.querySelector('.overlay .notifications').appendChild(wrapperEl)

  if (duration >= 0) removeNotification()
  else {
    duration = 0
    const closeEl = document.createElement('div')
    closeEl.classList.add('close')
    closeEl.innerHTML = topDoc.querySelector(
      '.iconAccess .closeWindow'
    ).innerHTML
    closeEl.addEventListener('click', function () {
      removeNotification()
    })
    closeEl.addEventListener('mouseover', function () {
      closeEl.style.backgroundColor = `var(--color-${color})`
    })
    closeEl.addEventListener('mouseout', function () {
      closeEl.style.removeProperty('background-color')
    })
    wrapperEl.appendChild(closeEl)
  }

  function removeNotification() {
    setTimeout(() => {
      if (timerBarEl) timerBarEl.style.width = '0%'
    }, 10)

    setTimeout(() => {
      if (wrapperEl)
        wrapperEl.style.animation =
          'notification-fly-out var(--transition-time-long) ease-in-out forwards'
    }, duration)

    const CSSRootTransitionTimeLong = parseInt(
      getComputedStyle(document.querySelector(':root'))
        .getPropertyValue('--transition-time-long')
        .trim()
        .slice(0, -'ms'.length)
    )
    setTimeout(
      () => {
        if (wrapperEl) wrapperEl.remove()
      },
      CSSRootTransitionTimeLong + duration + 500
    )
  }
}

/**
 * Copy text to clipboard. Tries Clipboard API first; falls back to execCommand for
 * non-secure contexts (e.g. Steam overlay, HTTP). Returns true if copy succeeded.
 */
function copyToClipboard(text) {
  if (text === undefined || text === null) text = ''
  const str = String(text)
  if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
    return navigator.clipboard.writeText(str).then(() => true).catch(() => false)
  }
  try {
    const el = document.createElement('textarea')
    el.value = str
    el.style.position = 'fixed'
    el.style.left = '-9999px'
    el.style.top = '0'
    el.setAttribute('readonly', '')
    document.body.appendChild(el)
    el.select()
    el.setSelectionRange(0, str.length)
    const ok = document.execCommand('copy')
    document.body.removeChild(el)
    return Promise.resolve(ok)
  } catch (e) {
    return Promise.resolve(false)
  }
}

async function getLanguageValue(value) {
  const language = await getLanguage()
  if (value === '' || value === null || value === undefined)
    return language.values.empty
  return language.values[value] || value
}

async function openInPedSearch(pedName) {
  await topWindow.openWindow('pedSearch')
  const iframe = topDoc
    .querySelector('.overlay .windows')
    .lastChild.querySelector('iframe')

  iframe.onload = () => {
    iframe.contentWindow.document.querySelector(
      '.searchInputWrapper #pedSearchInput'
    ).value = pedName
    iframe.contentWindow.document
      .querySelector('.searchInputWrapper button')
      .click()
  }
}

async function openInVehicleSearch(vehicleLicensePlate) {
  await topWindow.openWindow('vehicleSearch')
  const iframe = topDoc
    .querySelector('.overlay .windows')
    .lastChild.querySelector('iframe')

  iframe.onload = () => {
    iframe.contentWindow.document.querySelector(
      '.searchInputWrapper #vehicleSearchInput'
    ).value = vehicleLicensePlate
    iframe.contentWindow.document
      .querySelector('.searchInputWrapper button')
      .click()
  }
}

async function openFirearmsSearch(serialOrOwner) {
  await topWindow.openWindow('firearmsSearch')
  const iframe = topDoc
    .querySelector('.overlay .windows')
    .lastChild.querySelector('iframe')

  iframe.onload = () => {
    const input = iframe.contentWindow.document.querySelector(
      '.searchInputWrapper #firearmsSearchInput'
    )
    const btn = iframe.contentWindow.document.querySelector(
      '.searchInputWrapper button'
    )
    if (input && btn) {
      input.value = serialOrOwner != null && serialOrOwner !== '' ? String(serialOrOwner) : ''
      if (input.value) btn.click()
    }
  }
}

async function checkForReportOnCreatePage() {
  for (const iframe of topDoc.querySelectorAll('.overlay .windows .window iframe')) {
    try {
      if (typeof iframe.contentWindow?.reportIsOnCreatePage === 'function' && iframe.contentWindow.reportIsOnCreatePage()) {
        const language = await getLanguage()
        topWindow.showNotification(
          language.reports.notifications.createPageAlreadyOpen
        )
        return true
      }
    } catch (_) {}
  }
  return false
}

/** Find an existing Reports window element (so we can reuse it and bring to front). */
function findExistingReportsWindow() {
  const windows = topDoc.querySelectorAll('.overlay .windows .window')
  for (const win of windows) {
    const iframe = win.querySelector('iframe')
    if (iframe?.src && (iframe.src.includes('/page/reports.html') || iframe.src.includes('reports.html'))) {
      return { windowEl: win, iframe }
    }
  }
  return null
}

async function openPedAsOffenderInReport(type, pedName = '') {
  if (await checkForReportOnCreatePage()) return

  const existing = findExistingReportsWindow()
  if (existing) {
    const { windowEl, iframe } = existing
    if (typeof topWindow.focusWindowByElement === 'function') {
      topWindow.focusWindowByElement(windowEl)
    }
    const win = iframe.contentWindow
    if (win && typeof win.onCreateButtonClick === 'function') {
      await win.onCreateButtonClick()
      await win.onCreatePageTypeSelectorButtonClick(type)
      const pedInput = win.document.querySelector('.createPage #offenderSectionPedNameInput')
      if (pedInput) pedInput.value = pedName || ''
    }
    return
  }

  await topWindow.openWindow('reports')
  const iframe = topDoc
    .querySelector('.overlay .windows')
    .lastChild.querySelector('iframe')

  iframe.onload = async () => {
    await iframe.contentWindow.onCreateButtonClick()

    await iframe.contentWindow.onCreatePageTypeSelectorButtonClick(type)

    const pedInput = iframe.contentWindow.document.querySelector(
      '.createPage #offenderSectionPedNameInput'
    )
    if (pedInput) pedInput.value = pedName || ''
  }
}

/**
 * Open Reports window with prefill data from sessionStorage.
 * Used by Vehicle Search (impound), Person Search (injury), etc.
 * @param {string} reportType - 'impound' | 'injury' | 'trafficIncident' | 'citation' | 'arrest' | 'incident'
 * @param {object} prefillData - { source, pedName?, vehiclePlate?, vehicleData?, pedData? }
 */
async function openReportWithPrefill(reportType, prefillData) {
  if (await checkForReportOnCreatePage()) return
  try {
    sessionStorage.setItem('mdtproReportPrefill', JSON.stringify({
      source: prefillData.source || 'unknown',
      reportType,
      expires: Date.now() + 60000,
      data: prefillData
    }))
  } catch (_) {}
  const existing = findExistingReportsWindow()
  if (existing && typeof topWindow.focusWindowByElement === 'function') {
    topWindow.focusWindowByElement(existing.windowEl)
  }
  await topWindow.openWindow('reports')
}

async function openIdInReport(id, type = null) {
  if (!id) return

  async function performSearch(win, reportType) {
    if (typeof win.onListPageTypeSelectorButtonClick !== 'function') return false
    await win.onListPageTypeSelectorButtonClick(reportType)
    const list = win.document.querySelectorAll('.listPage .reportsList .listElement')
    for (const listElement of list) {
      if ((listElement.dataset.id || '') === String(id)) {
        const viewBtn = listElement.querySelector('.viewButton')
        if (viewBtn) viewBtn.click()
        return true
      }
    }
    return false
  }

  async function runOpenReportLogic(win) {
    if (!win) return
    let found = false
    if (type) {
      found = await performSearch(win, type)
    } else {
      const reportTypes = ['citation', 'arrest', 'incident', 'impound', 'trafficIncident', 'injury', 'propertyEvidence']
      for (const reportType of reportTypes) {
        found = await performSearch(win, reportType)
        if (found) break
      }
    }
    if (!found && typeof win.showNotification === 'function') {
      win.showNotification('Report not found.', 'warning')
    }
  }

  const existing = findExistingReportsWindow()
  if (existing) {
    const { windowEl, iframe } = existing
    if (typeof topWindow.focusWindowByElement === 'function') {
      topWindow.focusWindowByElement(windowEl)
    }
    const win = iframe.contentWindow
    if (win) {
      await runOpenReportLogic(win)
    }
    return
  }

  await topWindow.openWindow('reports')
  const iframe = topDoc
    .querySelector('.overlay .windows')
    .lastChild?.querySelector('iframe')
  if (!iframe) return

  iframe.onload = () => runOpenReportLogic(iframe.contentWindow)
  if (iframe.contentDocument?.readyState === 'complete') {
    runOpenReportLogic(iframe.contentWindow)
  }
}

let reportIsOnCreatePageBool = false
function reportIsOnCreatePage() {
  return reportIsOnCreatePageBool
}

async function getCurrencyString(number) {
  const language = await getLanguage()
  const config = await getConfig()
  if (config.displayCurrencySymbolBeforeNumber) {
    return language.units.currencySymbol + number
  }
  return number + language.units.currencySymbol
}

async function convertDaysToYMD(days) {
  const language = await getLanguage()
  const years = Math.floor(days / 365)
  const daysAfterYears = days % 365
  const months = Math.floor(daysAfterYears / 30)
  const remainingDays = daysAfterYears % 30
  const parts = []
  if (years) parts.push(`${years}${language.units.year}`)
  if (months) parts.push(`${months}${language.units.month}`)
  if (remainingDays) parts.push(`${remainingDays}${language.units.day}`)
  return parts.join(', ') || `0${language.units.day}`
}

async function convertMsToTimeString(ms) {
  const language = await getLanguage()
  const totalSeconds = Math.floor(ms / 1000)
  const h = Math.floor(totalSeconds / 3600)
  const m = Math.floor((totalSeconds % 3600) / 60)
  const s = totalSeconds % 60

  const pad = (n) => String(n).padStart(2, '0')
  const result = []

  if (h > 0) result.push(`${pad(h)}${language.units.hour}`)
  if (m > 0 || h > 0) result.push(`${pad(m)}${language.units.minute}`)
  if (s > 0 || m > 0 || h > 0 || result.length == 0)
    result.push(`${pad(s)}${language.units.second}`)

  return result.join(' ')
}

async function updateDomWithLanguage(page) {
  const language = await getLanguage()
  traverseObject(language[page].static, (key, value, path = []) => {
    const selector = [...path, key].join('.')
    document
      .querySelectorAll(`[data-language="${selector}"]`)
      .forEach((el) => (el.innerHTML = value))
    document
      .querySelectorAll(`[data-language-title="${selector}"]`)
      .forEach((el) => (el.title = value))
  })
}

const statusColorMap = {
  0: 'success',
  1: 'info',
  2: 'error',
  3: 'warning', // Pending (arrest)
}

function getActivePlugins() {
  const activePlugins = localStorage.getItem('activePlugins')
  if (!activePlugins) {
    localStorage.setItem('activePlugins', '[]')
    return []
  }
  return JSON.parse(activePlugins)
}

function removeGTAColorCodesFromString(str) {
  if (str == null) return str
  return str.replace(/~.~/g, '')
}
