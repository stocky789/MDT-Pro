/** Backup Quick Actions: hide Policing-Redefine-only controls when Ultimate Backup is the active provider (see GET /integration). */
function applyQuickBackupMenuForProvider(menu, ultimateBackup, lang) {
  if (!menu) return
  const ubNote = document.getElementById('qabBackupMenuUbNote')
  const codeLabel = document.getElementById('qabBackupResponseCodeLabel')

  menu.querySelectorAll('[data-qab-pr-only]').forEach((el) => {
    el.hidden = !!ultimateBackup
  })

  menu.querySelectorAll('.qabBackupSection').forEach((sec) => {
    const buttons = sec.querySelectorAll('button')
    const anyVisible = [...buttons].some((b) => !b.hidden)
    sec.hidden = buttons.length > 0 && !anyVisible
  })

  if (ultimateBackup) {
    const active = menu.querySelector('.qabBackupCode.active')
    if (active && (active.hidden || active.dataset.code === '1')) {
      menu.querySelectorAll('.qabBackupCode').forEach((b) => {
        b.classList.remove('active')
        b.setAttribute('aria-pressed', 'false')
      })
      const c2 = menu.querySelector('.qabBackupCode[data-code="2"]')
      if (c2 && !c2.hidden) {
        c2.classList.add('active')
        c2.setAttribute('aria-pressed', 'true')
      }
    }
  }

  if (codeLabel) {
    if (!codeLabel.dataset.qabDefaultLabel) {
      codeLabel.dataset.qabDefaultLabel = codeLabel.textContent.trim()
    }
    codeLabel.textContent = ultimateBackup
      ? (lang.quickActions?.backupResponseCodeLabelUb || 'Patrol code')
      : codeLabel.dataset.qabDefaultLabel
  }

  if (ubNote) {
    ubNote.hidden = !ultimateBackup
    ubNote.textContent = ultimateBackup ? (lang.quickActions?.backupUltimateBackupNote || '') : ''
  }
}

