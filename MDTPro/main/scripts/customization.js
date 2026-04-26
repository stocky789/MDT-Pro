;(async function loadDepartmentStylingIfEnabled() {
  const activePlugins = typeof getActivePlugins === 'function' ? getActivePlugins() : JSON.parse(localStorage.getItem('activePlugins') || '[]')
  if (!activePlugins.includes('DepartmentStyling')) return
  const pluginInfo = await fetch('/pluginInfo').then((r) => r.json()).catch(() => [])
  const plugin = pluginInfo.find((p) => p.id === 'DepartmentStyling')
  if (!plugin) return
  for (const pluginStyle of plugin.styles || []) {
    const link = document.createElement('link')
    link.rel = 'stylesheet'
    link.href = `/plugin/DepartmentStyling/style/${pluginStyle}`
    document.head.appendChild(link)
  }
  for (const pluginScript of plugin.scripts || []) {
    const el = document.createElement('script')
    el.src = `/plugin/DepartmentStyling/script/${pluginScript}`
    el.dataset.pluginId = 'DepartmentStyling'
    document.body.appendChild(el)
  }
})()

;(async function () {
  const config = await getConfig()
  if (config.updateDomWithLanguageOnLoad)
    await updateDomWithLanguage('customization')
})()

function bindConfigTooltip(iconEl, text) {
  const popover = document.getElementById('configTooltipPopover')
  if (!popover || !text) return
  const show = (e) => {
    popover.textContent = text
    popover.classList.remove('hidden')
    popover.setAttribute('aria-hidden', 'false')
    const rect = iconEl.getBoundingClientRect()
    const pad = 8
    let left = rect.left
    let top = rect.bottom + pad
    if (left + 320 > window.innerWidth) left = window.innerWidth - 320 - pad
    if (left < pad) left = pad
    if (top + popover.offsetHeight > window.innerHeight - pad) top = rect.top - popover.offsetHeight - pad
    if (top < pad) top = pad
    popover.style.left = left + 'px'
    popover.style.top = top + 'px'
  }
  const hide = () => {
    popover.classList.add('hidden')
    popover.setAttribute('aria-hidden', 'true')
  }
  iconEl.addEventListener('mouseenter', show)
  iconEl.addEventListener('mouseleave', hide)
  iconEl.addEventListener('focus', show)
  iconEl.addEventListener('blur', hide)
}

function showConfigFeedback(message, type) {
  const el = document.querySelector('.configFeedback')
  if (!el) return
  el.textContent = message
  el.className = 'configFeedback configFeedback--' + (type || 'info')
  el.classList.remove('hidden')
  const duration = type === 'error' ? 6000 : 4000
  clearTimeout(showConfigFeedback._timer)
  showConfigFeedback._timer = setTimeout(() => {
    el.classList.add('hidden')
    el.textContent = ''
  }, duration)
}

document.querySelectorAll('.sidebar button').forEach((button) => {
  button.addEventListener('click', () => {
    document.querySelector('.main').innerHTML = ''
    document
      .querySelectorAll('.sidebar button')
      .forEach((btn) => btn.classList.remove('selected'))
    button.classList.add('selected')
    button.blur()
  })
})

document
  .querySelector('.sidebar .plugins')
  .addEventListener('click', () => renderPluginsPage())

document
  .querySelector('.sidebar .config')
  .addEventListener('click', () => renderConfigPage())

