;(async function () {
  const config = await getConfig()
  if (config.updateDomWithLanguageOnLoad) await updateDomWithLanguage('reports')
})()

let autosaveInterval = null

function readApiLocationField(loc, key) {
  if (!loc) return ''
  const camel = key.charAt(0).toLowerCase() + key.slice(1)
  const v = loc[key] ?? loc[camel]
  return v != null ? String(v) : ''
}

function serializeDraft() {
  const el = document.querySelector('.createPage .reportInformation')
  if (!el || el.children.length === 0) return
  const type = document.querySelector(
    '.createPage .typeSelector .selected'
  )?.dataset.type
  if (!type) return
  const draft = { type, timestamp: Date.now(), fields: {} }
  el.querySelectorAll('input[id]').forEach((input) => {
    draft.fields[input.id] = input.value
  })
  const notes = el.querySelector('#notesSectionTextarea')
  if (notes) draft.fields['notesSectionTextarea'] = notes.value
  const selectedStatus = el.querySelector('.statusInput .selected')
  if (selectedStatus) draft.fields['status'] = selectedStatus.dataset.status
  localStorage.setItem('mdtproReportDraft', JSON.stringify(draft))
}

function clearDraft() {
  if (autosaveInterval) {
    clearInterval(autosaveInterval)
    autosaveInterval = null
  }
  localStorage.removeItem('mdtproReportDraft')
}

function getDraft() {
  const raw = localStorage.getItem('mdtproReportDraft')
  if (!raw) return null
  try {
    const draft = JSON.parse(raw)
    if (Date.now() - draft.timestamp >= 24 * 60 * 60 * 1000) {
      localStorage.removeItem('mdtproReportDraft')
      return null
    }
    return draft
  } catch {
    return null
  }
}

function applyDraft(draft) {
  const el = document.querySelector('.createPage .reportInformation')
  if (!el || !draft) return
  for (const [id, value] of Object.entries(draft.fields)) {
    if (id === 'status') {
      const statusButtons = el.querySelectorAll('.statusInput button')
      statusButtons.forEach((btn) => {
        btn.classList.toggle('selected', btn.dataset.status === value)
      })
    } else if (id === 'notesSectionTextarea') {
      const textarea = el.querySelector('#notesSectionTextarea')
      if (textarea) textarea.value = value
    } else {
      const input = el.querySelector(`#${id}`)
      if (input) input.value = value
    }
  }
}

async function applyReportPrefill(type) {
  try {
    const raw = sessionStorage.getItem('mdtproReportPrefill')
    if (!raw) return
    const prefill = JSON.parse(raw)
    if (prefill.reportType !== type || (prefill.expires && Date.now() > prefill.expires)) return
    const d = prefill.data || {}
    const el = document.querySelector('.createPage .reportInformation')
    if (!el) return

    const pedInput = el.querySelector('#offenderSectionPedNameInput')
    const vehicleInput = el.querySelector('#offenderSectionVehicleLicensePlateInput')
    if (d.pedName && pedInput) pedInput.value = d.pedName
    if (d.vehiclePlate && vehicleInput) vehicleInput.value = d.vehiclePlate
    if (d.vehicleData) {
      const v = d.vehicleData
      if (v.LicensePlate && vehicleInput) vehicleInput.value = v.LicensePlate
      const impoundPlate = el.querySelector('#impoundSectionPlateInput')
      const impoundModel = el.querySelector('#impoundSectionModelInput')
      const impoundOwner = el.querySelector('#impoundSectionOwnerInput')
      const impoundVin = el.querySelector('#impoundSectionVinInput')
      if (impoundPlate) impoundPlate.value = v.LicensePlate || ''
      if (impoundModel) impoundModel.value = v.ModelDisplayName || v.ModelName || ''
      if (impoundOwner) impoundOwner.value = v.Owner || ''
      if (impoundVin) impoundVin.value = v.VehicleIdentificationNumber || v.VinStatus || ''
    }

    const injuredInput = el.querySelector('#injurySectionInjuredPartyInput')
    if (d.pedName && injuredInput) injuredInput.value = d.pedName

    if (d.pedName && type === 'propertyEvidence') {
      const subjectsList = el.querySelector('.propertyEvidenceSubjectsList')
      if (subjectsList && !Array.from(subjectsList.querySelectorAll('.propertyEvidenceSubjectItem')).some(item => (item.dataset.pedName || '') === d.pedName)) {
        const wrapper = document.createElement('div')
        wrapper.classList.add('propertyEvidenceSubjectItem', 'chargeWrapper')
        wrapper.dataset.pedName = d.pedName
        const nameEl = document.createElement('div')
        nameEl.classList.add('chargeName')
        nameEl.textContent = d.pedName
        const delBtn = document.createElement('button')
        delBtn.classList.add('deleteChargeButton')
        delBtn.innerHTML = (typeof topDoc !== 'undefined' && topDoc?.querySelector('.iconAccess .trash')?.innerHTML) || '×'
        delBtn.addEventListener('click', () => wrapper.remove())
        wrapper.appendChild(nameEl)
        wrapper.appendChild(delBtn)
        subjectsList.appendChild(wrapper)
      }
    }

    sessionStorage.removeItem('mdtproReportPrefill')
    const lang = await getLanguage()
    let msg
    if (sessionStorage.getItem('mdtproAttachPropertyEvidenceToArrestId')) {
      msg = lang.reports?.notifications?.prefilledFromArrest ?? 'Prefilled from arrest. Report will be attached after save.'
    } else {
      msg = d.source === 'vehicleSearch'
        ? (lang.reports?.notifications?.prefilledFromVehicleSearch || 'Prefilled from Vehicle Search')
        : (lang.reports?.notifications?.prefilledFromPersonSearch || 'Prefilled from Person Search')
    }
    if (typeof topWindow !== 'undefined' && topWindow.showNotification) {
      topWindow.showNotification(msg, 'info')
    }
  } catch (_) {}
}

function startAutosave() {
  if (autosaveInterval) clearInterval(autosaveInterval)
  autosaveInterval = setInterval(serializeDraft, 5000)
}

document
  .querySelector('.listPage .createButton')
  .addEventListener('click', async function () {
    await onCreateButtonClick()
  })

async function onCreateButtonClick() {
  const language = await getLanguage()
  if (await checkForReportOnCreatePage()) return

  document.title = language.reports.newReportTitle
  reportIsOnCreatePageBool = true

  document.querySelector('.listPage').classList.add('hidden')
  document.querySelector('.listPage .reportInformation').innerHTML = ''
  document.querySelector('.createPage').classList.remove('hidden')

  document.querySelector('.createPage .listWrapper').style.display = 'grid'
  document.querySelector('.createPage .typeSelector').classList.remove('hidden')

  let type = document.querySelector('.listPage .typeSelector .selected')?.dataset?.type
  let prefillReportType = null
  try {
    const prefillRaw = sessionStorage.getItem('mdtproReportPrefill')
    if (prefillRaw) {
      const prefill = JSON.parse(prefillRaw)
      if (prefill.reportType && ['impound', 'injury', 'trafficIncident', 'propertyEvidence'].includes(prefill.reportType)) {
        prefillReportType = prefill.reportType
      }
    }
  } catch (_) {}
  const draft = getDraft()
  if (prefillReportType) {
    type = prefillReportType
  } else if (draft) {
    type = draft.type
  }
  if (!type) type = 'incident'

  await onCreatePageTypeSelectorButtonClick(type)

  if (draft && draft.type === type) {
    applyDraft(draft)
    topWindow.showNotification(
      language.reports.notifications?.draftRestored ||
        'Draft report restored',
      'info'
    )
  }

  startAutosave()
}

document
  .querySelectorAll('.listPage .listWrapper .typeSelector button')
  .forEach((button) =>
    button.addEventListener('click', async function () {
      await onListPageTypeSelectorButtonClick(button.dataset.type)
    })
  )