;(async function () {
  const config = await getConfig()
  const language = await getLanguage()
  let integration = {}
  try {
    const intRes = await fetch('/integration')
    if (intRes.ok) integration = await intRes.json()
  } catch (_) {
    /* older plugin or offline */
  }
  if (config.updateDomWithLanguageOnLoad) await updateDomWithLanguage('index')
  applySettingsInfoTooltips(language)

  // Quick Actions Bar visibility and handlers
  const quickActionsBar = document.getElementById('quickActionsBar')
  const qabBackupMenu = document.getElementById('qabBackupMenu')
  const useUbBackupMenu = integration.backupProvider === 'UltimateBackup'
  if (qabBackupMenu) applyQuickBackupMenuForProvider(qabBackupMenu, useUbBackupMenu, language)
  const requestBackupAction = async (action, btnEl) => {
    const code = parseInt(qabBackupMenu?.querySelector('.qabBackupCode.active')?.dataset?.code ?? '2', 10) || 2
    const res = await (await fetch('/post/requestBackup', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ action, responseCode: code })
    })).json()
    if (res?.success) topWindow.showNotification(language.quickActions?.backupSuccess || 'Backup requested.', 'checkMark')
    else topWindow.showNotification(res?.error || 'Backup request failed.', 'warning')
    if (btnEl) btnEl.classList.remove('loading')
  }
  if (quickActionsBar) {
    quickActionsBar.classList.toggle('hidden', config.quickActionsBarEnabled === false)
    // Backup dropdown toggle
    const qabBackupMain = quickActionsBar.querySelector('.qabBackupMain')
    if (qabBackupMain && qabBackupMenu) {
      qabBackupMain.addEventListener('click', function (e) {
        e.stopPropagation()
        const isOpen = qabBackupMenu.classList.toggle('open')
        qabBackupMain.setAttribute('aria-expanded', String(isOpen))
      })
      document.addEventListener('click', function (ev) {
        if (qabBackupMenu.classList.contains('open') && !qabBackupMain.contains(ev.target) && !qabBackupMenu.contains(ev.target)) {
          qabBackupMenu.classList.remove('open')
          qabBackupMain.setAttribute('aria-expanded', 'false')
        }
      })
      qabBackupMenu.querySelectorAll('.qabBackupCode').forEach((codeBtn) => {
        codeBtn.addEventListener('click', function (e) {
          e.stopPropagation()
          qabBackupMenu.querySelectorAll('.qabBackupCode').forEach((b) => { b.classList.remove('active'); b.setAttribute('aria-pressed', 'false') })
          codeBtn.classList.add('active')
          codeBtn.setAttribute('aria-pressed', 'true')
        })
      })
      qabBackupMenu.querySelectorAll('button[role="menuitem"]').forEach((mi) => {
        mi.addEventListener('click', async function (e) {
          e.stopPropagation()
          const action = this.dataset.action
          if (!action) return
          qabBackupMenu.classList.remove('open')
          qabBackupMain.setAttribute('aria-expanded', 'false')
          qabBackupMain.classList.add('loading')
          try { await requestBackupAction(action, qabBackupMain) } catch (e) { topWindow.showNotification(language.quickActions?.error || 'Action failed.', 'error'); qabBackupMain.classList.remove('loading') }
        })
      })
    }
    // Notepad: open popup (excluded from loading/API flow below)
    const notepadBtn = quickActionsBar.querySelector('.qabBtn[data-action="notepad"]')
    const notepadPopup = document.getElementById('notepadPopup')
    const notepadText = document.getElementById('notepadText')
    const NOTEPAD_STORAGE_KEY = 'mdt-notepad'
    if (notepadBtn && notepadPopup && notepadText) {
      const openNotepad = () => {
        notepadText.value = localStorage.getItem(NOTEPAD_STORAGE_KEY) || ''
        notepadPopup.classList.add('open')
        notepadPopup.setAttribute('aria-hidden', 'false')
        notepadText.focus()
      }
      const closeNotepad = () => {
        notepadPopup.classList.remove('open')
        notepadPopup.setAttribute('aria-hidden', 'true')
      }
      notepadBtn.addEventListener('click', function () {
        openNotepad()
      })
      notepadPopup.querySelectorAll('[data-notepad-close]').forEach((el) => {
        el.addEventListener('click', closeNotepad)
      })
      notepadPopup.querySelector('.notepadPopupSave')?.addEventListener('click', function () {
        localStorage.setItem(NOTEPAD_STORAGE_KEY, notepadText.value)
        topWindow.showNotification(language.quickActions?.notepadSaved || 'Notepad saved.', 'checkMark')
      })
      notepadPopup.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') closeNotepad()
      })
    }

    // Narcotics Cheat Sheet: open popup (excluded from loading/API flow below)
    const narcoticsBtn = quickActionsBar.querySelector('.qabBtn[data-action="narcoticsCheatsheet"]')
    const narcoticsPopup = document.getElementById('narcoticsCheatsheetPopup')
    const narcoticsContent = document.getElementById('narcoticsCheatsheetContent')
    if (narcoticsBtn && narcoticsPopup && narcoticsContent && typeof buildNarcoticsCheatsheetContent === 'function') {
      let contentBuilt = false
      const openNarcoticsCheatsheet = () => {
        if (!contentBuilt) {
          narcoticsContent.appendChild(buildNarcoticsCheatsheetContent())
          contentBuilt = true
        }
        narcoticsPopup.classList.add('open')
        narcoticsPopup.setAttribute('aria-hidden', 'false')
      }
      const closeNarcoticsCheatsheet = () => {
        narcoticsPopup.classList.remove('open')
        narcoticsPopup.setAttribute('aria-hidden', 'true')
      }
      narcoticsBtn.addEventListener('click', openNarcoticsCheatsheet)
      narcoticsPopup.querySelectorAll('[data-narcotics-cheatsheet-close]').forEach((el) => {
        el.addEventListener('click', closeNarcoticsCheatsheet)
      })
      narcoticsPopup.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') closeNarcoticsCheatsheet()
      })
    }

    // Panic, ALPR and other qabBtn (excluding backup, notepad, narcoticsCheatsheet which have their own handlers)
    quickActionsBar.querySelectorAll('.qabBtn:not(.qabBackupMain):not([data-action="notepad"]):not([data-action="narcoticsCheatsheet"])').forEach((btn) => {
      btn.addEventListener('click', async function () {
        const action = this.dataset.action
        if (!action) return
        if (this.classList.contains('loading')) return
        this.classList.add('loading')
        try {
          if (action === 'panic') {
            const res = await (await fetch('/post/requestBackup', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ action: 'panic' })
            })).json()
            if (res?.success) topWindow.showNotification(language.quickActions?.panicSuccess || 'Panic backup requested.', 'checkMark')
            else topWindow.showNotification(res?.error || 'Backup request failed.', 'warning')
          } else if (action === 'alpr') {
            const url = (typeof location !== 'undefined' && location.origin) ? `${location.origin}/post/alprClear` : '/post/alprClear'
            const alprRes = await fetch(url, {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: '{}'
            })
            if (alprRes.ok) {
              const notificationsEl = topDoc.querySelector('.overlay .notifications')
              if (notificationsEl) {
                notificationsEl.querySelectorAll('.alpr-popup').forEach((el) => el.remove())
              }
              topWindow.showNotification(language.quickActions?.alprCleared || 'ALPR cleared.', 'checkMark')
            } else topWindow.showNotification(language.quickActions?.error || 'Action failed.', 'warning')
          }
        } catch (e) {
          topWindow.showNotification(language.quickActions?.error || 'Action failed.', 'error')
        } finally {
          this.classList.remove('loading')
        }
      })
    })
  }
  const version = await (await fetch('/version')).text()
  document.querySelector('.overlay .settings .version').innerHTML =
    `${language.index.settings.version}: ${version}`

  const officerInformationData = await (
    await fetch('/data/officerInformationData')
  ).json()
  applyOfficerInformationToDOM(officerInformationData)

  const metrics = await (await fetch('/data/officerMetrics')).json()
  const metricsContent = document.querySelector(
    '.overlay .officerProfile .officerMetrics .metricsContent'
  )
  const ml = language.index.settings?.officerMetrics || {}
  const avgDuration = await convertMsToTimeString(metrics.averageShiftDurationMs)
  metricsContent.innerHTML = `
    <span class="metricLabel">${ml.totalShifts || 'Total Shifts'}</span><span class="metricValue">${metrics.totalShifts}</span>
    <span class="metricLabel">${ml.avgDuration || 'Avg Shift Duration'}</span><span class="metricValue">${avgDuration}</span>
    <span class="metricLabel">${ml.incidents || 'Incidents'}</span><span class="metricValue">${metrics.totalIncidentReports}</span>
    <span class="metricLabel">${ml.citations || 'Citations'}</span><span class="metricValue">${metrics.totalCitationReports}</span>
    <span class="metricLabel">${ml.arrests || 'Arrests'}</span><span class="metricValue">${metrics.totalArrestReports}</span>
    <span class="metricLabel">${ml.totalReports || 'Total Reports'}</span><span class="metricValue">${metrics.totalReports}</span>
    <span class="metricLabel">${ml.reportsPerShift || 'Reports/Shift'}</span><span class="metricValue">${metrics.reportsPerShift}</span>
  `

  const pluginInfo = await (await fetch('/pluginInfo')).json()
  let activePlugins = getActivePlugins()
  const isDev = /^localhost$|^127\.0\.0\.1$/i.test(location.hostname)
  // When no plugins are enabled (fresh install or cleared storage), default to all bundled plugins so Calendar etc. appear
  if (activePlugins.length === 0 && pluginInfo.length > 0) {
    activePlugins = pluginInfo.map((p) => p.id)
    localStorage.setItem('activePlugins', JSON.stringify(activePlugins))
  } else if (isDev && pluginInfo.length > 0) {
    const serverIds = pluginInfo.map((p) => p.id)
    const missing = serverIds.filter((id) => !activePlugins.includes(id))
    if (missing.length > 0) {
      activePlugins = [...activePlugins, ...missing]
      localStorage.setItem('activePlugins', JSON.stringify(activePlugins))
    }
  }
  for (const plugin of pluginInfo) {
    if (activePlugins.includes(plugin.id)) {
      for (const pluginScript of plugin.scripts) {
        const script = document.createElement('script')
        script.src = `/plugin/${plugin.id}/script/${pluginScript}`
        script.dataset.pluginId = plugin.id
        document.body.appendChild(script)
      }
      for (const pluginStyle of plugin.styles) {
        const link = document.createElement('link')
        link.rel = 'stylesheet'
        link.href = `/plugin/${plugin.id}/style/${pluginStyle}`
        link.dataset.pluginId = plugin.id
        document.head.appendChild(link)
      }
    }
  }
})()