async function renderPluginsPage() {
  const pluginsWrapper = document.createElement('div')
  pluginsWrapper.classList.add('pluginsWrapper')

  const pluginInfo = await (await fetch('/pluginInfo')).json()
  const language = await getLanguage()
  const MDTProVersionArr = (await (await fetch('/version')).text()).split('.')

  if (pluginInfo.length < 1) {
    document.querySelector('.main').innerHTML =
      language.customization.plugins.noPlugins
  }

  for (const plugin of pluginInfo) {
    const pluginElement = document.createElement('div')
    pluginElement.classList.add('plugin')
    pluginElement.title = plugin.id
    pluginElement.addEventListener('click', () => {
      togglePluginActivation(plugin.id)
      pluginElement.classList.toggle('selected')
    })
    const activePlugins = getActivePlugins()
    if (activePlugins.includes(plugin.id)) {
      pluginElement.classList.add('selected')
    }

    const name = document.createElement('div')
    name.classList.add('name')
    name.innerHTML = plugin.name
    pluginElement.appendChild(name)

    const description = document.createElement('div')
    description.classList.add('description')
    description.innerHTML = plugin.description
    pluginElement.appendChild(description)

    const versionArr = plugin.version.split('.')
    let versionColor = 'var(--color-success)'
    if (versionArr.length < 3) {
      versionColor = 'var(--color-error)'
    } else if (versionArr[0] != MDTProVersionArr[0]) {
      versionColor = 'var(--color-error)'
    } else if (versionArr[1] != MDTProVersionArr[1]) {
      versionColor = 'var(--color-warning)'
    } else if (versionArr[2] != MDTProVersionArr[2]) {
      versionColor = 'var(--color-warning)'
    }

    const version = document.createElement('div')
    version.classList.add('version')

    version.innerHTML = `<span style="color: var(--color-text-primary-half)">${language.customization.plugins.version}</span>: <span style="color: ${versionColor}">${plugin.version}</span>`
    pluginElement.appendChild(version)

    const author = document.createElement('div')
    author.classList.add('author')
    author.innerHTML = `<span style="color: var(--color-text-primary-half)">${language.customization.plugins.author}</span>: ${plugin.author}`
    pluginElement.appendChild(author)

    pluginsWrapper.appendChild(pluginElement)
  }
  const hint = document.createElement('p')
  hint.classList.add('pluginsHint')
  hint.style.marginTop = '16px'
  hint.style.color = 'var(--color-text-primary-half)'
  hint.style.fontSize = '13px'
  hint.textContent = 'Activated plugins load on the main MDT page. Some add sidebar items; others (e.g. ALPR) run in the background. Refresh the main page after enabling.'
  pluginsWrapper.appendChild(hint)
  document.querySelector('.main').appendChild(pluginsWrapper)
}

function togglePluginActivation(pluginId) {
  const activePlugins = getActivePlugins()
  if (activePlugins.includes(pluginId)) {
    activePlugins.splice(activePlugins.indexOf(pluginId), 1)
  } else {
    activePlugins.push(pluginId)
  }
  localStorage.setItem('activePlugins', JSON.stringify(activePlugins))

  // Refresh the main MDT page so plugin scripts load and the new app appears in the sidebar
  if (window.opener && !window.opener.closed) {
    try { window.opener.location.reload() } catch (_) {}
  }
}

function filterConfigBySearch(query) {
  const q = (query || '').trim().toLowerCase()
  document.querySelectorAll('.configSection').forEach((section) => {
    const header = section.querySelector('.configSectionHeader')
    const title = (header?.textContent || '').toLowerCase()
    const items = section.querySelectorAll('.configItem')
    let anyVisible = false
    items.forEach((item) => {
      const label = (item.querySelector('label')?.textContent || '').toLowerCase()
      const match = !q || title.includes(q) || label.includes(q)
      item.classList.toggle('filteredOut', !match)
      if (match) anyVisible = true
    })
    section.classList.toggle('filteredOut', !anyVisible)
  })
}