async function onListPageTypeSelectorButtonClick(type) {
  const button = document.querySelector(
    `.listPage .typeSelector [data-type="${type}"]`
  )

  if (button.classList.contains('loading')) return
  showLoadingOnButton(button)
  button.blur()

  document
    .querySelectorAll('.listPage .listWrapper .typeSelector button')
    .forEach((btn) => btn.classList.remove('selected'))
  button.classList.add('selected')

  const language = await getLanguage()

  document.title = document
    .querySelector('title')
    .dataset.language.split('.')
    .reduce((acc, key) => acc?.[key], language.reports.static)

  document
    .querySelector('.listPage .listWrapper .reportInformation')
    .classList.add('hidden')
  document
    .querySelector('.listPage .listWrapper .reportsList')
    .classList.remove('hidden')

  document.querySelector('.listPage .reportsList').innerHTML = ''

  let reports = []
  try {
    const res = await fetch(`/data/${type}Reports`)
    if (res.ok) {
      const data = await res.json()
      reports = Array.isArray(data) ? data : []
    }
  } catch (_) {
    reports = []
  }
  reports = reports.slice().reverse()

  const filterElement = document.createElement('div')
  filterElement.classList.add('filter')

  const filterTitle = document.createElement('div')
  filterTitle.classList.add('title')
  filterTitle.innerHTML = language.reports.list.filter.title

  const filterInput = document.createElement('input')
  filterInput.id = 'reportsListFilterInput'
  filterInput.type = 'text'
  filterInput.placeholder = language.reports.list.filter.searchPlaceholder
  filterInput.addEventListener('input', async function () {
    await applyFilter()
  })

  const statusButtonWrapper = document.createElement('div')
  statusButtonWrapper.classList.add('buttonWrapper', 'reportsListStatusFilter')

  const defaultStatusLabels = ['Closed', 'Open', 'Canceled', 'Pending']
  const statusLabel = (i) => (language.reports?.statusMap?.[i] ?? defaultStatusLabels[i]) || defaultStatusLabels[i]

  const allStatusesButton = document.createElement('button')
  allStatusesButton.type = 'button'
  allStatusesButton.innerHTML =
    language.reports?.list?.filter?.allStatuses || 'All statuses'
  allStatusesButton.dataset.status = 'all'
  allStatusesButton.classList.add('selected')

  const closedButton = document.createElement('button')
  closedButton.type = 'button'
  closedButton.innerHTML = statusLabel(0)
  closedButton.dataset.status = 0

  const openButton = document.createElement('button')
  openButton.type = 'button'
  openButton.innerHTML = statusLabel(1)
  openButton.dataset.status = 1

  const canceledButton = document.createElement('button')
  canceledButton.type = 'button'
  canceledButton.innerHTML = statusLabel(2)
  canceledButton.dataset.status = 2

  const pendingButton = document.createElement('button')
  pendingButton.type = 'button'
  pendingButton.innerHTML = statusLabel(3)
  pendingButton.dataset.status = 3

  statusButtonWrapper.appendChild(allStatusesButton)
  statusButtonWrapper.appendChild(closedButton)
  statusButtonWrapper.appendChild(openButton)
  statusButtonWrapper.appendChild(canceledButton)
  statusButtonWrapper.appendChild(pendingButton)

  for (const statusBtn of statusButtonWrapper.querySelectorAll('button')) {
    statusBtn.addEventListener('click', async function () {
      statusBtn.blur()
      statusButtonWrapper
        .querySelectorAll('button')
        .forEach((b) => b.classList.remove('selected'))
      statusBtn.classList.add('selected')
      await applyFilter()
    })
  }

  const dateRangeWrapper = document.createElement('div')
  dateRangeWrapper.classList.add('dateRange')

  const dateFromLabel = document.createElement('label')
  dateFromLabel.innerHTML =
    language.reports.list.filter?.dateFrom || 'From'
  const dateFromInput = document.createElement('input')
  dateFromInput.type = 'date'
  dateFromInput.id = 'reportsListFilterDateFrom'
  dateFromInput.addEventListener('change', async () => await applyFilter())

  const dateToLabel = document.createElement('label')
  dateToLabel.innerHTML =
    language.reports.list.filter?.dateTo || 'To'
  const dateToInput = document.createElement('input')
  dateToInput.type = 'date'
  dateToInput.id = 'reportsListFilterDateTo'
  dateToInput.addEventListener('change', async () => await applyFilter())

  dateRangeWrapper.appendChild(dateFromLabel)
  dateRangeWrapper.appendChild(dateFromInput)
  dateRangeWrapper.appendChild(dateToLabel)
  dateRangeWrapper.appendChild(dateToInput)

  const sortWrapper = document.createElement('div')
  sortWrapper.classList.add('buttonWrapper', 'sortWrapper')

  const sortNewest = document.createElement('button')
  sortNewest.innerHTML =
    language.reports.list.filter?.newest || 'Newest'
  sortNewest.classList.add('selected')
  sortNewest.dataset.sort = 'newest'

  const sortOldest = document.createElement('button')
  sortOldest.innerHTML =
    language.reports.list.filter?.oldest || 'Oldest'
  sortOldest.dataset.sort = 'oldest'

  sortWrapper.appendChild(sortNewest)
  sortWrapper.appendChild(sortOldest)

  for (const btn of sortWrapper.querySelectorAll('button')) {
    btn.addEventListener('click', async function () {
      btn.blur()
      sortWrapper
        .querySelectorAll('button')
        .forEach((b) => b.classList.remove('selected'))
      btn.classList.add('selected')
      await applyFilter()
    })
  }

  filterElement.appendChild(filterTitle)
  filterElement.appendChild(filterInput)
  filterElement.appendChild(dateRangeWrapper)
  filterElement.appendChild(statusButtonWrapper)
  filterElement.appendChild(sortWrapper)

  if (reports.length < 1) {
    document.querySelector('.listPage .reportsList').innerHTML +=
      language.reports.list.empty
  } else {
    document.querySelector('.listPage .reportsList').appendChild(filterElement)

    await applyFilter()
  }

  async function applyFilter() {
    const newReports = []
    const searchText = filterInput.value.toLowerCase()

    function addToNewReports(report) {
      if (!newReports.includes(report)) {
        newReports.push(report)
      }
    }
    function removeFromNewReports(report) {
      const index = newReports.indexOf(report)
      if (index > -1) newReports.splice(index, 1)
    }

    const dateFrom = dateFromInput.value
      ? new Date(dateFromInput.value)
      : null
    const dateTo = dateToInput.value
      ? new Date(dateToInput.value + 'T23:59:59')
      : null

    for (const report of reports) {
      // Date range filter
      const reportDate = new Date(report.TimeStamp)
      if (dateFrom && reportDate < dateFrom) continue
      if (dateTo && reportDate > dateTo) continue

      // Text search across fields (optional chaining for types that omit some fields, e.g. impound)
      const loc = report.Location
      const locationStr = loc ? `${loc.Postal || ''} ${loc.Street || ''} ${loc.Area || ''}`.toLowerCase() : ''
      if (
        report.OffenderPedName?.toLowerCase().includes(searchText) ||
        report.OffenderVehicleLicensePlate?.toLowerCase().includes(searchText) ||
        report.LicensePlate?.toLowerCase().includes(searchText) ||
        report.Owner?.toLowerCase().includes(searchText) ||
        report.VehicleModel?.toLowerCase().includes(searchText) ||
        report.InjuredPartyName?.toLowerCase().includes(searchText) ||
        report.InjuryType?.toLowerCase().includes(searchText) ||
        (report.Id && report.Id.toLowerCase().includes(searchText)) ||
        (report.TimeStamp && new Date(report.TimeStamp).toLocaleDateString().toLowerCase().includes(searchText)) ||
        locationStr.includes(searchText) ||
        (report.Notes && report.Notes.toLowerCase().includes(searchText)) ||
        report.ImpoundReason?.toLowerCase().includes(searchText) ||
        report.TowCompany?.toLowerCase().includes(searchText) ||
        report.ImpoundLot?.toLowerCase().includes(searchText)
      ) {
        addToNewReports(report)
      }

      // Charge name search
      if (report.Charges) {
        for (const charge of report.Charges) {
          if (charge.name?.toLowerCase().includes(searchText)) {
            addToNewReports(report)
            break
          }
        }
      }

      if (report.OffenderPedsNames) {
        for (const pedName of report.OffenderPedsNames) {
          if (pedName.toLowerCase().includes(searchText)) {
            addToNewReports(report)
            break
          }
        }
      }

      if (report.WitnessPedsNames) {
        for (const pedName of report.WitnessPedsNames) {
          if (pedName.toLowerCase().includes(searchText)) {
            addToNewReports(report)
            break
          }
        }
      }

      if (report.DriverNames) {
        for (const n of report.DriverNames) {
          if (n?.toLowerCase().includes(searchText)) { addToNewReports(report); break }
        }
      }
      if (report.VehiclePlates) {
        for (const p of report.VehiclePlates) {
          if (p?.toLowerCase().includes(searchText)) { addToNewReports(report); break }
        }
      }

      const selectedStatusBtn = statusButtonWrapper.querySelector(
        'button.selected'
      )
      const statusFilter = selectedStatusBtn?.dataset?.status
      if (
        statusFilter &&
        statusFilter !== 'all' &&
        String(report.Status) !== String(statusFilter)
      ) {
        removeFromNewReports(report)
      }
    }

    // Apply sort
    const sortDirection =
      sortWrapper.querySelector('.selected')?.dataset.sort || 'newest'
    if (sortDirection === 'oldest') {
      newReports.sort((a, b) => new Date(a.TimeStamp) - new Date(b.TimeStamp))
    } else {
      newReports.sort((a, b) => new Date(b.TimeStamp) - new Date(a.TimeStamp))
    }

    await renderReports(newReports, button.dataset.type)
  }

  hideLoadingOnButton(button)
}