const timeWS = new WebSocket(`ws://${location.host}/ws`)
timeWS.onopen = () => timeWS.send('interval/time')

let currentShift = null

timeWS.onmessage = async (event) => {
  const config = await getConfig()
  const data = JSON.parse(event.data)
  const inGameDateArr = data.response.split(':')
  const inGameDate = new Date()
  inGameDate.setHours(inGameDateArr[0])
  inGameDate.setMinutes(inGameDateArr[1])
  inGameDate.setSeconds(inGameDateArr[2])
  const realDate = new Date()
  document.querySelector('.taskbar .time').innerHTML = `${
    config.useInGameTime
      ? inGameDate.toLocaleTimeString([], {
          hour: '2-digit',
          minute: '2-digit',
          second: config.showSecondsInTaskbarClock ? '2-digit' : undefined,
        })
      : realDate.toLocaleTimeString([], {
          hour: '2-digit',
          minute: '2-digit',
          second: config.showSecondsInTaskbarClock ? '2-digit' : undefined,
        })
  }<br>${realDate.toLocaleDateString()}`

  currentShift =
    currentShift ?? (await (await fetch('/data/currentShift')).json())
  applyCurrentShiftToDOM(
    currentShift,
    config.useInGameTime ? inGameDate : realDate
  )
}

