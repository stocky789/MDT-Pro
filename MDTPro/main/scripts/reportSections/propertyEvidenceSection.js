/** Property and Evidence Receipt form section. Subjects (Recent IDs + multiple), drugs seized (add pattern + quantity), firearms seized (add pattern), other contraband. */
async function getPropertyEvidenceSection (data = {}, isList = false) {
  const language = await getLanguage()
  const labels = language.reports?.sections?.propertyEvidence || {}
  const section = document.createElement('div')
  section.classList.add('section', 'propertyEvidenceSection')
  if (isList) section.classList.add('searchResponseWrapper')

  let shell = null
  let docRoot = null
  if (!isList) {
    shell = document.createElement('div')
    shell.className = 'propertyEvidenceDocumentShell'
    const toolbar = document.createElement('div')
    toolbar.className = 'report-doc-toolbar'
    const printBtn = document.createElement('button')
    printBtn.type = 'button'
    printBtn.className = 'reportDocToolbarButton'
    printBtn.textContent = labels.printOrPdf || 'Print / Save as PDF…'
    printBtn.addEventListener('click', () => window.print())
    const recentModalBtn = document.createElement('button')
    recentModalBtn.type = 'button'
    recentModalBtn.className = 'reportDocToolbarButton'
    recentModalBtn.textContent = labels.recentIdsModal || 'Recent IDs…'
    recentModalBtn.addEventListener('click', () => openPropertyEvidenceRecentIdsModal(section, labels))
    toolbar.appendChild(printBtn)
    toolbar.appendChild(recentModalBtn)
    shell.appendChild(toolbar)
    docRoot = document.createElement('div')
    docRoot.className = 'report-document'
    const header = document.createElement('div')
    header.className = 'report-doc-header'
    const leftCol = document.createElement('div')
    leftCol.className = 'report-doc-header-left'
    const seal = document.createElement('div')
    seal.className = 'report-doc-seal'
    const sealImg = document.createElement('img')
    sealImg.className = 'report-doc-seal-img'
    sealImg.alt = ''
    const sealFallback = document.createElement('span')
    sealFallback.className = 'report-doc-seal-fallback'
    sealFallback.textContent = 'SARL'
    seal.appendChild(sealImg)
    seal.appendChild(sealFallback)
    const rightCol = document.createElement('div')
    rightCol.className = 'report-doc-header-right'
    const mainTitle = document.createElement('div')
    mainTitle.className = 'report-doc-main-title'
    mainTitle.textContent = labels.title || 'Property & Evidence Receipt'
    const rightTitle = document.createElement('div')
    rightTitle.className = 'report-doc-right-title'
    rightCol.appendChild(mainTitle)
    rightCol.appendChild(rightTitle)
    header.appendChild(leftCol)
    header.appendChild(seal)
    header.appendChild(rightCol)
    docRoot.appendChild(header)
    shell.appendChild(docRoot)
  }

  const title = document.createElement('div')
  title.classList.add(isList ? 'searchResponseSectionTitle' : 'title')
  title.innerHTML = labels.sectionDetailsTitle || 'Details'
  section.appendChild(title)

  // --- Subjects (multiple, with Recent IDs) ---
  const subjectsLabel = document.createElement('div')
  subjectsLabel.classList.add(isList ? 'searchResponseSectionTitle' : 'title')
  subjectsLabel.style.marginTop = '12px'
  subjectsLabel.style.fontSize = '14px'
  subjectsLabel.textContent = labels.subjectsTitle || 'Subjects (persons from whom seized)'
  section.appendChild(subjectsLabel)

  if (!isList) {
    const recentIdsRow = document.createElement('div')
    recentIdsRow.classList.add('inputWrapper', 'propertyEvidenceRecentIdsRow')
    const recentIdsLabel = document.createElement('label')
    recentIdsLabel.textContent = labels.selectFromRecentIds || 'Select from Recent IDs'
    const recentIdsList = document.createElement('div')
    recentIdsList.className = 'propertyEvidenceRecentIdsList'
    recentIdsRow.appendChild(recentIdsLabel)
    recentIdsRow.appendChild(recentIdsList)
    section.appendChild(recentIdsRow)
    ;(async function () {
      try {
        const res = await fetch('/data/recentIds')
        const recentIds = res.ok ? await res.json() : []
        recentIdsList.innerHTML = ''
        if (recentIds && recentIds.length > 0) {
          for (const entry of recentIds) {
            const btn = document.createElement('button')
            btn.type = 'button'
            btn.className = 'propertyEvidenceRecentIdItem'
            btn.dataset.pedName = entry.Name || ''
            btn.textContent = entry.Name || '—'
            if (entry.Type) {
              const span = document.createElement('span')
              span.className = 'propertyEvidenceRecentIdType'
              span.textContent = ` (${entry.Type})`
              btn.appendChild(span)
            }
            btn.addEventListener('click', function () {
              const name = (btn.dataset.pedName || '').trim()
              if (!name) return
              addSubjectToList(section, name)
            })
            recentIdsList.appendChild(btn)
          }
        } else {
          recentIdsList.textContent = labels.noRecentIds || 'No recent IDs. Collect an ID from a ped to show them here.'
        }
      } catch (e) {
        recentIdsList.textContent = labels.recentIdsError || 'Could not load Recent IDs.'
      }
    })()
  }

  const subjectsList = document.createElement('div')
  subjectsList.className = 'propertyEvidenceSubjectsList'
  section.appendChild(subjectsList)

  function addSubjectToList (parentSection, name) {
    if (!name) return
    const existingNames = Array.from(parentSection.querySelectorAll('.propertyEvidenceSubjectItem')).map(el => el.dataset.pedName).filter(Boolean)
    if (existingNames.includes(name)) return
    const wrapper = document.createElement('div')
    wrapper.classList.add('propertyEvidenceSubjectItem', 'chargeWrapper')
    wrapper.dataset.pedName = name
    const nameEl = document.createElement('div')
    nameEl.classList.add('chargeName')
    nameEl.textContent = name
    const delBtn = document.createElement('button')
    delBtn.classList.add('deleteChargeButton')
    delBtn.innerHTML = topDoc?.querySelector('.iconAccess .trash')?.innerHTML || '×'
    delBtn.addEventListener('click', () => wrapper.remove())
    wrapper.appendChild(nameEl)
    if (!isList) wrapper.appendChild(delBtn)
    parentSection.querySelector('.propertyEvidenceSubjectsList').appendChild(wrapper)
  }

  const initialSubjects = data.SubjectPedNames || (data.SubjectPedName ? [data.SubjectPedName] : [])
  for (const name of initialSubjects) {
    if (name) addSubjectToList(section, name)
  }

  if (!isList) {
    const addSubjectRow = document.createElement('div')
    addSubjectRow.classList.add('propertyEvidenceAddSubjectRow')
    const addSubjectInput = document.createElement('input')
    addSubjectInput.type = 'text'
    addSubjectInput.placeholder = labels.addSubjectPlaceholder || 'Add subject name'
    addSubjectInput.className = 'propertyEvidenceAddSubjectInput'
    const addSubjectBtn = document.createElement('button')
    addSubjectBtn.type = 'button'
    addSubjectBtn.textContent = labels.addSubject || 'Add'
    addSubjectBtn.addEventListener('click', () => {
      const name = addSubjectInput.value.trim()
      if (name) {
        addSubjectToList(section, name)
        addSubjectInput.value = ''
      }
    })
    addSubjectRow.appendChild(addSubjectInput)
    addSubjectRow.appendChild(addSubjectBtn)
    section.appendChild(addSubjectRow)
  }

  // --- Drugs seized (add pattern, with quantity) ---
  const seizureOptions = await getSeizureOptions()
  const drugTypes = seizureOptions?.drugTypes || []
  const drugQuantities = seizureOptions?.drugQuantities || []

  const drugsLabel = document.createElement('div')
  drugsLabel.classList.add('propertyEvidenceSubsectionTitle')
  drugsLabel.textContent = labels.drugsSeized || 'Drugs seized'
  section.appendChild(drugsLabel)

  if (!isList && drugTypes.length > 0) {
    const drugsAddLabel = document.createElement('div')
    drugsAddLabel.className = 'addChargesLabel'
    drugsAddLabel.style.marginBottom = '8px'
    drugsAddLabel.style.fontSize = '13px'
    drugsAddLabel.textContent = labels.addDrugsHelp || 'Select schedule, drug type, and quantity, then click Add'
    section.appendChild(drugsAddLabel)
    const drugsAddRow = document.createElement('div')
    drugsAddRow.classList.add('propertyEvidenceDrugsAddRow')
    const scheduleOrder = ['I', 'II', 'III', 'IV', 'V', 'Other', 'Paraphernalia']
    const scheduleLabels = { I: 'Schedule I', II: 'Schedule II', III: 'Schedule III', IV: 'Schedule IV', V: 'Schedule V', Other: 'Other / unspecified', Paraphernalia: 'Paraphernalia' }
    const scheduleSelect = document.createElement('select')
    scheduleSelect.className = 'propertyEvidenceDrugScheduleSelect'
    scheduleSelect.setAttribute('aria-label', labels.drugSchedule || 'Drug schedule')
    const optAll = document.createElement('option')
    optAll.value = ''
    optAll.textContent = labels.allSchedules || 'All schedules'
    scheduleSelect.appendChild(optAll)
    for (const s of scheduleOrder) {
      if (!drugTypes.some((d) => (d.schedule || 'Other') === s)) continue
      const o = document.createElement('option')
      o.value = s
      o.textContent = scheduleLabels[s] || s
      scheduleSelect.appendChild(o)
    }
    const drugTypeSelect = document.createElement('select')
    drugTypeSelect.className = 'propertyEvidenceDrugTypeSelect'
    function repopulateDrugTypeOptions () {
      const sched = scheduleSelect.value
      drugTypeSelect.innerHTML = ''
      const filtered = !sched
        ? drugTypes
        : drugTypes.filter((d) => (d.schedule || 'Other') === sched)
      const list = filtered.length > 0 ? filtered : drugTypes
      for (const opt of list) {
        const o = document.createElement('option')
        o.value = opt.id || opt.name || opt
        o.textContent = opt.name || opt.id || opt
        drugTypeSelect.appendChild(o)
      }
    }
    scheduleSelect.addEventListener('change', repopulateDrugTypeOptions)
    repopulateDrugTypeOptions()
    const quantitySelect = document.createElement('select')
    quantitySelect.className = 'propertyEvidenceQuantitySelect'
    drugQuantities.forEach(q => {
      const o = document.createElement('option')
      o.value = (q.id !== undefined && q.id !== '') ? q.id : (q.name || '')
      o.textContent = q.name || q.id || '—'
      quantitySelect.appendChild(o)
    })
    const addDrugBtn = document.createElement('button')
    addDrugBtn.type = 'button'
    addDrugBtn.textContent = labels.add || 'Add'
    addDrugBtn.addEventListener('click', () => {
      const type = drugTypeSelect.value
      if (!type) return
      const quantity = quantitySelect.value || ''
      addDrugToList(section, type, quantity)
    })
    drugsAddRow.appendChild(scheduleSelect)
    drugsAddRow.appendChild(drugTypeSelect)
    drugsAddRow.appendChild(quantitySelect)
    drugsAddRow.appendChild(addDrugBtn)
    section.appendChild(drugsAddRow)
  }

  const drugsList = document.createElement('div')
  drugsList.className = 'propertyEvidenceDrugsList optionsList'
  section.appendChild(drugsList)

  function addDrugToList (parentSection, drugType, quantity) {
    const wrapper = document.createElement('div')
    wrapper.classList.add('propertyEvidenceDrugItem', 'chargeWrapper')
    wrapper.dataset.drugType = drugType
    wrapper.dataset.quantity = quantity || ''
    const display = quantity ? `${drugType} (${quantity})` : drugType
    const nameEl = document.createElement('div')
    nameEl.classList.add('chargeName')
    nameEl.textContent = display
    const delBtn = document.createElement('button')
    delBtn.classList.add('deleteChargeButton')
    delBtn.innerHTML = topDoc?.querySelector('.iconAccess .trash')?.innerHTML || '×'
    delBtn.addEventListener('click', () => wrapper.remove())
    wrapper.appendChild(nameEl)
    if (!isList) wrapper.appendChild(delBtn)
    parentSection.querySelector('.propertyEvidenceDrugsList').appendChild(wrapper)
  }

  const initialDrugs = data.SeizedDrugs || []
  for (const d of initialDrugs) {
    const type = (d && d.DrugType) ? d.DrugType : (typeof d === 'string' ? d : '')
    const qty = (d && d.Quantity) ? d.Quantity : ''
    if (type) addDrugToList(section, type, qty)
  }

  // Legacy: SeizedDrugTypes (flat list)
  if (initialDrugs.length === 0 && data.SeizedDrugTypes && data.SeizedDrugTypes.length > 0) {
    for (const t of data.SeizedDrugTypes) {
      if (t) addDrugToList(section, t, '')
    }
  }

  // --- Firearms seized (add pattern) ---
  const firearmsLabel = document.createElement('div')
  firearmsLabel.classList.add('propertyEvidenceSubsectionTitle')
  firearmsLabel.textContent = labels.firearmsSeized || 'Firearms seized'
  section.appendChild(firearmsLabel)

  const firearmTypes = seizureOptions?.firearmTypes || []
  if (!isList && firearmTypes.length > 0) {
    const firearmsAddLabel = document.createElement('div')
    firearmsAddLabel.className = 'addChargesLabel'
    firearmsAddLabel.style.marginBottom = '8px'
    firearmsAddLabel.style.fontSize = '13px'
    firearmsAddLabel.textContent = labels.addFirearmsHelp || 'Select firearm type, then click Add'
    section.appendChild(firearmsAddLabel)
    const firearmsAddRow = document.createElement('div')
    firearmsAddRow.classList.add('propertyEvidenceFirearmsAddRow')
    const firearmSelect = document.createElement('select')
    firearmSelect.className = 'propertyEvidenceFirearmSelect'
    firearmTypes.forEach(opt => {
      const o = document.createElement('option')
      o.value = opt.id || opt.name || opt
      o.textContent = opt.name || opt.id || opt
      firearmSelect.appendChild(o)
    })
    const addFirearmBtn = document.createElement('button')
    addFirearmBtn.type = 'button'
    addFirearmBtn.textContent = labels.add || 'Add'
    addFirearmBtn.addEventListener('click', () => {
      const type = firearmSelect.value
      if (!type) return
      addFirearmToList(section, type)
    })
    firearmsAddRow.appendChild(firearmSelect)
    firearmsAddRow.appendChild(addFirearmBtn)
    section.appendChild(firearmsAddRow)
  }

  const firearmsList = document.createElement('div')
  firearmsList.className = 'propertyEvidenceFirearmsList optionsList'
  section.appendChild(firearmsList)

  function addFirearmToList (parentSection, firearmType) {
    const wrapper = document.createElement('div')
    wrapper.classList.add('propertyEvidenceFirearmItem', 'chargeWrapper')
    wrapper.dataset.firearmType = firearmType
    const nameEl = document.createElement('div')
    nameEl.classList.add('chargeName')
    nameEl.textContent = firearmType
    const delBtn = document.createElement('button')
    delBtn.classList.add('deleteChargeButton')
    delBtn.innerHTML = topDoc?.querySelector('.iconAccess .trash')?.innerHTML || '×'
    delBtn.addEventListener('click', () => wrapper.remove())
    wrapper.appendChild(nameEl)
    if (!isList) wrapper.appendChild(delBtn)
    parentSection.querySelector('.propertyEvidenceFirearmsList').appendChild(wrapper)
  }

  const initialFirearms = data.SeizedFirearmTypes || []
  for (const f of initialFirearms) {
    if (f) addFirearmToList(section, f)
  }

  // --- Other contraband ---
  const otherCell = document.createElement('div')
  otherCell.classList.add('fullWidth', 'propertyEvidenceOtherRow')
  const otherLabel = document.createElement('label')
  otherLabel.htmlFor = 'propertyEvidenceOtherInput'
  otherLabel.textContent = labels.otherContrabandNotes || 'Other contraband (optional)'
  const otherInput = document.createElement('textarea')
  otherInput.id = 'propertyEvidenceOtherInput'
  otherInput.rows = 3
  otherInput.placeholder = labels.otherContrabandPlaceholder || 'Describe other items seized'
  otherInput.value = data.OtherContrabandNotes || ''
  otherInput.disabled = isList
  otherCell.appendChild(otherLabel)
  otherCell.appendChild(otherInput)
  section.appendChild(otherCell)

  if (shell != null && docRoot != null) {
    docRoot.appendChild(section)
    const authBar = document.createElement('div')
    authBar.className = 'report-doc-auth-bar'
    authBar.textContent = labels.authorizationBlurb || 'Authorization: I authorize the prosecuting attorney to receive a copy of the related lab report. Signature: ________________  Date: __________'
    docRoot.appendChild(authBar)
    const footer = document.createElement('div')
    footer.className = 'report-doc-footer'
    docRoot.appendChild(footer)
    ;(async function applyBranding () {
      try {
        const res = await fetch('/data/reportBranding?reportType=propertyEvidence')
        const j = res.ok ? await res.json() : null
        const t = j && j.activeTemplate
        if (!t) return
        const leftEl = docRoot.querySelector('.report-doc-header-left')
        const sealEl = docRoot.querySelector('.report-doc-seal')
        const sealImgEl = sealEl && sealEl.querySelector('.report-doc-seal-img')
        const sealFbEl = sealEl && sealEl.querySelector('.report-doc-seal-fallback')
        const rightTitleEl = docRoot.querySelector('.report-doc-right-title')
        const mainTitleEl = docRoot.querySelector('.report-doc-main-title')
        if (leftEl) leftEl.textContent = (t.leftColumn || '').replace(/\r\n/g, '\n')
        if (sealFbEl) sealFbEl.textContent = String(t.centerTitle || 'LAB').trim().slice(0, 12)
        if (sealImgEl && sealFbEl) {
          const badge = t.sealBadgeFile
          if (badge && !/\.svg$/i.test(String(badge))) {
            sealImgEl.src = '/plugin/DepartmentStyling/image/' + String(badge).trim() + '?v=1'
            sealImgEl.style.display = 'block'
            sealFbEl.style.display = 'none'
          } else {
            sealImgEl.removeAttribute('src')
            sealImgEl.style.display = 'none'
            sealFbEl.style.display = 'flex'
          }
        }
        if (rightTitleEl) rightTitleEl.textContent = (t.rightTitle || '').replace(/\r\n/g, '\n')
        footer.textContent = t.footer || ''
        if (mainTitleEl && t.propertyEvidenceTitle) mainTitleEl.textContent = t.propertyEvidenceTitle
      } catch (_) {}
    })()
    return shell
  }

  return section
}