async function renderReports(reports, type) {
  const language = await getLanguage()

  document
    .querySelectorAll('.listPage .reportsList .listElement')
    .forEach((el) => el.remove())

  for (const report of reports) {
    const listElement = document.createElement('div')
    listElement.classList.add('listElement')
    listElement.dataset.id = report.Id

    const infoWrapper = document.createElement('div')
    infoWrapper.classList.add('infoWrapper')

    const iDElement = document.createElement('div')
    iDElement.classList.add('id', 'idRow')
    iDElement.innerHTML = `${language.reports.list.reportId}: <span>${report.Id}</span>`
    const copyBtn = document.createElement('button')
    copyBtn.type = 'button'
    copyBtn.classList.add('copyReportIdBtn', 'listCopyBtn')
    copyBtn.textContent = language.reports?.sections?.generalInformation?.copyReportId || 'Copy'
    copyBtn.title = language.reports?.sections?.generalInformation?.copyReportId || 'Copy report ID to clipboard'
    copyBtn.addEventListener('click', async (e) => {
      e.stopPropagation()
      const id = report.Id || ''
      if (!id) return
      const ok = await copyToClipboard(id)
      if (typeof topWindow !== 'undefined' && typeof topWindow.showNotification === 'function') {
        if (ok) {
          topWindow.showNotification(language.reports?.sections?.generalInformation?.copiedToClipboard || 'Report ID copied to clipboard.', 'checkMark')
        } else {
          topWindow.showNotification(language.reports?.sections?.generalInformation?.copyFailed || 'Could not copy.', 'warning')
        }
      }
    })
    iDElement.appendChild(copyBtn)

    const dateElement = document.createElement('div')
    dateElement.innerHTML = `${language.reports.list.date}: <span>${new Date(
      report.TimeStamp
    ).toLocaleDateString()}</span>`

    const locationElement = document.createElement('div')
    const loc = report.Location
    const locationStr = loc ? `${loc.Postal || ''} ${loc.Street || ''}, ${loc.Area || ''}`.trim() || '—' : '—'
    locationElement.innerHTML = `${language.reports.list.location}: <span>${locationStr}</span>`

    const textWrapper = document.createElement('div')
    textWrapper.appendChild(iDElement)
    textWrapper.appendChild(dateElement)
    textWrapper.appendChild(locationElement)
    textWrapper.classList.add('textWrapper')

    switch (type) {
      case 'incident':
        const involvedPartiesElement = document.createElement('div')
        const involvedParties = [
          ...report.OffenderPedsNames,
          ...report.WitnessPedsNames,
        ]
        involvedPartiesElement.innerHTML = `${
          language.reports.list.involvedParties
        }: <span>${involvedParties.join(', ')}</span>`

        if (involvedParties.length > 1)
          textWrapper.appendChild(involvedPartiesElement)
        break
      case 'citation':
      case 'arrest':
        const offenderElement = document.createElement('div')
        offenderElement.innerHTML = `${language.reports.list.offender}: <span>${report.OffenderPedName}</span>`

        const vehicleElement = document.createElement('div')
        vehicleElement.innerHTML = `${language.reports.list.vehicle}: <span>${report.OffenderVehicleLicensePlate}</span>`

        if (report.OffenderPedName) textWrapper.appendChild(offenderElement)
        if (report.OffenderVehicleLicensePlate)
          textWrapper.appendChild(vehicleElement)
        // FinalAmount is set only when a citation is closed (ReportStatus.Closed = 0)
        if (type === 'citation' && report.Status == 0 && report.FinalAmount != null) {
          const finalAmountEl = document.createElement('div')
          const formattedAmount = await getCurrencyString(report.FinalAmount)
          finalAmountEl.innerHTML = `${language.reports.list.finalAmount}: <span>${formattedAmount}</span>`
          textWrapper.appendChild(finalAmountEl)
        }
        break
      case 'impound':
        const impoundVehicleEl = document.createElement('div')
        impoundVehicleEl.innerHTML = `${language.reports?.list?.vehicle || 'Vehicle'}: <span>${report.LicensePlate || '—'}${report.VehicleModel ? ` (${report.VehicleModel})` : ''}</span>`
        textWrapper.appendChild(impoundVehicleEl)
        if (report.Owner) {
          const ownerEl = document.createElement('div')
          ownerEl.innerHTML = `${language.reports?.sections?.impound?.owner || 'Owner'}: <span>${report.Owner}</span>`
          textWrapper.appendChild(ownerEl)
        }
        break
      case 'trafficIncident':
        const drivers = (report.DriverNames || []).join(', ')
        const plates = (report.VehiclePlates || []).join(', ')
        if (drivers || plates) {
          const trafficEl = document.createElement('div')
          trafficEl.innerHTML = `${language.reports?.list?.involvedParties || 'Parties'}: <span>${drivers || '—'}${plates ? ` | ${plates}` : ''}</span>`
          textWrapper.appendChild(trafficEl)
        }
        if (report.InjuryReported) {
          const injuryEl = document.createElement('div')
          injuryEl.innerHTML = `${language.reports?.sections?.trafficIncident?.injuryReported || 'Injury reported'}: <span>Yes</span>`
          textWrapper.appendChild(injuryEl)
        }
        break
      case 'injury':
        const injuredEl = document.createElement('div')
        injuredEl.innerHTML = `${language.reports?.sections?.injury?.injuredParty || 'Injured party'}: <span>${report.InjuredPartyName || '—'}</span>`
        textWrapper.appendChild(injuredEl)
        if (report.InjuryType) {
          const typeEl = document.createElement('div')
          typeEl.innerHTML = `${language.reports?.sections?.injury?.injuryType || 'Type'}: <span>${report.InjuryType}</span>`
          textWrapper.appendChild(typeEl)
        }
        break
      case 'propertyEvidence': {
        const subjectNames = report.SubjectPedNames || (report.SubjectPedName ? [report.SubjectPedName] : [])
        const subjectEl = document.createElement('div')
        subjectEl.innerHTML = `${language.reports?.sections?.propertyEvidence?.subjectsTitle || 'Subjects'}: <span>${subjectNames.length > 0 ? subjectNames.join(', ') : '—'}</span>`
        textWrapper.appendChild(subjectEl)
        const drugsStr = (report.SeizedDrugs || []).map(d => d && d.DrugType ? (d.Quantity ? `${d.DrugType} (${d.Quantity})` : d.DrugType) : '').filter(Boolean).join(', ')
        const drugsLegacy = (report.SeizedDrugTypes || []).join(', ')
        const drugs = drugsStr || drugsLegacy
        const firearms = (report.SeizedFirearmTypes || []).join(', ')
        if (drugs || firearms) {
          const seizedEl = document.createElement('div')
          seizedEl.innerHTML = `${language.reports?.sections?.propertyEvidence?.seizedSummary || 'Seized'}: <span>${[drugs, firearms].filter(Boolean).join(' | ')}</span>`
          textWrapper.appendChild(seizedEl)
        }
        break
      }
    }

    const statusElement = document.createElement('div')
    statusElement.classList.add('status')
    statusElement.dataset.status = report.Status
    const statusColor = statusColorMap[report.Status] ?? statusColorMap[3] ?? 'warning'
    statusElement.style.backgroundColor = `var(--color-${statusColor}-half)`
    statusElement.style.borderColor = `var(--color-${statusColor})`
    const defaultStatusLabels = { 0: 'Closed', 1: 'Open', 2: 'Canceled', 3: 'Pending' }
    statusElement.innerHTML = (language.reports?.statusMap?.[report.Status] ?? defaultStatusLabels[report.Status]) || 'Unknown'

    infoWrapper.appendChild(textWrapper)
    infoWrapper.appendChild(statusElement)

    const buttonWrapper = document.createElement('div')
    buttonWrapper.classList.add('buttonWrapper')

    const viewButton = document.createElement('button')
    viewButton.classList.add('viewButton')
    viewButton.innerHTML = language.reports.list.viewButton
    viewButton.addEventListener('click', async function () {
      if (viewButton.classList.contains('loading')) return
      showLoadingOnButton(viewButton)

      await renderReportInformation(report, type, true)

      hideLoadingOnButton(viewButton)
    })

    const editButton = document.createElement('button')
    editButton.classList.add('editButton')
    editButton.innerHTML = language.reports.list.editButton
    editButton.addEventListener('click', async function () {
      if (editButton.classList.contains('loading')) return
      showLoadingOnButton(editButton)

      const language = await getLanguage()
      for (const iframe of topDoc.querySelectorAll('.overlay .windows .window iframe')) {
        if (iframe.contentWindow.reportIsOnCreatePage()) {
          topWindow.showNotification(
            language.reports.notifications.createPageAlreadyOpen
          )
          return
        }
      }

      document.title = language.reports.editReportTitle
      reportIsOnCreatePageBool = true

      document
        .querySelectorAll('.createPage .typeSelector .selected')
        .forEach((el) => el.classList.remove('selected'))
      document
        .querySelector(
          `.createPage .typeSelector [data-type="${
            document.querySelector('.listPage .typeSelector .selected').dataset
              .type
          }"]`
        )
        .classList.add('selected')

      document.querySelector('.createPage .listWrapper').style.display = 'block'
      document
        .querySelector('.createPage .typeSelector')
        .classList.add('hidden')

      await renderReportInformation(report, type, false)

      hideLoadingOnButton(editButton)
    })

    buttonWrapper.appendChild(viewButton)
    buttonWrapper.appendChild(editButton)

    listElement.appendChild(infoWrapper)
    listElement.appendChild(buttonWrapper)

    document.querySelector('.listPage .reportsList').appendChild(listElement)
  }
}