document
  .querySelector('.overlay .officerProfile .currentShift .buttonWrapper .startShift')
  .addEventListener('click', async function () {
    if (this.classList.contains('loading')) return
    showLoadingOnButton(this)

    const response = await (
      await fetch('/post/modifyCurrentShift', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: 'start',
      })
    ).text()

    const language = await getLanguage()
    if (response == 'OK') {
      currentShift = await (await fetch('/data/currentShift')).json()
      const officerInformationData = await (
        await fetch('/data/officerInformationData')
      ).json()
      if (officerInformationData.rank && officerInformationData.lastName) {
        showNotification(
          `${language.index.notifications.currentShiftStartedOfficerInformationExists} ${officerInformationData.rank} ${officerInformationData.lastName}`
        )
      } else {
        showNotification(language.index.notifications.currentShiftStarted)
      }
    } else {
      showNotification(
        language.index.notifications.currentShiftStartedError,
        'error'
      )
    }

    hideLoadingOnButton(this)
  })

document
  .querySelector('.overlay .officerProfile .currentShift .buttonWrapper .endShift')
  .addEventListener('click', async function () {
    if (this.classList.contains('loading')) return
    showLoadingOnButton(this)

    const response = await (
      await fetch('/post/modifyCurrentShift', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: 'end',
      })
    ).text()

    const language = await getLanguage()
    if (response == 'OK') {
      currentShift = await (await fetch('/data/currentShift')).json()
      showNotification(language.index.notifications.currentShiftEnded)
    } else {
      showNotification(
        language.index.notifications.currentShiftEndedError,
        'error'
      )
    }

    hideLoadingOnButton(this)
  })

async function applyCurrentShiftToDOM(currentShift, currentDate) {
  const language = await getLanguage()
  if (currentShift.startTime) {
    document.querySelector(
      '.overlay .officerProfile .currentShift .buttonWrapper .startShift'
    ).disabled = true
    document.querySelector(
      '.overlay .officerProfile .currentShift .buttonWrapper .endShift'
    ).disabled = false

    document.querySelector(
      '.overlay .officerProfile .currentShift .startTime'
    ).innerHTML = `${
      language.index.settings.currentShift.startTime
    }: ${new Date(currentShift.startTime).toLocaleTimeString()}`

    const duration = await convertMsToTimeString(
      currentDate.getTime() - new Date(currentShift.startTime).getTime()
    )
    document.querySelector(
      '.overlay .officerProfile .currentShift .duration'
    ).innerHTML =
      `${language.index.settings.currentShift.duration}: ${duration}`
  } else {
    document.querySelector(
      '.overlay .officerProfile .currentShift .buttonWrapper .startShift'
    ).disabled = false
    document.querySelector(
      '.overlay .officerProfile .currentShift .buttonWrapper .endShift'
    ).disabled = true

    document.querySelector(
      '.overlay .officerProfile .currentShift .startTime'
    ).innerHTML = language.index.settings.currentShift.offDuty
    document.querySelector(
      '.overlay .officerProfile .currentShift .duration'
    ).innerHTML = ''
  }
}

timeWS.onclose = async () => {
  const language = await getLanguage()
  showNotification(language.index.notifications.webSocketOnClose, 'warning', -1)
}

