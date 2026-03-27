/** Property and Evidence Receipt form section. Subjects (Recent IDs + multiple), drugs seized (add pattern + quantity), firearms seized (add pattern), other contraband. */
async function getPropertyEvidenceSection (data = {}, isList = false) {
  const language = await getLanguage()
  const labels = language.reports?.sections?.propertyEvidence || {}
  const section = document.createElement('div')
  section.classList.add('section', 'propertyEvidenceSection')
  if (isList) section.classList.add('searchResponseWrapper')

  const title = document.createElement('div')
  title.classList.add(isList ? 'searchResponseSectionTitle' : 'title')
  title.innerHTML = labels.title || 'Property and Evidence Details'
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

  return section
}