async function renderReportInformation(report, type, isList) {
  const language = await getLanguage()

  const reportInformationEl = document.querySelector(
    `.${isList ? 'listPage' : 'createPage'} .listWrapper .reportInformation`
  )

  if (isList) {
    document
      .querySelector('.listPage .listWrapper .reportsList')
      .classList.add('hidden')
    reportInformationEl.classList.remove('hidden')
  } else {
    document.querySelector('.listPage').classList.add('hidden')
    document.querySelector('.createPage').classList.remove('hidden')
  }

  reportInformationEl.innerHTML = ''
  delete reportInformationEl.dataset.courtCaseNumber

  let reportBodyMount = reportInformationEl
  if (
    type !== 'propertyEvidence' &&
    typeof window.mdtproMountStandardReportDocument === 'function'
  ) {
    reportBodyMount = window.mdtproMountStandardReportDocument(
      reportInformationEl,
      type,
      { readOnly: isList }
    )
  }

  const timeStamp = new Date(report.TimeStamp)
  timeStamp.setMinutes(timeStamp.getMinutes() - timeStamp.getTimezoneOffset())

  const generalInformation = {
    reportId: report.Id,
    status: report.Status,
    timeStamp: timeStamp,
    ...(type === 'arrest' && report.CourtCaseNumber ? { courtCaseNumber: report.CourtCaseNumber } : {})
  }

  const officerInformation = report.OfficerInformation

  const location = report.Location

  reportBodyMount.appendChild(
    await getGeneralInformationSection(generalInformation, isList, type)
  )
  reportBodyMount.appendChild(
    await getOfficerInformationSection(officerInformation, isList)
  )
  reportBodyMount.appendChild(await getLocationSection(location, isList))

  switch (type) {
    case 'incident':
      if (!isList || report.OffenderPedsNames.length > 0) {
        reportBodyMount.appendChild(
          await getMultipleNameInputsSection(
            language.reports.sections.incident.titleOffenders,
            language.reports.sections.incident.labelOffenders,
            language.reports.sections.incident.addOffender,
            language.reports.sections.incident.removeOffender,
            isList,
            report.OffenderPedsNames
          )
        )
      }
      if (!isList || report.WitnessPedsNames.length > 0) {
        reportBodyMount.appendChild(
          await getMultipleNameInputsSection(
            language.reports.sections.incident.titleWitnesses,
            language.reports.sections.incident.labelWitnesses,
            language.reports.sections.incident.addWitness,
            language.reports.sections.incident.removeWitness,
            isList,
            report.WitnessPedsNames
          )
        )
      }
      break
    case 'citation':
    case 'arrest':
      const canEditCharges = !isList || report.canEditCitationArrest
      reportBodyMount.appendChild(
        await getOffenderSection(
          {
            pedName: report.OffenderPedName,
            vehicleLicensePlate: report.OffenderVehicleLicensePlate,
          },
          isList,
          canEditCharges
        )
      )
      if (canEditCharges || (report.Charges && report.Charges.length > 0)) {
        reportBodyMount.appendChild(
          await getCitationArrestSection(type, isList, report.Charges || [])
        )
      }
      if (type === 'arrest' && report.CourtCaseNumber)
        reportInformationEl.dataset.courtCaseNumber = report.CourtCaseNumber
      if (type === 'arrest') {
        reportInformationEl.dataset.attachedReportIds = JSON.stringify(report.AttachedReportIds || [])
        reportInformationEl.dataset.documentedDrugs = String(!!report.DocumentedDrugs)
        reportInformationEl.dataset.documentedFirearms = String(!!report.DocumentedFirearms)
      }
      if (type === 'arrest' && (!isList || (report.UseOfForce && report.UseOfForce.Type))) {
        reportBodyMount.appendChild(
          await getUseOfForceSection(report.UseOfForce || {}, isList)
        )
      }
      if (type === 'arrest') {
        reportBodyMount.appendChild(
          await getEvidenceSeizedSection(report, isList)
        )
      }
      if (type === 'arrest' && !isList) {
        reportBodyMount.appendChild(
          await getArrestAttachedReportsSection(report)
        )
      }
      break
    case 'impound':
      reportBodyMount.appendChild(
        await getImpoundSection(
          {
            LicensePlate: report.LicensePlate,
            VehicleModel: report.VehicleModel,
            Owner: report.Owner,
            Vin: report.Vin,
            ImpoundReason: report.ImpoundReason,
            TowCompany: report.TowCompany,
            ImpoundLot: report.ImpoundLot
          },
          isList
        )
      )
      break
    case 'trafficIncident':
      reportBodyMount.appendChild(
        await getTrafficIncidentSection(
          {
            DriverNames: report.DriverNames || [],
            PassengerNames: report.PassengerNames || [],
            PedestrianNames: report.PedestrianNames || [],
            VehiclePlates: report.VehiclePlates || [],
            VehicleModels: report.VehicleModels || [],
            InjuryReported: report.InjuryReported || false,
            InjuryDetails: report.InjuryDetails,
            CollisionType: report.CollisionType
          },
          isList
        )
      )
      break
    case 'injury':
      reportBodyMount.appendChild(
        await getInjurySection(
          {
            InjuredPartyName: report.InjuredPartyName,
            InjuryType: report.InjuryType,
            Severity: report.Severity,
            Treatment: report.Treatment,
            IncidentContext: report.IncidentContext,
            LinkedReportId: report.LinkedReportId
          },
          isList
        )
      )
      break
    case 'propertyEvidence':
      reportBodyMount.appendChild(
        await getPropertyEvidenceSection(
          {
            SubjectPedNames: report.SubjectPedNames || [],
            SubjectPedName: report.SubjectPedName,
            SeizedDrugs: report.SeizedDrugs || [],
            SeizedDrugTypes: report.SeizedDrugTypes || [],
            SeizedFirearmTypes: report.SeizedFirearmTypes || [],
            OtherContrabandNotes: report.OtherContrabandNotes
          },
          isList
        )
      )
      break
  }

  reportBodyMount.appendChild(await getNotesSection(report.Notes, isList))
}

/**
 * Arrest report: Seized contraband from attached Property and Evidence Receipt (PER) reports.
 * Audits PER reports attached to this arrest and lists all items (drugs, firearms, other).
 * @param {object} report - Arrest report with AttachedReportIds
 * @param {boolean} isList - If true, show read-only
 * @returns {Promise<HTMLElement>}
 */
async function getEvidenceSeizedSection(report, isList) {
  const language = await getLanguage()
  const labels = language.reports?.sections?.arrest || {}
  const section = document.createElement('div')
  section.className = 'section evidenceSeizedSection'
  section.dataset.title = labels.evidenceSeized || 'Evidence seized'
  const title = document.createElement('div')
  title.className = 'sectionTitle'
  title.textContent = section.dataset.title
  section.appendChild(title)

  const attachedIds = report.AttachedReportIds || []
  let items = []
  if (attachedIds.length > 0) {
    try {
      const res = await fetch('/data/reportSummaries', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(attachedIds)
      })
      if (res.ok) {
        const summaries = await res.json()
        for (const s of summaries) {
          if (s && s.type === 'propertyEvidence' && Array.isArray(s.items)) {
            for (const item of s.items) {
              if (item && typeof item === 'string') items.push(item)
            }
          }
        }
      }
    } catch (_) {}
  }

  if (items.length > 0) {
    const listEl = document.createElement('ul')
    listEl.className = 'evidenceSeizedItemsList'
    for (const item of items) {
      const li = document.createElement('li')
      li.textContent = item
      listEl.appendChild(li)
    }
    section.appendChild(listEl)
  } else {
    const help = document.createElement('p')
    help.className = 'evidenceSeizedHelp'
    help.textContent = labels.evidenceSeizedHelp ?? 'Attach a Property and Evidence Receipt report below to document seized drugs and firearms. Items from attached PER reports appear here.'
    section.appendChild(help)
  }
  return section
}

/**
 * Updates dataset and adds a row for an attached report. Used for both API and local (draft) attach.
 * @param {HTMLElement} listWrap - List container
 * @param {HTMLElement} reportInfoEl - Report info element with dataset.attachedReportIds
 * @param {object} report - Arrest report
 * @param {string} reportId - Report ID to add
 * @param {object} summary - Optional { typeLabel, date, subtitle } for display
 * @param {object} language - Language strings
 * @param {Function} onDetach - Async (row) => void, handles detach (API or local)
 */