const locationWS = new WebSocket(`ws://${location.host}/ws`)
locationWS.onopen = () => locationWS.send('interval/playerLocation')

locationWS.onmessage = async (event) => {
  const location = JSON.parse(event.data).response
  const icon = document.querySelector('.iconAccess .location').innerHTML
  const postal = location?.Postal ?? ''
  const street = location?.Street ?? ''
  const area = location?.Area ?? ''
  document.querySelector('.taskbar .location').innerHTML =
    `${icon} ${postal} ${street},<br>${area}`
}

locationWS.onclose = async () => {
  const language = await getLanguage()
  showNotification(language.index.notifications.webSocketOnClose, 'warning', -1)
}

const desktopItems = document.querySelectorAll('.desktop .desktopItem')

for (const desktopItem of desktopItems) {
  desktopItem.addEventListener('click', async function () {
    const name = this.dataset.name
    if (!name) return
    try {
      await openWindow(name)
    } catch (e) {
      const msg = e?.message || String(e)
      console.error('openWindow failed:', name, e)
      if (typeof topWindow.showNotification === 'function') {
        topWindow.showNotification(
          (await getLanguage()).index?.notifications?.errorLoadingPage || 'Failed to open: ' + name + (msg ? ' — ' + msg : ''),
          'error'
        )
      }
    }
  })
}

