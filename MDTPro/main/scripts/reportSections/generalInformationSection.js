async function getGeneralInformationSection(
  generalInformation,
  isList = false,
  reportType = ''
) {
  const language = await getLanguage()

  const title = document.createElement('div')
  title.classList.add('title')
  title.innerHTML = language.reports.sections.generalInformation.title

  const reportId = document.createElement('div')
  reportId.classList.add('reportId')
  const reportIdLabel = document.createElement('label')
  reportIdLabel.innerHTML =
    language.reports.sections.generalInformation.reportId
  reportIdLabel.htmlFor = 'generalInformationSectionReportIdInput'
  const reportIdRow = document.createElement('div')
  reportIdRow.classList.add('reportIdRow')
  const reportIdInput = document.createElement('input')
  reportIdInput.type = 'text'
  reportIdInput.value = generalInformation.reportId
  reportIdInput.id = 'generalInformationSectionReportIdInput'
  reportIdInput.disabled = true
  const copyBtn = document.createElement('button')
  copyBtn.type = 'button'
  copyBtn.classList.add('copyReportIdBtn')
  copyBtn.textContent = language.reports?.sections?.generalInformation?.copyReportId || 'Copy'
  copyBtn.title = language.reports?.sections?.generalInformation?.copyReportId || 'Copy report ID to clipboard'
  copyBtn.addEventListener('click', async () => {
    const id = generalInformation.reportId || ''
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
  reportIdRow.appendChild(reportIdInput)
  reportIdRow.appendChild(copyBtn)
  reportId.appendChild(reportIdLabel)
  reportId.appendChild(reportIdRow)

  const status = document.createElement('div')
  status.classList.add('status')
  const statusLabel = document.createElement('label')
  statusLabel.htmlFor = 'generalInformationSectionStatusInput'
  statusLabel.innerHTML = language.reports.sections.generalInformation.status
  status.appendChild(statusLabel)

  if (isList) {
    const statusInput = document.createElement('input')
    statusInput.type = 'text'
    const defaultStatusLabels = ['Closed', 'Open', 'Canceled', 'Pending']
    statusInput.value = (language.reports?.statusMap?.[generalInformation.status] ?? defaultStatusLabels[generalInformation.status]) || 'Unknown'
    statusInput.id = 'generalInformationSectionStatusInput'
    statusInput.disabled = true
    statusInput.style.color = `var(--color-${
      statusColorMap[generalInformation.status]
    })`
    status.appendChild(statusInput)
  } else {
    const defaultStatusLabels = ['Closed', 'Open', 'Canceled', 'Pending']
    const statusLabel = (i) => (language.reports?.statusMap?.[i] ?? defaultStatusLabels[i]) || defaultStatusLabels[i]
    const statusInput = document.createElement('div')
    statusInput.classList.add('statusInput')
    const statusClosed = document.createElement('button')
    statusClosed.innerHTML = statusLabel(0)
    statusClosed.classList.add('closed')
    statusClosed.dataset.status = 0
    if (generalInformation.status == 0) {
      statusClosed.classList.add('selected')
    }
    statusClosed.addEventListener('click', function () {
      statusClosed.blur()
      statusInput
        .querySelectorAll('button')
        .forEach((btn) => btn.classList.remove('selected'))
      statusClosed.classList.add('selected')
    })

    // For arrests: only Closed, Pending, Canceled (no "Open" — Pending = can attach reports, not yet submitted to court)
    const statusOpen = document.createElement('button')
    statusOpen.innerHTML = statusLabel(1)
    statusOpen.classList.add('open')
    statusOpen.dataset.status = 1
    if (generalInformation.status == 1) {
      statusOpen.classList.add('selected')
    }
    statusOpen.addEventListener('click', function () {
      statusOpen.blur()
      statusInput
        .querySelectorAll('button')
        .forEach((btn) => btn.classList.remove('selected'))
      statusOpen.classList.add('selected')
    })
    if (reportType !== 'arrest') {
      statusInput.appendChild(statusClosed)
      statusInput.appendChild(statusOpen)
    }

    const statusCanceled = document.createElement('button')
    statusCanceled.innerHTML = statusLabel(2)
    statusCanceled.classList.add('canceled')
    statusCanceled.dataset.status = 2
    if (generalInformation.status == 2) {
      statusCanceled.classList.add('selected')
    }
    statusCanceled.addEventListener('click', function () {
      statusCanceled.blur()
      statusInput
        .querySelectorAll('button')
        .forEach((btn) => btn.classList.remove('selected'))
      statusCanceled.classList.add('selected')
    })

    if (reportType === 'arrest') {
      statusInput.appendChild(statusClosed)
      // Pending = not yet closed for court; can attach reports. Hide if arrest already has a court case.
      const statusPending = document.createElement('button')
      statusPending.innerHTML = statusLabel(3)
      statusPending.classList.add('pending')
      statusPending.dataset.status = 3
      if (generalInformation.status == 3 || generalInformation.status == 1) {
        statusPending.classList.add('selected')
      }
      statusPending.addEventListener('click', function () {
        statusPending.blur()
        statusInput
          .querySelectorAll('button')
          .forEach((btn) => btn.classList.remove('selected'))
        statusPending.classList.add('selected')
      })
      if (generalInformation.courtCaseNumber) {
        statusPending.style.display = 'none'
      }
      statusInput.appendChild(statusPending)
    }
    statusInput.appendChild(statusCanceled)
    status.appendChild(statusInput)
  }

  const dateMatch = generalInformation.timeStamp
    .toISOString()
    .match(/\d{4}-\d\d-\d\d/)
  const timeMatch = generalInformation.timeStamp
    .toISOString()
    .match(/\d\d:\d\d:\d\d/)

  const date = document.createElement('div')
  date.classList.add('date')
  const dateLabel = document.createElement('label')
  dateLabel.innerHTML = language.reports.sections.generalInformation.date
  dateLabel.htmlFor = 'generalInformationSectionDateInput'
  const dateInput = document.createElement('input')
  dateInput.type = 'date'
  dateInput.value = dateMatch?.[0] || null
  dateInput.id = 'generalInformationSectionDateInput'
  dateInput.autocomplete = 'off'
  dateInput.disabled = isList
  date.appendChild(dateLabel)
  date.appendChild(dateInput)

  const time = document.createElement('div')
  time.classList.add('time')
  const timeLabel = document.createElement('label')
  timeLabel.innerHTML = language.reports.sections.generalInformation.time
  timeLabel.htmlFor = 'generalInformationSectionTimeInput'
  const timeInput = document.createElement('input')
  timeInput.type = 'time'
  timeInput.step = 1
  timeInput.value = timeMatch?.[0] || ''
  timeInput.id = 'generalInformationSectionTimeInput'
  timeInput.autocomplete = 'off'
  timeInput.disabled = isList
  time.appendChild(timeLabel)
  time.appendChild(timeInput)

  const inputWrapper = document.createElement('div')
  inputWrapper.classList.add('inputWrapper')
  inputWrapper.classList.add('grid')

  inputWrapper.appendChild(reportId)
  inputWrapper.appendChild(status)
  inputWrapper.appendChild(date)
  inputWrapper.appendChild(time)

  const sectionWrapper = document.createElement('div')
  sectionWrapper.classList.add('section')

  sectionWrapper.appendChild(title)
  sectionWrapper.appendChild(inputWrapper)

  return sectionWrapper
}