function addAttachedReportRow(listWrap, reportInfoEl, report, reportId, summary, language, onDetach) {
  const typeLabel = summary?.typeLabel ?? '—'
  const date = summary?.date ? new Date(summary.date + 'T00:00:00').toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }) : ''
  const subtitle = summary?.subtitle ? String(summary.subtitle).trim() : ''
  const row = document.createElement('div')
  row.className = 'attachedReportIdRow'
  row.dataset.reportId = reportId
  const info = document.createElement('div')
  info.className = 'attachedReportIdRowInfo'
  const idBlock = document.createElement('span')
  idBlock.className = 'attachedReportIdRowId'
  idBlock.textContent = reportId
  info.appendChild(idBlock)
  const meta = document.createElement('span')
  meta.className = 'attachedReportIdRowMeta'
  meta.textContent = [typeLabel, date, subtitle].filter(Boolean).join(' · ')
  info.appendChild(meta)
  row.appendChild(info)
  const detachBtn = document.createElement('button')
  detachBtn.type = 'button'
  detachBtn.className = 'detachReportButton'
  detachBtn.textContent = language.reports?.sections?.arrest?.detach ?? 'Detach'
  detachBtn.addEventListener('click', async function () {
    if (detachBtn.classList.contains('loading')) return
    detachBtn.classList.add('loading')
    await onDetach(row)
    detachBtn.classList.remove('loading')
  })
  row.appendChild(detachBtn)
  listWrap.appendChild(row)
}

/**
 * Local attach for draft arrests (not yet saved). Updates dataset and adds rows.
 */
async function localAttachReports(reportInfoEl, listWrap, report, reportIds, language, typeLabelByType) {
  let current = []
  try {
    current = JSON.parse(reportInfoEl.dataset.attachedReportIds || '[]')
  } catch (_) {}
  const toAdd = reportIds.filter((id) => id && id !== report.Id && !current.includes(id))
  if (toAdd.length === 0) return 0
  const nextIds = [...current, ...toAdd]
  reportInfoEl.dataset.attachedReportIds = JSON.stringify(nextIds)
  let summaries = []
  if (toAdd.length > 0) {
    try {
      const res = await fetch('/data/reportSummaries', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(toAdd) })
      if (res.ok) summaries = await res.json()
    } catch (_) {}
  }
  const doDetach = async (row) => {
    const rid = row.dataset.reportId
    let ids = []
    try { ids = JSON.parse(reportInfoEl.dataset.attachedReportIds || '[]') } catch (_) {}
    ids = ids.filter((id) => id !== rid)
    reportInfoEl.dataset.attachedReportIds = JSON.stringify(ids)
    row.remove()
  }
  toAdd.forEach((rid) => {
    const sum = Array.isArray(summaries) ? summaries.find((s) => s && s.id === rid) : null
    const typeLabel = typeLabelByType && typeLabelByType[rid] ? typeLabelByType[rid] : (sum?.typeLabel ?? '—')
    addAttachedReportRow(listWrap, reportInfoEl, report, rid, sum || { typeLabel }, language, doDetach)
  })
  return toAdd.length
}

/**
 * Section shown when editing an arrest report with Status Pending: attached report IDs,
 * attach/detach controls, and "Close arrest (submit for court)" button.
 * @param {object} report - Arrest report with Id, Status, AttachedReportIds
 * @returns {Promise<HTMLElement>}
 */
async function getArrestAttachedReportsSection(report) {
  const language = await getLanguage()
  const section = document.createElement('div')
  section.className = 'arrestAttachedReportsSection'
  section.dataset.title = language.reports?.sections?.arrest?.attachedReports ?? 'Attached reports (evidence for court)'

  const status = parseInt(report.Status, 10)
  // Open (1) and Pending (3) both mean "not yet closed for court" — allow attaching reports for either
  const canAttach = status === 1 || status === 3
  const hasId = report.Id != null && report.Id !== ''

  if (!hasId || !canAttach) {
    section.classList.add('hidden')
    return section
  }

  const reportInfoEl = document.querySelector('.createPage .listWrapper .reportInformation')

  const title = document.createElement('div')
  title.className = 'sectionTitle'
  title.textContent = section.dataset.title
  section.appendChild(title)

  const help = document.createElement('p')
  help.className = 'arrestAttachedReportsHelp'
  help.textContent = language.reports?.sections?.arrest?.attachedReportsHelp ?? 'Reports you attach are used as evidence. Those that directly support the case carry full weight; other attached reports still count but carry less weight (e.g. impound on a drug case, or a stolen firearm report—not ignored, just weighted less).'
  section.appendChild(help)

  const listWrap = document.createElement('div')
  listWrap.className = 'attachedReportIdsList'
  const attachedIds = report.AttachedReportIds || []

  // Fetch brief info for each attached report so we can show type, date, subtitle
  let summaries = []
  if (attachedIds.length > 0) {
    try {
      const res = await fetch('/data/reportSummaries', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(attachedIds)
      })
      if (res.ok) summaries = await res.json()
    } catch (_) {}
  }

  const isArrestNotFound = (err) => (err && (String(err.error || '').toLowerCase().includes('not found') || String(err.error || '').toLowerCase().includes('already closed')))

  attachedIds.forEach((reportId) => {
    const sum = summaries.find((s) => s.id === reportId)
    const typeLabel = sum?.typeLabel ?? '—'
    const date = sum?.date ? new Date(sum.date + 'T00:00:00').toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }) : ''
    const subtitle = sum?.subtitle ? String(sum.subtitle).trim() : ''

    const onDetach = async (row) => {
      const res = await (
        await fetch('/post/detachReportFromArrest', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ arrestReportId: report.Id, reportId })
        })
      ).text()
      if (res === 'OK') {
        const list = await (await fetch('/data/arrestReports')).json()
        const updated = list.find((r) => r.Id === report.Id)
        if (updated) await renderReportInformation(updated, 'arrest', false)
      } else {
        let err = null
        try { err = JSON.parse(res) } catch (_) {}
        if (reportInfoEl && isArrestNotFound(err)) {
          let ids = []
          try { ids = JSON.parse(reportInfoEl.dataset.attachedReportIds || '[]') } catch (_) {}
          ids = ids.filter((id) => id !== reportId)
          reportInfoEl.dataset.attachedReportIds = JSON.stringify(ids)
          row.remove()
        } else {
          topWindow.showNotification(err?.error || language.reports?.notifications?.saveError || 'Error', 'error')
        }
      }
    }
    addAttachedReportRow(listWrap, reportInfoEl, report, reportId, sum, language, onDetach)
  })
  section.appendChild(listWrap)

  const quickActionRow = document.createElement('div')
  quickActionRow.className = 'attachReportQuickActionRow'
  const createPerBtn = document.createElement('button')
  createPerBtn.type = 'button'
  createPerBtn.className = 'createPropertyEvidenceReceiptButton'
  createPerBtn.textContent = language.reports?.sections?.arrest?.createPropertyEvidenceReceipt ?? 'Create Property and Evidence Receipt'
  createPerBtn.addEventListener('click', async function () {
    sessionStorage.setItem('mdtproReportPrefill', JSON.stringify({
      reportType: 'propertyEvidence',
      data: { pedName: report.OffenderPedName || '' },
      expires: Date.now() + 5 * 60 * 1000
    }))
    sessionStorage.setItem('mdtproAttachPropertyEvidenceToArrestId', report.Id)
    document.querySelector('.createPage .listWrapper').style.display = 'grid'
    document.querySelector('.createPage .typeSelector').classList.remove('hidden')
    document.querySelectorAll('.createPage .typeSelector .selected').forEach((b) => b.classList.remove('selected'))
    const propBtn = document.querySelector('.createPage .typeSelector [data-type="propertyEvidence"]')
    if (propBtn) propBtn.classList.add('selected')
    await onCreatePageTypeSelectorButtonClick('propertyEvidence')
    document.querySelector('.listPage').classList.add('hidden')
    document.querySelector('.createPage').classList.remove('hidden')
    reportIsOnCreatePageBool = true
  })
  quickActionRow.appendChild(createPerBtn)

  const importRecentBtn = document.createElement('button')
  importRecentBtn.type = 'button'
  importRecentBtn.className = 'importRecentReportsButton'
  importRecentBtn.title = language.reports?.sections?.arrest?.importRecentReportsHelp ?? 'Attaches all reports created in the last 60 minutes.'
  importRecentBtn.textContent = language.reports?.sections?.arrest?.importRecentReports ?? 'Import recent reports'
  importRecentBtn.addEventListener('click', async function () {
    if (importRecentBtn.classList.contains('loading')) return
    importRecentBtn.classList.add('loading')
    try {
      const offenderInput = document.querySelector('.createPage #offenderSectionPedNameInput')
      const offenderName = (offenderInput?.value?.trim() || report.OffenderPedName || '').trim()
      const res = await fetch('/data/recentReports', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ withinMinutes: 60, pedName: offenderName || undefined })
      })
      if (!res.ok) throw new Error('Failed to fetch recent reports')
      const recent = await res.json()
      let currentAttached = []
      if (reportInfoEl) {
        try { currentAttached = JSON.parse(reportInfoEl.dataset.attachedReportIds || '[]') } catch (_) {}
      } else {
        currentAttached = report.AttachedReportIds || []
      }
      const reportIds = (Array.isArray(recent) ? recent : [])
        .map((r) => r && r.id)
        .filter((id) => id && id !== report.Id && !currentAttached.includes(id))
      if (reportIds.length === 0) {
        topWindow.showNotification(
          language.reports?.sections?.arrest?.importRecentReportsNone ?? 'No new recent reports to import (last 60 min).',
          'info'
        )
        importRecentBtn.classList.remove('loading')
        return
      }
      const attachRes = await fetch('/post/attachReportsToArrest', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ arrestReportId: report.Id, reportIds })
      })
      const attachData = await attachRes.json().catch(() => ({}))
      if (attachRes.ok && attachData.added > 0) {
        const list = await (await fetch('/data/arrestReports')).json()
        const updated = list.find((r) => r.Id === report.Id)
        if (updated) await renderReportInformation(updated, 'arrest', false)
        topWindow.showNotification(
          `${attachData.added} report(s) attached.`,
          'success'
        )
      } else if (reportInfoEl && !attachRes.ok && isArrestNotFound(attachData)) {
        const typeLabelByType = {}
        ;(Array.isArray(recent) ? recent : []).forEach((r) => { if (r && r.id && r.type) typeLabelByType[r.id] = (r.type === 'propertyEvidence' ? 'Property & Evidence' : r.type === 'injury' ? 'Injury' : (r.type || '').charAt(0).toUpperCase() + (r.type || '').slice(1)) })
        const added = await localAttachReports(reportInfoEl, listWrap, report, reportIds, language, typeLabelByType)
        topWindow.showNotification(
          added > 0
            ? (language.reports?.sections?.arrest?.attachReportDraftHint ?? 'Reports will be attached when you save the arrest.') + ` (${added})`
            : (language.reports?.sections?.arrest?.importRecentReportsNone ?? 'No new recent reports to import.'),
          added > 0 ? 'success' : 'info'
        )
      } else if (attachRes.ok && attachData.added === 0) {
        topWindow.showNotification(
          language.reports?.sections?.arrest?.importRecentReportsNone ?? 'No new recent reports to import.',
          'info'
        )
      } else {
        topWindow.showNotification((attachData?.error || language.reports?.notifications?.saveError) || 'Error', 'error')
      }
    } catch (e) {
      topWindow.showNotification(
        language.reports?.notifications?.saveError ?? 'Error',
        'error'
      )
    }
    importRecentBtn.classList.remove('loading')
  })
  quickActionRow.appendChild(importRecentBtn)

  section.appendChild(quickActionRow)

  const attachWrap = document.createElement('div')
  attachWrap.className = 'attachReportWrap'
  const attachInput = document.createElement('input')
  attachInput.type = 'text'
  attachInput.placeholder = language.reports?.sections?.arrest?.attachReportIdPlaceholder ?? 'Report ID (e.g. INC-25-0001, PER-25-0001)'
  attachInput.className = 'attachReportIdInput'
  const attachBtn = document.createElement('button')
  attachBtn.type = 'button'
  attachBtn.className = 'attachReportButton'
  attachBtn.textContent = language.reports?.sections?.arrest?.attachReport ?? 'Attach report'
  attachBtn.addEventListener('click', async function () {
    const reportId = (attachInput.value || '').trim()
    if (!reportId) return
    if (attachBtn.classList.contains('loading')) return
    attachBtn.classList.add('loading')
    const res = await (
      await fetch('/post/attachReportToArrest', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ arrestReportId: report.Id, reportId })
      })
    ).text()
    attachBtn.classList.remove('loading')
    if (res === 'OK') {
      attachInput.value = ''
      const list = await (await fetch('/data/arrestReports')).json()
      const updated = list.find((r) => r.Id === report.Id)
      if (updated) await renderReportInformation(updated, 'arrest', false)
    } else {
      let err = null
      try { err = JSON.parse(res) } catch (_) {}
      if (reportInfoEl && isArrestNotFound(err)) {
        const added = await localAttachReports(reportInfoEl, listWrap, report, [reportId], language, null)
        if (added > 0) {
          attachInput.value = ''
          topWindow.showNotification(
            language.reports?.sections?.arrest?.attachReportDraftHint ?? 'Report will be attached when you save the arrest.',
            'success'
          )
        } else {
          topWindow.showNotification(language.reports?.notifications?.saveError ?? 'Error', 'error')
        }
      } else {
        topWindow.showNotification((err?.error || language.reports?.notifications?.saveError) || 'Error', 'error')
      }
    }
  })
  attachWrap.appendChild(attachInput)
  attachWrap.appendChild(attachBtn)
  section.appendChild(attachWrap)

  const closeArrestBtn = document.createElement('button')
  closeArrestBtn.type = 'button'
  closeArrestBtn.className = 'closeArrestButton'
  closeArrestBtn.textContent = language.reports?.sections?.arrest?.closeArrestSubmit ?? 'Save and close (submit for court)'
  closeArrestBtn.addEventListener('click', async function () {
    if (closeArrestBtn.classList.contains('loading')) return
    const statusBtn = document.querySelector('.createPage .statusInput button[data-status="0"]')
    if (!statusBtn) return
    document.querySelectorAll('.createPage .statusInput button').forEach((b) => b.classList.remove('selected'))
    statusBtn.classList.add('selected')
    closeArrestBtn.classList.add('loading')
    const saved = await saveReport('arrest', { skipArrestCaution: true })
    closeArrestBtn.classList.remove('loading')
    if (saved) {
      topWindow.showNotification(
        language.reports?.notifications?.closeArrestSuccess ?? 'Arrest closed and submitted for court.',
        'success'
      )
    }
  })
  section.appendChild(closeArrestBtn)

  return section
}