async function openWindow(name, pluginId = null) {
  const config = await getConfig()
  const url = pluginId
    ? `/plugin/${pluginId}/page/${name}.html`
    : `/page/${name}.html`
  let size = [config.initialWindowWidth, config.initialWindowHeight]
  if (name === 'court') {
    size = [Math.max(size[0], 720), Math.max(size[1], 420)]
  }
  const windowDimensions = [window.innerWidth, window.innerHeight]
  const offset = [
    windowDimensions[0] / 2 - size[0] / 2,
    windowDimensions[1] / 2 - size[1] / 2,
  ]

  const existingWindows = document.querySelectorAll('.overlay .windows .window')
  // Stagger offset so multiple windows don't fully overlap (avoids clicks hitting wrong window)
  const staggerPx = 36
  const staggerIndex = Math.min(existingWindows.length, 12)
  const windowElement = document.createElement('div')
  windowElement.style.width = `${size[0]}px`
  windowElement.style.height = `${size[1]}px`
  windowElement.style.left = `${offset[0] + staggerIndex * staggerPx}px`
  windowElement.style.top = `${offset[1] + staggerIndex * staggerPx}px`
  windowElement.style.scale = '0'
  windowElement.classList.add('window')

  const taskbarIcon = document.createElement('button')

  function focusWindow() {
    document.querySelectorAll('.overlay .windows .window').forEach((win) => {
      win.style.zIndex = ''
    })
    windowElement.style.zIndex = '3'

    document
      .querySelectorAll('.taskbar .icons button.focused')
      .forEach((icon) => {
        icon.classList.remove('focused')
      })
    taskbarIcon.classList.add('focused')
  }
  windowElement.addEventListener('mousedown', focusWindow)

  const iframe = document.createElement('iframe')
  iframe.src = url
  const header = document.createElement('div')
  header.classList.add('windowHeader')
  let x, y
  let lastSize
  let lastOffset
  header.addEventListener('mousedown', function (e) {
    e.preventDefault()
    x = e.clientX - windowElement.offsetLeft
    y = e.clientY - windowElement.offsetTop
    document.onmouseup = function () {
      document.onmouseup = null
      document.onmousemove = null
      windowElement.style.transition = '250ms ease'
    }
    document.onmousemove = function (e) {
      if (
        e.clientX < 0 ||
        e.clientY < 0 ||
        e.clientX > document.querySelector('.desktop').clientWidth ||
        e.clientY > document.querySelector('.desktop').clientHeight
      ) {
        document.onmouseup()
      }
      if (e.target == windowControls || windowControls.contains(e.target)) {
        document.onmouseup()
        return
      }
      windowElement.style.transition = 'none'
      windowElement.style.left = e.clientX - x + 'px'
      windowElement.style.top = e.clientY - y + 'px'
      if (windowElement.classList.contains('maximized')) {
        windowElement.classList.remove('maximized')
        windowElement.style.width = lastSize[0]
        windowElement.style.height = lastSize[1]
        windowElement.querySelector(
          '.windowHeader .windowControls .maximizeButton'
        ).innerHTML = document.querySelector(
          '.iconAccess .maximizeWindow'
        ).innerHTML
      }
    }
  })

  new ResizeObserver(() => {
    windowElement.onmouseup = function () {
      windowElement.style.transition = '250ms ease'
    }
    windowElement.onmousedown = function () {
      windowElement.style.transition = 'none'
    }
  }).observe(windowElement)

  const windowControls = document.createElement('div')
  windowControls.classList.add('windowControls')

  const iconTitleWrapper = document.createElement('div')
  iconTitleWrapper.classList.add('iconTitleWrapper')

  const icon = document.createElement('div')
  icon.classList.add('icon')
  icon.innerHTML = document.querySelector(`.iconAccess .${name}`).innerHTML

  const title = document.createElement('div')
  title.classList.add('title')
  iframe.addEventListener('load', () => {
    title.innerHTML = iframe.contentDocument.title
    taskbarIcon.title = iframe.contentDocument.title
    windowElement.style.minWidth = `${
      iconTitleWrapper.offsetWidth + windowControls.offsetWidth
    }px`
    windowElement.style.scale = '1'

    document.dispatchEvent(new Event(`windowLoaded:${name}`))

    new MutationObserver(() => {
      title.innerHTML = iframe.contentDocument.title
      taskbarIcon.title = iframe.contentDocument.title
      windowElement.style.minWidth = `${
        iconTitleWrapper.offsetWidth + windowControls.offsetWidth
      }px`
    }).observe(iframe.contentDocument.querySelector('title'), {
      childList: true,
    })

    iframe.contentWindow.addEventListener('mousedown', focusWindow)
    iframe.contentWindow.addEventListener('mousedown', function () {
      document.querySelector('.overlay .officerProfile').classList.add('hide')
      document.querySelector('.overlay .settings').classList.add('hide')
    })
  })

  const minimize = document.createElement('div')
  minimize.classList.add('minimizeButton')
  minimize.innerHTML = document.querySelector(
    '.iconAccess .minimizeWindow'
  ).innerHTML
  minimize.addEventListener('mousedown', function (e) {
    e.stopPropagation()
  })
  minimize.addEventListener('click', function (e) {
    e.stopPropagation()
    windowElement.classList.toggle('minimized')
    if (!windowElement.classList.contains('minimized')) {
      focusWindow()
    } else {
      taskbarIcon.classList.remove('focused')
    }
  })

  const maximize = document.createElement('div')
  maximize.classList.add('maximizeButton')
  maximize.innerHTML = document.querySelector(
    '.iconAccess .maximizeWindow'
  ).innerHTML
  maximize.addEventListener('mousedown', function (e) {
    e.stopPropagation()
  })
  maximize.addEventListener('click', function (e) {
    e.stopPropagation()
    if (windowElement.classList.contains('maximized')) {
      windowElement.classList.remove('maximized')
      windowElement.style.width = lastSize[0]
      windowElement.style.height = lastSize[1]
      windowElement.style.left = lastOffset[0]
      windowElement.style.top = lastOffset[1]
      windowElement.querySelector(
        '.windowHeader .windowControls .maximizeButton'
      ).innerHTML = document.querySelector(
        '.iconAccess .maximizeWindow'
      ).innerHTML
    } else {
      lastSize = [windowElement.style.width, windowElement.style.height]
      lastOffset = [windowElement.style.left, windowElement.style.top]
      windowElement.style.width = 'calc(100% - 2px)'
      windowElement.style.height = `calc(100% - var(--tb-height))`
      windowElement.style.left = '0'
      windowElement.style.top = '' /* use CSS .window.maximized top so window sits below taskbar */
      windowElement.style.minWidth = `${
        iconTitleWrapper.offsetWidth + windowControls.offsetWidth
      }px`
      windowElement.classList.add('maximized')
      windowElement.querySelector(
        '.windowHeader .windowControls .maximizeButton'
      ).innerHTML = document.querySelector(
        '.iconAccess .restoreWindow'
      ).innerHTML
    }
  })
  header.addEventListener('dblclick', function () {
    maximize.click()
  })

  const close = document.createElement('div')
  close.classList.add('closeButton')
  close.innerHTML = document.querySelector('.iconAccess .closeWindow').innerHTML
  close.addEventListener('mousedown', function (e) {
    e.stopPropagation()
  })
  close.addEventListener('click', async function (e) {
    e.stopPropagation()
    windowElement.style.pointerEvents = 'none'
    taskbarIcon.style.pointerEvents = 'none'
    const CSSRootTransitionTimeLong = parseInt(
      getComputedStyle(document.querySelector(':root'))
        .getPropertyValue('--transition-time-long')
        .trim()
        .slice(0, -'ms'.length)
    )
    windowElement.style.scale = '0'
    taskbarIcon.style.opacity = '0'
    await sleep(CSSRootTransitionTimeLong)
    windowElement.remove()
    taskbarIcon.remove()
  })
  iconTitleWrapper.appendChild(icon)
  iconTitleWrapper.appendChild(title)
  header.appendChild(iconTitleWrapper)
  windowControls.appendChild(minimize)
  windowControls.appendChild(maximize)
  windowControls.appendChild(close)
  header.appendChild(windowControls)
  windowElement.appendChild(header)
  windowElement.appendChild(iframe)

  document.querySelector('.overlay .windows').appendChild(windowElement)

  focusWindow()

  taskbarIcon.classList.add('open', 'taskbarWindow')
  const taskbarIconIcon = document.createElement('span')
  taskbarIconIcon.classList.add('taskbarWindowIcon')
  taskbarIconIcon.innerHTML = icon.innerHTML
  taskbarIcon.appendChild(taskbarIconIcon)
  const taskbarClose = document.createElement('span')
  taskbarClose.classList.add('taskbarWindowClose')
  taskbarClose.innerHTML = document.querySelector('.iconAccess .closeWindow').innerHTML
  taskbarClose.title = 'Close'
  taskbarClose.addEventListener('click', function (e) {
    e.stopPropagation()
    close.click()
  })
  taskbarIcon.appendChild(taskbarClose)
  taskbarIcon.addEventListener('click', function () {
    this.blur()
    if (
      !taskbarIcon.classList.contains('focused') &&
      !windowElement.classList.contains('minimized')
    ) {
      focusWindow()
    } else {
      minimize.click()
    }
  })

  taskbarIcon.style.opacity = '0'

  document.querySelector('.taskbar .icons').appendChild(taskbarIcon)

  requestAnimationFrame(() => {
    taskbarIcon.style.opacity = '1'
  })
}