async function renderConfigPage() {
  const language = await getLanguage()
  const config = await getConfig()

  const stickyBar = document.createElement('div')
  stickyBar.classList.add('configStickyBar')

  const searchWrapper = document.createElement('div')
  searchWrapper.classList.add('configSearchWrapper')
  const searchInput = document.createElement('input')
  searchInput.type = 'text'
  searchInput.placeholder = language.customization?.searchPlaceholder || 'Filter settings…'
  searchInput.setAttribute('aria-label', language.customization?.searchPlaceholder || 'Filter settings')
  searchInput.addEventListener('input', () => filterConfigBySearch(searchInput.value))
  searchWrapper.appendChild(searchInput)
  stickyBar.appendChild(searchWrapper)

  const buttonWrapper = document.createElement('div')
  buttonWrapper.classList.add('buttonWrapper')
  const revertButton = document.createElement('button')
  revertButton.innerHTML = language.customization.revert || language.customization.reset || 'Revert'
  revertButton.title = language.customization?.revertTooltip || 'Discard unsaved changes and reload from server'
  revertButton.addEventListener('click', () => {
    document.querySelector('.main').innerHTML = ''
    renderConfigPage()
  })
  const saveButton = document.createElement('button')
  saveButton.innerHTML = language.customization.save
  saveButton.addEventListener('click', async () => {
    const newConfig = {}
    document.querySelectorAll('.configWrapper .configCheckbox[data-config-key], .configWrapper .configPresetSelect[data-config-key], .configWrapper .configManualInput[data-config-key]').forEach((el) => {
      const key = el.dataset.configKey
      if (el.classList.contains('configCheckbox')) {
        newConfig[key] = el.checked
      } else if (el.classList.contains('configPresetSelect')) {
        const isNumber = el.dataset.valueType === 'number'
        let raw
        if (el.value === '__custom__') {
          const manualInput = el.closest('.configItemValue')?.querySelector('.configManualInput')
          if (!manualInput) return // Skip: manual input missing, avoid saving invalid __custom__ or 0
          raw = manualInput.value
        } else {
          raw = el.value
        }
        newConfig[key] = isNumber ? (parseFloat(raw) || 0) : raw
      } else if (el.classList.contains('configPresetManualInput')) {
        // Skip: preset companion input is handled by configPresetSelect (reads from it when __custom__)
      } else {
        const isNumber = el.dataset.valueType === 'number'
        const raw = el.value
        newConfig[key] = isNumber ? (parseFloat(raw) || 0) : raw
      }
    })

    saveButton.disabled = true
    const language = await getLanguage()
    try {
      const resp = await fetch('/post/updateConfig', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(newConfig),
      })
      if (!resp.ok) {
        const errText = await resp.text()
        showConfigFeedback(language.customization?.saveError || 'Failed to save. ' + (errText || resp.status), 'error')
        return
      }
      localStorage.removeItem('config')
      showConfigFeedback(language.customization?.saveSuccess || 'Config saved. Refreshing…', 'success')
      document.querySelector('.main').innerHTML = ''
      await renderConfigPage()
      const main = document.querySelector('.main')
      if (main) main.scrollTop = 0
    } catch (err) {
      showConfigFeedback(language.customization?.saveError || 'Failed to save: ' + (err.message || 'Network error'), 'error')
    } finally {
      saveButton.disabled = false
    }
  })

  buttonWrapper.appendChild(revertButton)
  buttonWrapper.appendChild(saveButton)
  stickyBar.appendChild(buttonWrapper)
  document.querySelector('.main').appendChild(stickyBar)

  const configWrapper = document.createElement('div')
  configWrapper.classList.add('configWrapper')

  const allSectionKeys = new Set()
  CONFIG_SECTIONS.forEach((s) => s.keys.forEach((k) => allSectionKeys.add(k)))
  const hiddenConfigKeys = new Set([
    'alprEnabled',
    'alprPopupDuration',
    'alprHudAnchor',
    'alprHudOffsetX',
    'alprHudOffsetY',
    'alprHudScale',
    'citationArrestOptionsVersion',
    'checkForUpdates',
    'githubReleasesRepo',
  ])
  const otherKeys = Object.keys(config).filter((k) => !allSectionKeys.has(k) && !hiddenConfigKeys.has(k))
  const sectionsToRender = otherKeys.length > 0
    ? [...CONFIG_SECTIONS, { title: 'Other', keys: otherKeys }]
    : CONFIG_SECTIONS

  sectionsToRender.forEach((section) => {
    const sectionEl = document.createElement('div')
    sectionEl.classList.add('configSection')
    const header = document.createElement('div')
    header.classList.add('configSectionHeader')
    const sectionTitle = document.createElement('h3')
    sectionTitle.classList.add('configSectionTitle')
    sectionTitle.textContent = section.title
    header.appendChild(sectionTitle)
    header.addEventListener('click', () => sectionEl.classList.toggle('collapsed'))
    sectionEl.appendChild(header)
    const sectionContent = document.createElement('div')
    sectionContent.classList.add('configSectionContent')

    section.keys.forEach((key) => {
      const value = config[key]
      if (value === undefined) return

      const meta = getConfigFieldMeta(key)
      const isBoolean = typeof value === 'boolean'
      const isNumber = typeof value === 'number'
      const presets = meta.presets

      const wrapper = document.createElement('div')
      wrapper.classList.add('configItem')

      const labelRow = document.createElement('div')
      labelRow.classList.add('configItemLabelRow')
      const label = document.createElement('label')
      label.textContent = meta.label
      label.htmlFor = `config-${key}`
      const infoIcon = document.createElement('span')
      infoIcon.classList.add('configInfoIcon')
      infoIcon.setAttribute('role', 'img')
      infoIcon.setAttribute('aria-label', 'More information')
      infoIcon.textContent = '?'
      const tooltipText = (meta.tooltip && String(meta.tooltip).trim()) || 'No description available.'
      infoIcon.setAttribute('title', tooltipText)
      bindConfigTooltip(infoIcon, tooltipText)
      labelRow.appendChild(label)
      labelRow.appendChild(infoIcon)
      wrapper.appendChild(labelRow)

      const valueCol = document.createElement('div')
      valueCol.classList.add('configItemValue')

      if (isBoolean) {
        const checkbox = document.createElement('input')
        checkbox.type = 'checkbox'
        checkbox.checked = value
        checkbox.id = `config-${key}`
        checkbox.dataset.configKey = key
        checkbox.classList.add('configCheckbox')
        valueCol.appendChild(checkbox)
      } else if (presets && presets.length > 0) {
        const select = document.createElement('select')
        select.classList.add('configPresetSelect')
        select.id = `config-${key}`
        select.dataset.configKey = key
        select.dataset.valueType = isNumber ? 'number' : 'text'
        const strVal = String(value)
        let matchedPreset = false
        presets.forEach((p) => {
          if (p.value === '__custom__') return
          const opt = document.createElement('option')
          opt.value = String(p.value)
          opt.textContent = p.label
          if (String(p.value) === strVal || (isNumber && parseFloat(p.value) === parseFloat(value))) {
            opt.selected = true
            matchedPreset = true
          }
          select.appendChild(opt)
        })
        const customOpt = document.createElement('option')
        customOpt.value = '__custom__'
        customOpt.textContent = 'Custom...'
        if (!matchedPreset) customOpt.selected = true
        select.appendChild(customOpt)

        const manualWrapper = document.createElement('div')
        manualWrapper.classList.add('configManualWrapper')
        if (matchedPreset) manualWrapper.classList.add('hidden')
        const manualLabel = document.createElement('label')
        manualLabel.textContent = 'Manual value:'
        manualLabel.htmlFor = `config-manual-${key}`
        const manualInput = document.createElement('input')
        manualInput.type = isNumber ? 'number' : 'text'
        manualInput.id = `config-manual-${key}`
        manualInput.classList.add('configManualInput', 'configPresetManualInput')
        manualInput.dataset.configKey = key
        manualInput.dataset.valueType = isNumber ? 'number' : 'text'
        manualInput.value = value
        manualWrapper.appendChild(manualLabel)
        manualWrapper.appendChild(manualInput)

        select.addEventListener('change', () => {
          if (select.value === '__custom__') {
            manualWrapper.classList.remove('hidden')
            manualInput.focus()
          } else {
            manualWrapper.classList.add('hidden')
            manualInput.value = select.value
          }
        })
        manualInput.addEventListener('input', () => {
          let found = false
          for (let i = 0; i < select.options.length; i++) {
            const opt = select.options[i]
            if (opt.value === '__custom__') continue
            const match = isNumber
              ? parseFloat(opt.value) === parseFloat(manualInput.value)
              : String(opt.value) === manualInput.value
            if (match) {
              opt.selected = true
              manualWrapper.classList.add('hidden')
              found = true
              break
            }
          }
          if (!found) select.value = '__custom__'
        })

        valueCol.appendChild(select)
        valueCol.appendChild(manualWrapper)
      } else {
        const inputElement = document.createElement('input')
        inputElement.type = isNumber ? 'number' : 'text'
        inputElement.value = value
        inputElement.id = `config-${key}`
        inputElement.dataset.configKey = key
        inputElement.dataset.valueType = isNumber ? 'number' : 'text'
        inputElement.classList.add('configManualInput')
        valueCol.appendChild(inputElement)
      }

      wrapper.appendChild(valueCol)
      sectionContent.appendChild(wrapper)
    })

    if (sectionContent.querySelector('.configItem')) {
      sectionEl.appendChild(sectionContent)
      configWrapper.appendChild(sectionEl)
    }
  })

  document.querySelector('.main').appendChild(configWrapper)
  if (typeof window.__mdtCustomWallpaperAfterConfigRender === 'function') {
    try {
      window.__mdtCustomWallpaperAfterConfigRender()
    } catch (_) {}
  }
}

document.querySelector('.sidebar .plugins').click()