document
  .querySelector('.createPage .cancelButton')
  .addEventListener('click', async function () {
    clearDraft()
    document.querySelector('.createPage').classList.add('hidden')
    document.querySelector('.createPage .reportInformation').innerHTML = ''
    document.querySelector('.listPage').classList.remove('hidden')
    reportIsOnCreatePageBool = false

    await onListPageTypeSelectorButtonClick(
      document.querySelector('.createPage .typeSelector .selected').dataset.type
    )
  })

document
  .querySelector('.createPage .saveButton')
  .addEventListener('click', async function () {
    await saveReport(
      document.querySelector('.createPage .typeSelector .selected').dataset.type
    )
  })

document
  .querySelectorAll('.createPage .typeSelector button')
  .forEach((button) =>
    button.addEventListener('click', async function () {
      await onCreatePageTypeSelectorButtonClick(button.dataset.type)
    })
  )

async function onCreatePageTypeSelectorButtonClick(type) {
  const button = document.querySelector(
    `.createPage .typeSelector [data-type="${type}"]`
  )

  button.blur()
  document
    .querySelectorAll('.createPage .typeSelector button')
    .forEach((btn) => btn.classList.remove('selected'))
  button.classList.add('selected')

  document.querySelector('.createPage .reportInformation').innerHTML = ''

  const language = await getLanguage()
  const config = await getConfig()
  const rawPlayerLoc = await (await fetch('/data/playerLocation')).json()
  const location = {
    Postal: readApiLocationField(rawPlayerLoc, 'Postal'),
    Street: readApiLocationField(rawPlayerLoc, 'Street'),
    Area: readApiLocationField(rawPlayerLoc, 'Area'),
    County: readApiLocationField(rawPlayerLoc, 'County'),
  }
  const officerInformation = await (
    await fetch('/data/officerInformationData')
  ).json()

  const inGameDateArr = (await (await fetch('/data/currentTime')).text()).split(
    ':'
  )
  const inGameDate = new Date()
  inGameDate.setHours(inGameDateArr[0])
  inGameDate.setMinutes(inGameDateArr[1])
  inGameDate.setSeconds(inGameDateArr[2])

  const reportId = await generateReportId(button.dataset.type)

  const fakeReport = {
    Id: reportId,
    Status: type === 'arrest' ? 3 : (type === 'citation' ? 0 : 1),
    TimeStamp: config.useInGameTime ? inGameDate : new Date(),
    OfficerInformation: officerInformation,
    Location: location,
    Notes: '',
    canEditCitationArrest: true,
  }

  await renderReportInformation(fakeReport, button.dataset.type, false)
  await applyReportPrefill(type)

  if (type === 'injury') {
    setTimeout(function () {
      const importBtn = document.querySelector('.createPage .injurySection .importInjuryFromGameBtn')
      if (importBtn && document.querySelector('#injurySectionInjuredPartyInput')?.value?.trim()) {
        importBtn.click()
      }
    }, 300)
  }

  const fillFromPriorBtn = document.createElement('button')
  fillFromPriorBtn.innerHTML =
    language.reports.create?.fillFromPrior || 'Fill from Prior Report'
  fillFromPriorBtn.classList.add('fillFromPrior')
  fillFromPriorBtn.addEventListener('click', async function () {
    if (fillFromPriorBtn.classList.contains('loading')) return
    showLoadingOnButton(fillFromPriorBtn)
    let reports = []
    try {
      const res = await fetch(`/data/${button.dataset.type}Reports`)
      if (res.ok) reports = await res.json()
    } catch (_) {}
    if (!Array.isArray(reports)) reports = []
    if (reports.length === 0) {
      topWindow.showNotification(
        language.reports.notifications?.noPriorReport ||
          'No prior reports found',
        'info'
      )
      hideLoadingOnButton(fillFromPriorBtn)
      return
    }
    const latest = reports[reports.length - 1]
    const loc = latest?.Location
    const el = document.querySelector('.createPage .reportInformation')
    const fields = {
      '#locationSectionAreaInput': readApiLocationField(loc, 'Area'),
      '#locationSectionStreetInput': readApiLocationField(loc, 'Street'),
      '#locationSectionCountyInput': readApiLocationField(loc, 'County'),
      '#locationSectionPostalInput': readApiLocationField(loc, 'Postal'),
    }
    for (const [selector, value] of Object.entries(fields)) {
      const input = el.querySelector(selector)
      if (input) input.value = value != null ? value : ''
    }
    hideLoadingOnButton(fillFromPriorBtn)
  })
  const reportInfo = document.querySelector('.createPage .reportInformation')
  reportInfo.insertBefore(fillFromPriorBtn, reportInfo.firstChild)
}