/**
 * Bring a specific window to front (for use when reusing e.g. Reports window).
 * Called from root.js so that "Open report from ped/vehicle" always raises the Reports window.
 */
function focusWindowByElement(windowEl) {
  if (!windowEl || !windowEl.classList?.contains('window')) return
  document.querySelectorAll('.overlay .windows .window').forEach((win) => {
    win.style.zIndex = ''
  })
  windowEl.style.zIndex = '3'
  windowEl.classList.remove('minimized')
  const windows = document.querySelectorAll('.overlay .windows .window')
  const taskbarWindows = document.querySelectorAll('.taskbar .icons button.taskbarWindow')
  const index = Array.from(windows).indexOf(windowEl)
  if (index >= 0 && taskbarWindows[index]) {
    taskbarWindows.forEach((btn) => btn.classList.remove('focused'))
    taskbarWindows[index].classList.add('focused')
  }
}

document.addEventListener('mousedown', function (e) {
  const officerProfileBtn = document.querySelector('.taskbar .icons .officerProfile')
  const settingsBtn = document.querySelector('.taskbar .icons .settings')
  const officerProfileEl = document.querySelector('.overlay .officerProfile')
  const settingsEl = document.querySelector('.overlay .settings')
  const clickedProfile = e.target === officerProfileBtn || officerProfileBtn?.contains(e.target)
  const clickedSettings = e.target === settingsBtn || settingsBtn?.contains(e.target)
  const clickedProfilePanel = e.target === officerProfileEl || officerProfileEl?.contains(e.target)
  const clickedSettingsPanel = e.target === settingsEl || settingsEl?.contains(e.target)
  if (clickedProfile || clickedSettings || clickedProfilePanel || clickedSettingsPanel) {
    return
  }
  officerProfileEl?.classList.add('hide')
  settingsEl?.classList.add('hide')
})

new MutationObserver(() => {
  setMinWidthOnTaskbar()
}).observe(document.querySelector('.taskbar'), {
  childList: true,
  subtree: true,
})