function openPropertyEvidenceRecentIdsModal (section, labels) {
  const overlay = document.createElement('div')
  overlay.className = 'report-doc-modal-overlay'
  overlay.setAttribute('role', 'dialog')
  overlay.setAttribute('aria-modal', 'true')
  const modal = document.createElement('div')
  modal.className = 'report-doc-modal'
  const h = document.createElement('h3')
  h.textContent = labels.recentIdsModalTitle || 'Recent IDs'
  const listEl = document.createElement('div')
  listEl.className = 'report-doc-modal-list'
  const actions = document.createElement('div')
  actions.className = 'report-doc-modal-actions'
  const closeBtn = document.createElement('button')
  closeBtn.type = 'button'
  closeBtn.className = 'reportDocToolbarButton'
  closeBtn.textContent = labels.close || 'Close'
  function close () {
    overlay.remove()
  }
  closeBtn.addEventListener('click', close)
  overlay.addEventListener('click', (e) => { if (e.target === overlay) close() })
  actions.appendChild(closeBtn)
  modal.appendChild(h)
  modal.appendChild(listEl)
  modal.appendChild(actions)
  overlay.appendChild(modal)
  document.body.appendChild(overlay)

  function addSubjectToList (parentSection, name) {
    if (!name) return
    const existingNames = Array.from(parentSection.querySelectorAll('.propertyEvidenceSubjectItem')).map(el => el.dataset.pedName).filter(Boolean)
    if (existingNames.includes(name)) return
    const wrapper = document.createElement('div')
    wrapper.classList.add('propertyEvidenceSubjectItem', 'chargeWrapper')
    wrapper.dataset.pedName = name
    const nameEl = document.createElement('div')
    nameEl.classList.add('chargeName')
    nameEl.textContent = name
    const delBtn = document.createElement('button')
    delBtn.classList.add('deleteChargeButton')
    delBtn.innerHTML = topDoc?.querySelector('.iconAccess .trash')?.innerHTML || '×'
    delBtn.addEventListener('click', () => wrapper.remove())
    wrapper.appendChild(nameEl)
    wrapper.appendChild(delBtn)
    parentSection.querySelector('.propertyEvidenceSubjectsList').appendChild(wrapper)
  }

  ;(async function () {
    try {
      const res = await fetch('/data/recentIds')
      const recentIds = res.ok ? await res.json() : []
      if (!recentIds || recentIds.length === 0) {
        const p = document.createElement('p')
        p.style.padding = '12px'
        p.textContent = labels.noRecentIds || 'No recent IDs.'
        listEl.appendChild(p)
        return
      }
      for (const entry of recentIds) {
        const name = (entry.Name || '').trim()
        if (!name) continue
        const row = document.createElement('button')
        row.type = 'button'
        row.className = 'report-doc-modal-row'
        row.textContent = entry.Type ? `${name} (${entry.Type})` : name
        row.addEventListener('click', () => {
          addSubjectToList(section, name)
          close()
        })
        listEl.appendChild(row)
      }
    } catch (_) {
      listEl.textContent = labels.recentIdsError || 'Could not load Recent IDs.'
    }
  })()
}