const pageLoadedEvent = new Event('pageLoaded')
document.addEventListener('DOMContentLoaded', async function () {
  let initialType = 'incident'
  try {
    const prefillRaw = sessionStorage.getItem('mdtproReportPrefill')
    if (prefillRaw) {
      const prefill = JSON.parse(prefillRaw)
      if (prefill.reportType && ['impound', 'injury', 'trafficIncident', 'propertyEvidence'].includes(prefill.reportType)) {
        initialType = prefill.reportType
        document.querySelector('.listPage .createButton')?.click()
      }
    }
  } catch (_) {}
  await onListPageTypeSelectorButtonClick(initialType)
  document.dispatchEvent(pageLoadedEvent)
})

async function generateReportId(type) {
  const config = await getConfig()
  const language = await getLanguage()
  const reports = await (await fetch(`/data/${type}Reports`)).json()
  const shortYear = new Date().getFullYear().toString().slice(-2)
  let index = 1
  for (const report of reports) {
    if (report.ShortYear == shortYear) index++
  }
  const typeMap = language.reports?.idTypeMap || {}
  const defaultPrefixes = { impound: 'IMP', trafficIncident: 'TIR', injury: 'INJ', propertyEvidence: 'PER' }
  const typePrefix = typeMap[type] ?? defaultPrefixes[type] ?? type?.slice(0, 3)?.toUpperCase() ?? 'RPT'
  let id = config.reportIdFormat
  id = id.replace('{type}', typePrefix)
  id = id.replace('{shortYear}', shortYear)
  id = id.replace('{year}', new Date().getFullYear())
  id = id.replace('{month}', new Date().getMonth() + 1)
  id = id.replace('{day}', new Date().getDate())
  id = id.replace(
    '{index}',
    index.toString().padStart(config.reportIdIndexPad, '0')
  )
  return id
}