setMinWidthOnTaskbar()
function setMinWidthOnTaskbar() {
  const taskbar = document.querySelector('.taskbar')
  const locationWidth = taskbar.querySelector('.location').clientWidth
  const timeWidth = taskbar.querySelector('.time').clientWidth
  const additionalWidth =
    locationWidth > timeWidth ? locationWidth * 2 : timeWidth * 2
  taskbar.style.minWidth = `${
    taskbar.querySelector('.icons').clientWidth + additionalWidth
  }px`
}

document
  .querySelector('.overlay .officerProfile .officerInformation .autoFill')
  .addEventListener('click', async function () {
    if (this.classList.contains('loading')) return
    showLoadingOnButton(this)

    const officerInformation = await (
      await fetch('/data/officerInformation')
    ).json()
    applyOfficerInformationToDOM(officerInformation)

    // Persist filled data so it's remembered across sessions
    const payload = buildOfficerInformationPayload()
    const response = await (
      await fetch('post/updateOfficerInformationData', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
    ).text()
    const language = await getLanguage()
    if (response === 'OK') {
      showNotification(
        language.index.notifications.officerInformationSaved,
        'checkMark'
      )
    }

    hideLoadingOnButton(this)
  })

function applySettingsInfoTooltips(language) {
  const base = language?.index?.static
  if (!base) return
  document.querySelectorAll('.overlay .officerProfile [data-language-info], .overlay .settings [data-language-info]').forEach((el) => {
    const key = el.getAttribute('data-language-info')
    if (!key) return
    const value = key.split('.').reduce((o, k) => o?.[k], base)
    if (typeof value === 'string') {
      el.title = value
      el.setAttribute('aria-label', value)
    }
  })
}

const OFFICER_INFO_KEYS = [
  'firstName',
  'lastName',
  'badgeNumber',
  'rank',
  'callSign',
  'agency',
]

function applyOfficerInformationToDOM(officerInformation) {
  const inputWrapper = document.querySelector(
    '.overlay .officerProfile .officerInformation .inputWrapper'
  )
  if (inputWrapper) {
    for (const key of OFFICER_INFO_KEYS) {
      const input = inputWrapper.querySelector(`.${key} input`)
      if (!input) continue
      const val = officerInformation[key]
      input.value =
        val === null || val === undefined ? '' : String(val)
    }
  }

  const taskbarStatus = document.querySelector('.taskbarOfficerStatus')
  if (taskbarStatus) {
    for (const key of OFFICER_INFO_KEYS) {
      const item = taskbarStatus.querySelector(`.officerStatusItem[data-key="${key}"]`)
      if (!item) continue
      const valueEl = item.querySelector('.officerStatusValue')
      if (!valueEl) continue
      const val = officerInformation[key]
      const display =
        val === null || val === undefined ? '—' : String(val).trim()
      valueEl.textContent = display || '—'
      item.classList.toggle('hasValue', !!display && display !== '—')
    }
  }
}

function buildOfficerInformationPayload() {
  const inputWrapper = document.querySelector(
    '.overlay .officerProfile .officerInformation .inputWrapper'
  )
  if (!inputWrapper) return {}
  const v = (sel) => inputWrapper.querySelector(sel)?.value?.trim() ?? ''
  const badgeRaw = v('.badgeNumber input')
  return {
    firstName: v('.firstName input') || null,
    lastName: v('.lastName input') || null,
    badgeNumber: badgeRaw === '' ? null : (parseInt(badgeRaw, 10) || null),
    rank: v('.rank input') || null,
    callSign: v('.callSign input') || null,
    agency: v('.agency input') || null,
  }
}

document
  .querySelector('.overlay .officerProfile .officerInformation .save')
  .addEventListener('click', async function () {
    if (this.classList.contains('loading')) return
    showLoadingOnButton(this)

    const response = await (
      await fetch('post/updateOfficerInformationData', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(buildOfficerInformationPayload()),
      })
    ).text()

    const language = await getLanguage()

    if (response == 'OK') {
      applyOfficerInformationToDOM(buildOfficerInformationPayload())
      showNotification(
        language.index.notifications.officerInformationSaved,
        'checkMark'
      )
    } else {
      showNotification(
        language.index.notifications.officerInformationError,
        'error'
      )
    }

    hideLoadingOnButton(this)
  })

document.querySelector('.overlay .settings .customization')?.addEventListener('keydown', function (e) {
  if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault()
    window.open('/page/customization', '_blank')
  }
})