async function saveReport(type, options = {}) {
  const language = await getLanguage()

  const el = document.querySelector('.createPage .reportInformation')
  const date = new Date(
    `${el.querySelector('#generalInformationSectionDateInput').value}T${el.querySelector('#generalInformationSectionTimeInput').value}`
  )
  const statusRaw = el.querySelector('.statusInput .selected')?.dataset?.status
  const statusNum = parseInt(statusRaw, 10)
  const generalInformation = {
    Id: el.querySelector('#generalInformationSectionReportIdInput').value,
    TimeStamp: date,
    // Numeric enum for C# ReportStatus (dataset is always a string; "0" must not stay a string in JSON)
    Status: Number.isFinite(statusNum) ? statusNum : 1,
    Notes: el.querySelector('#notesSectionTextarea').value.trim(),
    ShortYear: date.getFullYear() % 100,
  }

  if (!isValidDate(generalInformation.TimeStamp)) {
    topWindow.showNotification(
      `${language.reports.notifications.saveError} ${language.reports.notifications.invalidTimeStamp}`,
      'error',
      6000
    )
    return false
  }

  const badgeRaw = el
    .querySelector('#officerInformationSectionBadgeNumberInput')
    .value.trim()
  const officerInformation = {
    firstName: el
      .querySelector('#officerInformationSectionFirstNameInput')
      .value.trim(),
    lastName: el
      .querySelector('#officerInformationSectionLastNameInput')
      .value.trim(),
    // C# stores INTEGER; send null unless badge is all digits (avoids Json.NET 500 on "" or alphanumeric)
    badgeNumber: /^\d+$/.test(badgeRaw) ? parseInt(badgeRaw, 10) : null,
    rank: el.querySelector('#officerInformationSectionRankInput').value.trim(),
    callSign: el
      .querySelector('#officerInformationSectionCallSignInput')
      .value.trim(),
    agency: el
      .querySelector('#officerInformationSectionAgencyInput')
      .value.trim(),
  }

  const location = {
    Area: el.querySelector('#locationSectionAreaInput').value.trim(),
    Street: el.querySelector('#locationSectionStreetInput').value.trim(),
    County: el.querySelector('#locationSectionCountyInput').value.trim(),
    Postal: el.querySelector('#locationSectionPostalInput').value.trim(),
  }

  const report = {
    ...generalInformation,
    OfficerInformation: officerInformation,
    Location: location,
  }

  let response

  switch (type) {
    case 'incident':
      report.OffenderPedsNames = []
      const offenderInputs = el.querySelectorAll(
        `[data-title="${language.reports.sections.incident.titleOffenders}"] .inputWrapper > div:has(input) input`
      )
      for (const input of offenderInputs) {
        if (input.value.trim()) {
          report.OffenderPedsNames.push(input.value.trim())
        }
      }

      report.WitnessPedsNames = []
      const witnessInputs = el.querySelectorAll(
        `[data-title="${language.reports.sections.incident.titleWitnesses}"] .inputWrapper > div:has(input) input`
      )
      for (const input of witnessInputs) {
        if (input.value.trim()) {
          report.WitnessPedsNames.push(input.value.trim())
        }
      }

      response = await (
        await fetch('/post/createIncidentReport', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify(report),
        })
      ).text()
      break
    case 'citation':
      report.OffenderPedName = el
        .querySelector('#offenderSectionPedNameInput')
        .value.trim()

      if (!report.OffenderPedName) {
        return topWindow.showNotification(
          `${language.reports.notifications.saveError} ${language.reports.notifications.noOffender}`,
          'error'
        )
      }

      report.OffenderVehicleLicensePlate = el
        .querySelector('#offenderSectionVehicleLicensePlateInput')
        .value.trim()

      report.Charges = []
      for (const chargeEl of el.querySelectorAll(
        `.${type}Section .optionsList .chargeWrapper`
      )) {
        const charge = JSON.parse(chargeEl.dataset.charge)
        report.Charges.push(charge)
      }

      if (report.Charges.length < 1) {
        return topWindow.showNotification(
          `${language.reports.notifications.saveError} ${language.reports.notifications.noCharges}`,
          'error'
        )
      }

      report.CourtCaseNumber = null

      response = await (
        await fetch('/post/createCitationReport', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify(report),
        })
      ).text()
      break
    case 'arrest':
      report.OffenderPedName = el
        .querySelector('#offenderSectionPedNameInput')
        .value.trim()

      if (!report.OffenderPedName) {
        topWindow.showNotification(
          `${language.reports.notifications.saveError} ${language.reports.notifications.noOffender}`,
          'error'
        )
        return false
      }

      report.OffenderVehicleLicensePlate = el
        .querySelector('#offenderSectionVehicleLicensePlateInput')
        .value.trim()

      report.Charges = []
      for (const chargeEl of el.querySelectorAll(
        `.${type}Section .optionsList .chargeWrapper`
      )) {
        const charge = JSON.parse(chargeEl.dataset.charge)
        report.Charges.push(charge)
      }

      if (report.Charges.length < 1) {
        topWindow.showNotification(
          `${language.reports.notifications.saveError} ${language.reports.notifications.noCharges}`,
          'error'
        )
        return false
      }

      // Pending arrests must not carry a court docket (stale dataset/DB would block real case creation on close).
      report.CourtCaseNumber =
        report.Status === 0 ? el.dataset.courtCaseNumber ?? null : null
      try {
        report.AttachedReportIds = JSON.parse(el.dataset.attachedReportIds || '[]')
      } catch {
        report.AttachedReportIds = []
      }

      const uofType = el.querySelector('#useOfForceTypeSelect')?.value?.trim()
      if (uofType) {
        report.UseOfForce = {
          Type: uofType,
          TypeOther: uofType === 'Other' ? (el.querySelector('#useOfForceTypeOtherInput')?.value?.trim() || null) : null,
          Justification: el.querySelector('#useOfForceJustificationInput')?.value?.trim() || null,
          InjuryToSuspect: el.querySelector('#useOfForceInjurySuspect')?.checked === true,
          InjuryToOfficer: el.querySelector('#useOfForceInjuryOfficer')?.checked === true,
          Witnesses: el.querySelector('#useOfForceWitnessesInput')?.value?.trim() || null
        }
      }
      const drugsEl = el.querySelector('#evidenceSeizedDrugs')
      const firearmsEl = el.querySelector('#evidenceSeizedFirearms')
      report.DocumentedDrugs = drugsEl ? drugsEl.checked === true : el.dataset.documentedDrugs === 'true'
      report.DocumentedFirearms = firearmsEl ? firearmsEl.checked === true : el.dataset.documentedFirearms === 'true'

      response = await (
        await fetch('/post/createArrestReport', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify(report),
        })
      ).text()
      break
    case 'impound':
      report.PersonAtFaultName = el.querySelector('#impoundSectionPersonAtFaultInput')?.value?.trim() || null
      report.LicensePlate = el.querySelector('#impoundSectionPlateInput')?.value?.trim() || ''
      report.VehicleModel = el.querySelector('#impoundSectionModelInput')?.value?.trim() || ''
      report.Owner = el.querySelector('#impoundSectionOwnerInput')?.value?.trim() || ''
      report.Vin = el.querySelector('#impoundSectionVinInput')?.value?.trim() || ''
      report.ImpoundReason = el.querySelector('#impoundSectionReasonInput')?.value?.trim() || ''
      report.TowCompany = el.querySelector('#impoundSectionTowInput')?.value?.trim() || ''
      report.ImpoundLot = el.querySelector('#impoundSectionLotInput')?.value?.trim() || ''
      response = await (
        await fetch('/post/createImpoundReport', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(report),
        })
      ).text()
      break
    case 'trafficIncident': {
      const tiLabels = language.reports?.sections?.trafficIncident || {}
      const collectNames = (title) => {
        const section = el.querySelector(`[data-title="${title}"]`)
        if (!section) return []
        const inputs = section.querySelectorAll('.inputWrapper input[type="text"]')
        const arr = []
        inputs.forEach((inp) => { if (inp.value?.trim()) arr.push(inp.value.trim()) })
        return arr
      }
      report.DriverNames = collectNames(tiLabels.drivers || 'Drivers')
      report.PassengerNames = collectNames(tiLabels.passengers || 'Passengers')
      report.PedestrianNames = collectNames(tiLabels.pedestrians || 'Pedestrians')
      report.VehiclePlates = collectNames(tiLabels.vehicles || 'Vehicles')
      report.VehicleModels = collectNames(tiLabels.vehicleModels || 'Vehicle Models')
      report.InjuryReported = el.querySelector('#trafficIncidentInjuryCheck')?.checked === true
      report.InjuryDetails = el.querySelector('#trafficIncidentInjuryDetailsInput')?.value?.trim() || null
      report.CollisionType = el.querySelector('#trafficIncidentCollisionInput')?.value?.trim() || null
      response = await (
        await fetch('/post/createTrafficIncidentReport', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(report),
        })
      ).text()
      break
    }
    case 'propertyEvidence': {
      report.SubjectPedNames = Array.from(el.querySelectorAll('.propertyEvidenceSubjectItem')).map(item => item.dataset.pedName || '').filter(Boolean)
      report.SubjectPedName = report.SubjectPedNames.length > 0 ? report.SubjectPedNames[0] : null
      report.SeizedDrugs = Array.from(el.querySelectorAll('.propertyEvidenceDrugItem')).map(item => ({
        DrugType: item.dataset.drugType || '',
        Quantity: item.dataset.quantity || ''
      })).filter(d => d.DrugType)
      report.SeizedDrugTypes = report.SeizedDrugs.map(d => d.DrugType)
      report.SeizedFirearmTypes = Array.from(el.querySelectorAll('.propertyEvidenceFirearmItem')).map(item => item.dataset.firearmType || '').filter(Boolean)
      report.OtherContrabandNotes = el.querySelector('#propertyEvidenceOtherInput')?.value?.trim() || null
      response = await (
        await fetch('/post/createPropertyEvidenceReceiptReport', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(report)
        })
      ).text()
      break
    }
    case 'injury': {
      const injurySection = el.querySelector('.injurySection')
      report.InjuredPartyName = el.querySelector('#injurySectionInjuredPartyInput')?.value?.trim() || ''
      report.InjuryType = el.querySelector('#injurySectionInjuryTypeInput')?.value?.trim() || null
      report.Severity = el.querySelector('#injurySectionSeverityInput')?.value?.trim() || null
      report.Treatment = el.querySelector('#injurySectionTreatmentInput')?.value?.trim() || null
      report.IncidentContext = el.querySelector('#injurySectionContextInput')?.value?.trim() || null
      report.LinkedReportId = el.querySelector('#injurySectionLinkedReportInput')?.value?.trim() || null
      report.GameInjurySnapshot = injurySection?.dataset?.gameInjurySnapshot || null
      response = await (
        await fetch('/post/createInjuryReport', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(report),
        })
      ).text()
      break
    }
  }

  if (response != 'OK') {
    let msg = language.reports.notifications.saveError
    try {
      const err = JSON.parse(response)
      if (err && err.error) msg = err.error
    } catch (_) {}
    topWindow.showNotification(msg, 'error')
    return false
  }
  const attachToArrestId = sessionStorage.getItem('mdtproAttachPropertyEvidenceToArrestId')
  if (type === 'propertyEvidence' && attachToArrestId && report.Id) {
    sessionStorage.removeItem('mdtproAttachPropertyEvidenceToArrestId')
    try {
      const attachRes = await fetch('/post/attachReportToArrest', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ arrestReportId: attachToArrestId, reportId: report.Id })
      })
      const attachText = await attachRes.text()
      if (attachText === 'OK') {
        topWindow.showNotification(
          language.reports?.notifications?.savedAndAttachedToArrest ?? 'Report saved and attached to arrest.',
          'success'
        )
      } else {
        topWindow.showNotification(language.reports?.notifications?.saveSuccess, 'success')
      }
    } catch (_) {
      topWindow.showNotification(language.reports?.notifications?.saveSuccess, 'success')
    }
  } else {
    topWindow.showNotification(
      language.reports.notifications.saveSuccess,
      'success'
    )
  }

  if (type === 'arrest' && !options.skipArrestCaution) {
    await showArrestSaveCautionDialog(language)
  }

  clearDraft()

  document.querySelector('.createPage').classList.add('hidden')
  document.querySelector('.createPage .reportInformation').innerHTML = ''
  document.querySelector('.listPage').classList.remove('hidden')
  reportIsOnCreatePageBool = false

  await onListPageTypeSelectorButtonClick(type)
  return true
}

/**
 * Shows a caution/warning dialog after saving an arrest report, reminding the player
 * to attach relevant reports for court evidence. Returns a Promise that resolves when the user dismisses the dialog.
 */
function showArrestSaveCautionDialog(language) {
  return new Promise((resolve) => {
    const doc = document
    const overlay = doc.createElement('div')
    overlay.className = 'arrestCautionOverlay'
    overlay.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,0.5);display:flex;align-items:center;justify-content:center;z-index:9999;'

    const box = doc.createElement('div')
    box.className = 'arrestCautionBox'
    box.style.cssText = 'background:var(--color-background, #1a1a2e);border:2px solid var(--color-warning, #e6a800);border-radius:8px;padding:1.25rem;max-width:360px;box-shadow:0 4px 20px rgba(0,0,0,0.4);'

    const title = doc.createElement('div')
    title.className = 'arrestCautionTitle'
    title.style.cssText = 'font-weight:bold;color:var(--color-warning, #e6a800);margin-bottom:0.75rem;font-size:1.1rem;'
    title.textContent = language.reports?.notifications?.arrestSaveCautionTitle || 'Reminder'

    const message = doc.createElement('div')
    message.className = 'arrestCautionMessage'
    message.style.cssText = 'color:var(--color-text, #e0e0e0);line-height:1.45;margin-bottom:1rem;'
    message.textContent = language.reports?.notifications?.arrestSaveCautionMessage ||
      'Remember to attach relevant reports (e.g. incident, injury) to this arrest report. The arrest report alone may not be enough evidence to secure a conviction in court—this depends on the case.'

    const okBtn = doc.createElement('button')
    okBtn.className = 'arrestCautionOk'
    okBtn.textContent = 'OK'
    okBtn.style.cssText = 'background:var(--color-warning, #e6a800);color:#1a1a2e;border:none;padding:0.5rem 1.25rem;border-radius:4px;cursor:pointer;font-weight:bold;'
    okBtn.addEventListener('click', () => {
      overlay.remove()
      resolve()
    })

    box.appendChild(title)
    box.appendChild(message)
    box.appendChild(okBtn)
    overlay.appendChild(box)
    overlay.addEventListener('click', (e) => {
      if (e.target === overlay) {
        overlay.remove()
        resolve()
      }
    })
    doc.body.appendChild(overlay)
  })
}

function isValidDate(date) {
  return date instanceof Date && !isNaN(date) && !isNaN(date.getTime())
}
