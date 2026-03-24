;(async function () {
  const language = await getLanguage()
  const config = await getConfig()
  if (config.updateDomWithLanguageOnLoad) await updateDomWithLanguage('court')

  let courtCases = await (await fetch('/data/court')).json()
  courtCases = Array.isArray(courtCases) ? courtCases : []
  const root = document.querySelector('.list')
  root.innerHTML = ''

  const controls = createControls(language)
  root.appendChild(controls.wrapper)

  const listContainer = document.createElement('div')
  listContainer.classList.add('courtCaseList')
  root.appendChild(listContainer)

  listContainer.addEventListener('click', (e) => {
    const row = e.target && e.target.closest('.courtCaseRow')
    if (!row) return
    e.preventDefault()
    const listItem = row.closest('.courtCaseListItem')
    if (listItem) toggleCourtCaseExpanded(listItem)
  })

  const state = {
    query: '',
    status: 'all',
    sort: 'updatedDesc',
  }

  const refreshCourtList = async () => {
    courtCases = await (await fetch('/data/court')).json()
    courtCases = Array.isArray(courtCases) ? courtCases : []
    await render()
  }

  const render = async () => {
    listContainer.innerHTML = ''

    const filteredCases = courtCases
      .filter((c) => c != null && typeof c === 'object')
      .filter((c) => {
        if (state.status !== 'all' && `${c.Status}` !== state.status) return false
        const query = state.query.trim().toLowerCase()
        if (!query) return true
        return (
          (c.Number || '').toLowerCase().includes(query) ||
          (c.PedName || '').toLowerCase().includes(query) ||
          (c.ReportId || '').toLowerCase().includes(query)
        )
      })
      .sort((a, b) => {
        if (state.sort === 'updatedDesc')
          return (b.LastUpdatedUtc || '').localeCompare(a.LastUpdatedUtc || '')
        if (state.sort === 'riskDesc')
          return (b.RepeatOffenderScore || 0) - (a.RepeatOffenderScore || 0)
        return (b.ShortYear || 0) - (a.ShortYear || 0)
      })

    if (filteredCases.length < 1) {
      listContainer.innerHTML = language.court.empty
      return
    }

    // Docket-style column headers
    const headerRow = document.createElement('div')
    headerRow.classList.add('courtCaseListHeader')
    headerRow.innerHTML = `
      <span class="courtCaseListHeaderExpand"></span>
      <span class="courtCaseListHeaderDefendant">${escapeHtml(language.court.defendant || 'Defendant')}</span>
      <span class="courtCaseListHeaderCaseNumber">${escapeHtml(language.court.number || 'Case')}</span>
      <span class="courtCaseListHeaderDistrict">${escapeHtml(language.court.courtDistrictCol || 'Court District')}</span>
      <span class="courtCaseListHeaderDate">${escapeHtml(language.court.dateCol || 'Date')}</span>
      <span class="courtCaseListHeaderStatus">${escapeHtml(language.court.status || 'Status')}</span>
    `
    listContainer.appendChild(headerRow)

    for (const courtCase of filteredCases) {
      listContainer.appendChild(await createCourtCaseElement(courtCase, language, refreshCourtList))
    }
  }

  controls.searchInput.addEventListener('input', () => {
    state.query = controls.searchInput.value
    render()
  })
  controls.statusSelect.addEventListener('change', () => {
    state.status = controls.statusSelect.value
    render()
  })
  controls.sortSelect.addEventListener('change', () => {
    state.sort = controls.sortSelect.value
    render()
  })

  await render()
})()

function createControls(language) {
  const wrapper = document.createElement('div')
  wrapper.classList.add('courtControls')

  const searchInput = document.createElement('input')
  searchInput.type = 'text'
  searchInput.placeholder = language.court.searchPlaceholder || 'Search case #, name, report'

  const statusSelect = document.createElement('select')
  const statusMap = language.court.statusMap || ['Pending', 'Convicted', 'Acquitted', 'Dismissed']
  statusSelect.innerHTML = `<option value="all">${language.court.allStatuses || 'All statuses'}</option>`
  statusMap.forEach((label, index) => {
    statusSelect.innerHTML += `<option value="${index}">${label}</option>`
  })

  const sortSelect = document.createElement('select')
  sortSelect.innerHTML = `
    <option value="updatedDesc">${language.court.sortUpdated || 'Recently Updated'}</option>
    <option value="riskDesc">${language.court.sortRisk || 'Highest Risk First'}</option>
    <option value="yearDesc">${language.court.sortYear || 'Newest Case Number'}</option>
  `

  wrapper.appendChild(searchInput)
  wrapper.appendChild(statusSelect)
  wrapper.appendChild(sortSelect)

  return { wrapper, searchInput, statusSelect, sortSelect }
}

async function createCourtCaseElement(courtCase, language, refreshCourtList) {
  const listItem = document.createElement('div')
  listItem.classList.add('listItem', 'courtCaseListItem')

  const statusMap = language.court.statusMap || ['Pending', 'Convicted', 'Acquitted', 'Dismissed']
  const courtStatusColorMap = { 0: 'info', 1: 'error', 2: 'success', 3: 'warning' }
  const statusLabel = statusMap[courtCase.Status] || 'Unknown'
  const statusColor = courtStatusColorMap[courtCase.Status] || 'info'

  const courtDateLabel = courtCase.Status === 0 ? (language.court.trialDate || 'Trial') : (language.court.courtDate || 'Court')
  const dateForRow = courtCase.Status === 0 && courtCase.ResolveAtUtc
    ? courtCase.ResolveAtUtc
    : courtCase.HearingDateUtc || courtCase.ResolveAtUtc || courtCase.LastUpdatedUtc || ''
  const dateFormatted = dateForRow
    ? (() => {
        try {
          const d = new Date(dateForRow)
          return Number.isNaN(d.getTime()) ? dateForRow : d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
        } catch (_) {
          return dateForRow
        }
      })()
    : '–'

  const row = document.createElement('div')
  row.classList.add('courtCaseRow')
  row.setAttribute('role', 'button')
  row.setAttribute('tabindex', '0')
  row.setAttribute('aria-expanded', 'false')
  row.innerHTML = `
    <span class="courtCaseRowExpandIcon" aria-hidden="true"></span>
    <span class="courtCaseRowName">${escapeHtml(courtCase.PedName || '–')}</span>
    <span class="courtCaseRowCaseNumber">${escapeHtml(courtCase.Number || '–')}</span>
    <span class="courtCaseRowDistrict">${escapeHtml(courtCase.CourtDistrict || '–')}</span>
    <span class="courtCaseRowDate">${courtCase.Status === 0 && (courtCase.ResolveAtUtc || courtCase.HearingDateUtc) ? escapeHtml(`${courtDateLabel}: ${dateFormatted}`) : escapeHtml(dateFormatted)}</span>
    <span class="courtCaseRowStatus courtCaseRowStatus--${statusColor}">${escapeHtml(statusLabel)}</span>
  `
  listItem.appendChild(row)

  const details = document.createElement('div')
  details.classList.add('courtCaseDetails')
  details.hidden = true
  listItem.appendChild(details)

  row.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault()
      toggleCourtCaseExpanded(listItem)
    }
  })

  const chargesSearchResponseWrapper = document.createElement('div')
  chargesSearchResponseWrapper.classList.add('searchResponseWrapper', 'chargesWrapper', 'section')

  const chargesSearchResponseTitle = document.createElement('div')
  chargesSearchResponseTitle.classList.add('searchResponseSectionTitle')
  chargesSearchResponseTitle.innerHTML = escapeHtml(language.court?.charges ?? 'Charges')
  chargesSearchResponseWrapper.appendChild(chargesSearchResponseTitle)

  const chargesInputWrapper = document.createElement('div')
  chargesInputWrapper.classList.add('inputWrapper', 'grid')

  const isResolved = courtCase.Status !== 0
  let totalFine = 0
  let totalTime = 0
  let lifeSentences = 0
  for (const charge of courtCase.Charges || []) {
    if (!charge || typeof charge !== 'object') continue
    // Outcome 1 = Convicted; legacy data (Outcome 0) on resolved cases: treat as convicted when case Status === 1
    const convicted = isResolved && (charge.Outcome === 1 || (charge.Outcome === 0 && courtCase.Status === 1))
    if (!isResolved || convicted) {
      totalFine += charge.Fine || 0
      // Resolved convicted: use SentenceDaysServed (actual imposed sentence); pending: use Time (statutory)
      const daysForTime = isResolved && convicted && charge.SentenceDaysServed != null
        ? charge.SentenceDaysServed
        : (charge.Time ?? 0)
      totalTime += typeof daysForTime === 'number' ? daysForTime : 0
      if (charge.Time === null) lifeSentences++
    }

    const chargeWrapper = document.createElement('div')
    const chargeLabel = document.createElement('label')
    chargeLabel.textContent = charge.Name || '–'
    chargeWrapper.appendChild(chargeLabel)
    let detailValue
    if (!isResolved) {
      const md = typeof charge.MinDays === 'number' ? charge.MinDays : 0
      const mxd = charge.MaxDays != null && typeof charge.MaxDays === 'number' ? charge.MaxDays : null
      const useStatutoryRange = md > 0 || (mxd != null && mxd > 0)
      detailValue = useStatutoryRange
        ? await getChargeDetailsString(charge.Fine || 0, charge.Time, { minDays: md, maxDays: mxd })
        : await getChargeDetailsString(charge.Fine || 0, charge.Time)
    } else if (convicted) {
      let imposedDays = charge.SentenceDaysServed
      if (imposedDays == null) {
        imposedDays = charge.Time != null ? charge.Time : null
      }
      detailValue = await getChargeDetailsString(charge.Fine || 0, imposedDays)
    } else {
      detailValue = await getChargeDetailsString(charge.Fine || 0, charge.Time, { hideIncarceration: true })
    }
    if (isResolved) {
      const outcomeMap = {
        1: language.court.chargeOutcomeConvicted || 'Convicted',
        2: language.court.chargeOutcomeAcquitted || 'Acquitted',
        3: language.court.chargeOutcomeDismissed || 'Dismissed'
      }
      // Legacy charges have Outcome 0; use case Status (1=Convicted, 2=Acquitted, 3=Dismissed) for display
      const displayOutcome = (charge.Outcome === 0 || charge.Outcome === undefined) ? courtCase.Status : charge.Outcome
      const outcomeLabel = outcomeMap[displayOutcome] || (language.court.chargeOutcomePending || 'Pending')
      detailValue = `${detailValue} · ${outcomeLabel}`
    }
    const chargeInput = document.createElement('input')
    chargeInput.value = detailValue
    chargeInput.type = 'text'
    chargeInput.disabled = true
    chargeWrapper.appendChild(chargeInput)
    chargesInputWrapper.appendChild(chargeWrapper)
  }

  const searchResponseWrapper = document.createElement('div')
  searchResponseWrapper.classList.add('searchResponseWrapper', 'section')

  const searchResponseTitle = document.createElement('div')
  searchResponseTitle.classList.add('searchResponseSectionTitle')
  searchResponseTitle.innerHTML = `${escapeHtml(language.court.number || 'Case')}: ${escapeHtml(courtCase.Number ?? '–')}`
  searchResponseWrapper.appendChild(searchResponseTitle)

  const inputWrapper = document.createElement('div')
  inputWrapper.classList.add('inputWrapper', 'grid')

  inputWrapper.appendChild(
    createSectionHeader(language.court.sectionCaseProfile || 'Case Profile')
  )

  // Case Timeline
  const timelineDates = [
    [language.court.hearingDate || 'Hearing Date', courtCase.HearingDateUtc],
    [language.court.createdAt || 'Created', courtCase.CreatedAtUtc],
    [language.court.lastUpdated || 'Last Updated', courtCase.LastUpdatedUtc],
    [language.court.resolveAt || 'Court Date', courtCase.ResolveAtUtc]
  ].filter(([, v]) => v)
  if (timelineDates.length > 0) {
    const timelineValues = await Promise.all(timelineDates.map(([, v]) => formatIsoDate(v)))
    const timelineSection = document.createElement('div')
    timelineSection.classList.add('courtTimelineSection')
    timelineSection.appendChild(createSectionHeader(language.court.caseTimeline || 'Case Timeline'))
    timelineDates.forEach(([label], i) => {
      const r = document.createElement('div')
      r.classList.add('courtTimelineRow')
      r.appendChild(createLabel(label))
      r.appendChild(createReadOnlyInput(timelineValues[i]))
      timelineSection.appendChild(r)
    })
    inputWrapper.appendChild(timelineSection)
  }

  const pedNameWrapper = document.createElement('div')
  pedNameWrapper.classList.add('clickable')
  pedNameWrapper.addEventListener('click', async function () {
    await openInPedSearch(courtCase.PedName)
  })
  pedNameWrapper.appendChild(createLabel(language.court.pedName))
  pedNameWrapper.appendChild(createReadOnlyInput(courtCase.PedName || ''))
  inputWrapper.appendChild(pedNameWrapper)

  const reportWrapper = document.createElement('div')
  reportWrapper.classList.add('clickable')
  reportWrapper.addEventListener('click', async function () {
    await openIdInReport(courtCase.ReportId)
  })
  reportWrapper.appendChild(createLabel(language.court.report))
  reportWrapper.appendChild(createReadOnlyInput(courtCase.ReportId || ''))
  inputWrapper.appendChild(reportWrapper)

  const attachedIds = courtCase.AttachedReportIds || []
  const hasAttachedReports = attachedIds.length > 0
  if (courtCase.Status === 0 || hasAttachedReports) {
    const attachedSection = document.createElement('div')
    attachedSection.classList.add('courtAttachedReportsSection')
    attachedSection.appendChild(
      createSectionHeader(language.court.attachedReports || 'Attached reports (evidence)')
    )
    if (courtCase.Status === 0) {
      const attachedHelp = document.createElement('p')
      attachedHelp.className = 'courtAttachedReportsHelp'
      attachedHelp.textContent = language.court.attachedReportsHelp || 'Attached reports count as evidence. Relevant ones (defendant named, or report type matches charges) carry full weight; others still count but carry less weight.'
      attachedSection.appendChild(attachedHelp)
    }
    const listWrap = document.createElement('div')
    listWrap.classList.add('courtAttachedReportIdsList')

    let reportSummaries = []
    if (attachedIds.length > 0) {
      try {
        const res = await fetch('/data/reportSummaries', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(attachedIds)
        })
        if (res.ok) {
          const data = await res.json()
          reportSummaries = Array.isArray(data) ? data : []
        }
      } catch (_) {}
    }

    const reportTypeLabels = {
      incident: language.court.reportTypeIncident || 'Incident',
      citation: language.court.reportTypeCitation || 'Citation',
      arrest: language.court.reportTypeArrest || 'Arrest',
      impound: language.court.reportTypeImpound || 'Impound',
      trafficIncident: language.court.reportTypeTrafficIncident || 'Traffic Incident',
      injury: language.court.reportTypeInjury || 'Injury',
      propertyEvidence: language.court.reportTypePropertyEvidence || 'Property & Evidence'
    }

    attachedIds.forEach((reportId) => {
      const sum = reportSummaries.find((s) => s && s.id === reportId)
      const typeLabel = sum?.type ? (reportTypeLabels[sum.type] || sum.typeLabel || '—') : '—'
      const row = document.createElement('div')
      row.classList.add('courtAttachedReportIdRow')
      const span = document.createElement('span')
      span.textContent = courtCase.Status !== 0 ? `${reportId} (${typeLabel})` : reportId
      row.appendChild(span)
      if (courtCase.Status === 0) {
        const detachBtn = document.createElement('button')
        detachBtn.type = 'button'
        detachBtn.className = 'detachCourtReportButton'
        detachBtn.textContent = language.court.detach || 'Detach'
        detachBtn.addEventListener('click', async function () {
          if (detachBtn.classList.contains('loading')) return
          detachBtn.classList.add('loading')
          const res = await (
            await fetch('/post/detachReportFromCourtCase', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ courtCaseNumber: courtCase.Number, reportId })
            })
          ).text()
          detachBtn.classList.remove('loading')
          if (res === 'OK') await refreshCourtList()
          else topWindow.showNotification(language.court.saveCaseError || 'Error', 'error')
        })
        row.appendChild(detachBtn)
      } else {
        const linkSpan = document.createElement('span')
        linkSpan.classList.add('courtAttachedReportLink')
        linkSpan.textContent = 'View'
        linkSpan.setAttribute('role', 'button')
        linkSpan.tabIndex = 0
        const reportType = sum?.type || null
        linkSpan.addEventListener('click', () => openIdInReport(reportId, reportType))
        linkSpan.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openIdInReport(reportId, reportType) } })
        row.appendChild(linkSpan)
      }
      listWrap.appendChild(row)
    })
    attachedSection.appendChild(listWrap)
    const now = new Date()
    const resolveAt = courtCase.ResolveAtUtc ? new Date(courtCase.ResolveAtUtc) : null
    const canAttach = courtCase.Status === 0 && (!resolveAt || now < resolveAt)
    if (canAttach) {
      const attachWrap = document.createElement('div')
      attachWrap.classList.add('courtAttachReportWrap')
      const attachInput = document.createElement('input')
      attachInput.type = 'text'
      attachInput.placeholder = language.court.attachReportIdPlaceholder || 'Report ID'
      attachInput.className = 'courtAttachReportIdInput'
      const attachBtn = document.createElement('button')
      attachBtn.type = 'button'
      attachBtn.className = 'courtAttachReportButton'
      attachBtn.textContent = language.court.attachReportToCase || 'Attach report to case'
      attachBtn.addEventListener('click', async function () {
        const reportId = (attachInput.value || '').trim()
        if (!reportId) return
        if (attachBtn.classList.contains('loading')) return
        attachBtn.classList.add('loading')
        const res = await (
          await fetch('/post/attachReportToCourtCase', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ courtCaseNumber: courtCase.Number, reportId })
          })
        ).text()
        attachBtn.classList.remove('loading')
        if (res === 'OK') {
          attachInput.value = ''
          await refreshCourtList()
        } else {
          let msg = language.court.saveCaseError || 'Error'
          try {
            const err = JSON.parse(res)
            if (err && err.error) msg = err.error
          } catch (_) {}
          topWindow.showNotification(msg, 'error')
        }
      })
      attachWrap.appendChild(attachInput)
      attachWrap.appendChild(attachBtn)
      attachedSection.appendChild(attachWrap)
    }
    inputWrapper.appendChild(attachedSection)
  }

  const isCaseConcluded = courtCase.Status === 1 || courtCase.Status === 2
  if (isCaseConcluded) {
    const totalFineWrapper = document.createElement('div')
    totalFineWrapper.appendChild(createLabel(language.court.totalFine))
    totalFineWrapper.appendChild(createReadOnlyInput(await getCurrencyString(totalFine)))
    inputWrapper.appendChild(totalFineWrapper)

    const totalTimeWrapper = document.createElement('div')
    totalTimeWrapper.appendChild(createLabel(language.court.totalIncarceration))
    totalTimeWrapper.appendChild(
      createReadOnlyInput(await getTotalTimeString(totalTime, lifeSentences))
    )
    if (totalTime > 0 || lifeSentences > 0) inputWrapper.appendChild(totalTimeWrapper)
  }

  const riskWrapper = document.createElement('div')
  inputWrapper.appendChild(
    createSectionHeader(language.court.sectionScoring || 'Scoring & Sentencing')
  )
  riskWrapper.appendChild(createLabel(language.court.repeatOffenderScore || 'Repeat Offender Score'))
  riskWrapper.appendChild(createReadOnlyInput(`${courtCase.RepeatOffenderScore || 0}`))
  inputWrapper.appendChild(riskWrapper)

  const multiplierWrapper = document.createElement('div')
  multiplierWrapper.appendChild(createLabel(language.court.sentenceMultiplier || 'Sentence Multiplier'))
  multiplierWrapper.appendChild(
    createReadOnlyInput(`${(courtCase.SentenceMultiplier || 1).toFixed(2)}x`)
  )
  inputWrapper.appendChild(multiplierWrapper)

  const courtDistrictWrapper = document.createElement('div')
  courtDistrictWrapper.appendChild(
    createLabel(language.court.courtDistrict || 'Court District')
  )
  courtDistrictWrapper.appendChild(createReadOnlyInput(courtCase.CourtDistrict || '-'))
  inputWrapper.appendChild(courtDistrictWrapper)

  const courtNameWrapper = document.createElement('div')
  courtNameWrapper.appendChild(createLabel(language.court.courtName || 'Court Name'))
  courtNameWrapper.appendChild(
    createReadOnlyInput(
      courtCase.CourtName
        ? `${courtCase.CourtName}${courtCase.CourtType ? ` (${courtCase.CourtType})` : ''}`
        : '-'
    )
  )
  inputWrapper.appendChild(courtNameWrapper)

  const judgeWrapper = document.createElement('div')
  judgeWrapper.appendChild(createLabel(language.court.judge || 'Judge'))
  judgeWrapper.appendChild(createReadOnlyInput(courtCase.JudgeName || '-'))
  inputWrapper.appendChild(judgeWrapper)

  const prosecutorWrapper = document.createElement('div')
  prosecutorWrapper.appendChild(createLabel(language.court.prosecutor || 'Prosecutor'))
  prosecutorWrapper.appendChild(createReadOnlyInput(courtCase.ProsecutorName || '-'))
  inputWrapper.appendChild(prosecutorWrapper)

  const defenseAttorneyWrapper = document.createElement('div')
  defenseAttorneyWrapper.appendChild(createLabel(language.court.defenseAttorney || 'Defense Attorney'))
  defenseAttorneyWrapper.appendChild(createReadOnlyInput(courtCase.DefenseAttorneyName || '-'))
  inputWrapper.appendChild(defenseAttorneyWrapper)

  const severityWrapper = document.createElement('div')
  severityWrapper.appendChild(createLabel(language.court.severityScore || 'Severity Score'))
  severityWrapper.appendChild(createReadOnlyInput(`${courtCase.SeverityScore || 0}`))
  inputWrapper.appendChild(severityWrapper)

  const evidenceScore = Math.max(0, Number(courtCase.EvidenceScore) || 0)
  const evidenceBand = evidenceScore < 35 ? 'Low' : evidenceScore < 60 ? 'Medium' : 'Strong'
  const evidenceBandLabel = evidenceBand === 'Low' ? (language.court.evidenceBandLow || 'Low') : evidenceBand === 'Medium' ? (language.court.evidenceBandMedium || 'Medium') : (language.court.evidenceBandStrong || 'Strong')
  const evidenceWrapper = document.createElement('div')
  evidenceWrapper.appendChild(createLabel(language.court.evidenceScore || 'Evidence Score'))
  evidenceWrapper.appendChild(createReadOnlyInput(`${evidenceScore} (${evidenceBandLabel})`))
  inputWrapper.appendChild(evidenceWrapper)
  if (evidenceBand === 'Low') {
    const evidenceBandNote = document.createElement('div')
    evidenceBandNote.classList.add('courtEvidenceBandNote')
    evidenceBandNote.textContent = language.court.evidenceBandLowNote || 'Limited physical evidence – case may rely on officer testimony.'
    inputWrapper.appendChild(evidenceBandNote)
  }

  const evidenceToggleWrapper = document.createElement('div')
  evidenceToggleWrapper.classList.add('evidenceToggleWrapper')
  const evidenceToggleBtn = document.createElement('button')
  evidenceToggleBtn.classList.add('evidenceToggleBtn')
  evidenceToggleBtn.innerText = language.court.viewEvidenceBtn || 'View Evidence Breakdown'
  evidenceToggleBtn.title = language.court.viewEvidenceBtn || 'View prosecution exhibits'
  evidenceToggleWrapper.appendChild(evidenceToggleBtn)
  inputWrapper.appendChild(evidenceToggleWrapper)

  const evidenceBreakdown = document.createElement('div')
  evidenceBreakdown.classList.add('evidenceBreakdown')
  evidenceBreakdown.style.display = 'none'

  const hasAnyRealEvidence = (courtCase.EvidenceHadWeapon ?? false) || (courtCase.EvidenceWasWanted ?? false) || (courtCase.EvidenceAssaultedPed ?? false) || (courtCase.EvidenceDamagedVehicle ?? false) || (courtCase.EvidenceResisted ?? false) || (courtCase.EvidenceHadDrugs ?? false) || (courtCase.EvidenceUseOfForce ?? false) || (courtCase.EvidenceWasDrunk ?? false) || (courtCase.EvidenceWasFleeing ?? false) || (courtCase.EvidenceViolatedSupervision ?? false) || (courtCase.EvidenceWasPatDown ?? false) || (courtCase.EvidenceIllegalWeapon ?? false)
  const noEvidenceNote = document.createElement('div')
  noEvidenceNote.classList.add('evidenceBreakdownNote')
  noEvidenceNote.innerText = hasAnyRealEvidence
    ? (language.court.evidenceCapturedNote || 'In-game evidence was captured for this case.')
    : (language.court.evidenceNotCapturedNote || 'No in-game evidence captured. Either the arrest was processed before evidence hooks fired, or PR/LSPDFR events did not trigger for this ped.')
  evidenceBreakdown.appendChild(noEvidenceNote)

  // Repeat offender score — not shown anywhere else in the card
  const repeatRow = document.createElement('div')
  repeatRow.classList.add('evidenceBreakdownRow')
  const repeatScore = courtCase.RepeatOffenderScore || 0
  if (repeatScore > 0) repeatRow.classList.add('evidenceBreakdownRowActive')
  const repeatLbl = document.createElement('span')
  repeatLbl.innerText = language.court.repeatOffenderScore || 'Repeat Offender Score'
  const repeatVal = document.createElement('span')
  repeatVal.classList.add('evidenceBreakdownValue')
  repeatVal.innerText = repeatScore
  repeatRow.appendChild(repeatLbl)
  repeatRow.appendChild(repeatVal)
  evidenceBreakdown.appendChild(repeatRow)

  // Sentence multiplier
  const multiplierRow = document.createElement('div')
  multiplierRow.classList.add('evidenceBreakdownRow')
  const multiplier = courtCase.SentenceMultiplier || 1
  if (multiplier > 1) multiplierRow.classList.add('evidenceBreakdownRowActive')
  const multiplierLbl = document.createElement('span')
  multiplierLbl.innerText = language.court.sentenceMultiplier || 'Sentence Multiplier'
  const multiplierVal = document.createElement('span')
  multiplierVal.classList.add('evidenceBreakdownValue')
  multiplierVal.innerText = `×${multiplier.toFixed(2)}`
  multiplierRow.appendChild(multiplierLbl)
  multiplierRow.appendChild(multiplierVal)
  evidenceBreakdown.appendChild(multiplierRow)

  // Prosecution exhibits — map evidence flags to exhibit-style blocks
  const exhibitLabels = [
    { key: 'EvidenceHadWeapon', label: (language.court.exhibitFirearm || 'Firearm recovered') + (courtCase.EvidenceFirearmTypesBreakdown?.length > 0 ? ` (${courtCase.EvidenceFirearmTypesBreakdown.join(', ')})` : '') },
    { key: 'EvidenceHadDrugs', label: (language.court.exhibitDrugs || 'Drugs') + (courtCase.EvidenceDrugTypesBreakdown?.length > 0 ? ` (${courtCase.EvidenceDrugTypesBreakdown.join(', ')})` : '') },
    { key: '_arrestReport', label: language.court.exhibitArrestReport || 'Arrest report', always: true },
    { key: 'EvidenceWasWanted', label: language.court.exhibitWarrant || 'Active warrant documentation' },
    { key: 'EvidenceAssaultedPed', label: language.court.exhibitAssault || 'Assault evidence' },
    { key: 'EvidenceDamagedVehicle', label: language.court.exhibitVehicleDamage || 'Vehicle/property damage evidence' },
    { key: 'EvidenceResisted', label: language.court.exhibitResistance || 'Resistance evidence' },
    { key: 'EvidenceUseOfForce', label: language.court.exhibitUseOfForce || 'Use of force documentation' },
    { key: 'EvidenceWasDrunk', label: language.court.exhibitIntoxication || 'Intoxication evidence' },
    { key: 'EvidenceWasFleeing', label: language.court.exhibitFleeing || 'Fleeing evidence' },
    { key: 'EvidenceViolatedSupervision', label: language.court.exhibitSupervision || 'Supervision violation' },
    { key: 'EvidenceWasPatDown', label: language.court.exhibitPatDown || 'Pat-down/search evidence' },
    { key: 'EvidenceIllegalWeapon', label: language.court.exhibitIllegalWeapon || 'Illegal weapon evidence' }
  ]
  const exhibitLetters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'
  let exhibitIndex = 0
  for (const ex of exhibitLabels) {
    const isActive = ex.always || (courtCase[ex.key] ?? false)
    if (!isActive) continue
    const letter = exhibitLetters[exhibitIndex++] || String(exhibitIndex)
    const exhibitRow = document.createElement('div')
    exhibitRow.classList.add('evidenceBreakdownRow', 'evidenceExhibitRow')
    if (ex.always || courtCase[ex.key]) exhibitRow.classList.add('evidenceBreakdownRowActive')
    const lbl = document.createElement('span')
    lbl.innerText = `Exhibit ${letter}: ${ex.label}`
    const val = document.createElement('span')
    val.classList.add('evidenceBreakdownValue')
    val.innerText = ex.always ? (courtCase.ReportId ? courtCase.ReportId : '—') : (language.court.evidenceYes || 'YES')
    exhibitRow.appendChild(lbl)
    exhibitRow.appendChild(val)
    evidenceBreakdown.appendChild(exhibitRow)
  }

  // Charges filed
  const charges = (courtCase.Charges || []).filter(c => c != null && typeof c === 'object')
  charges.forEach(charge => {
    const chargeRow = document.createElement('div')
    chargeRow.classList.add('evidenceBreakdownRow', 'evidenceBreakdownCharges')
    const chargeLbl = document.createElement('span')
    chargeLbl.innerText = charge.Name || '—'
    const chargeVal = document.createElement('span')
    chargeVal.classList.add('evidenceBreakdownValue')
    chargeVal.innerText = charge.IsArrestable ? (language.court.arrestable || 'ARRESTABLE') : (language.court.nonArrestable || 'CIVIL')
    chargeRow.appendChild(chargeLbl)
    chargeRow.appendChild(chargeVal)
    evidenceBreakdown.appendChild(chargeRow)
  })
  if (charges.length === 0) {
    const chargeRow = document.createElement('div')
    chargeRow.classList.add('evidenceBreakdownRow', 'evidenceBreakdownCharges')
    chargeRow.innerText = language.court.noCharges || 'No charges filed'
    evidenceBreakdown.appendChild(chargeRow)
  }

  inputWrapper.appendChild(evidenceBreakdown)

  evidenceToggleBtn.addEventListener('click', () => {
    const isVisible = evidenceBreakdown.style.display !== 'none'
    evidenceBreakdown.style.display = isVisible ? 'none' : 'block'
    evidenceToggleBtn.classList.toggle('active', !isVisible)
  })

  const prosecutionWrapper = document.createElement('div')
  inputWrapper.appendChild(
    createSectionHeader(language.court.sectionAdversarial || 'Trial Model')
  )
  prosecutionWrapper.appendChild(createLabelWithTooltip(language.court.prosecutionStrength || 'Prosecution Strength', language.court.prosecutionStrengthTooltip || 'Prosecution\'s estimated strength based on evidence and case factors.'))
  prosecutionWrapper.appendChild(
    createReadOnlyInput(`${(courtCase.ProsecutionStrength || 0).toFixed(1)}`)
  )
  inputWrapper.appendChild(prosecutionWrapper)

  const defenseWrapper = document.createElement('div')
  defenseWrapper.appendChild(createLabelWithTooltip(language.court.defenseStrength || 'Defense Strength', language.court.defenseStrengthTooltip || 'Defense\'s estimated strength based on representation and case factors.'))
  defenseWrapper.appendChild(
    createReadOnlyInput(`${(courtCase.DefenseStrength || 0).toFixed(1)}`)
  )
  inputWrapper.appendChild(defenseWrapper)

  const convictionChanceWrapper = document.createElement('div')
  convictionChanceWrapper.appendChild(createLabel(language.court.convictionChance || 'Conviction Probability'))
  convictionChanceWrapper.appendChild(createReadOnlyInput(`${courtCase.ConvictionChance || 0}%`))
  inputWrapper.appendChild(convictionChanceWrapper)

  const docketWrapper = document.createElement('div')
  docketWrapper.appendChild(createLabelWithTooltip(language.court.docketPressure || 'Docket Pressure', language.court.docketPressureTooltip))
  docketWrapper.appendChild(
    createReadOnlyInput(`${((courtCase.DocketPressure || 0) * 100).toFixed(0)}%`)
  )
  inputWrapper.appendChild(docketWrapper)

  const policyWrapper = document.createElement('div')
  policyWrapper.appendChild(
    createLabelWithTooltip(language.court.policyAdjustment || 'District Policy Adjustment', language.court.policyAdjustmentTooltip)
  )
  policyWrapper.appendChild(
    createReadOnlyInput(`${((courtCase.PolicyAdjustment || 0) * 100).toFixed(1)}%`)
  )
  inputWrapper.appendChild(policyWrapper)

  if (isCaseConcluded) {
    const juryWrapper = document.createElement('div')
    juryWrapper.appendChild(createLabel(language.court.jury || 'Jury'))
    const juryText = courtCase.IsJuryTrial
      ? `${courtCase.JuryVotesForConviction || 0}-${courtCase.JuryVotesForAcquittal || 0} / ${courtCase.JurySize || 0}`
      : language.court.benchTrial || 'Bench Trial'
    juryWrapper.appendChild(createReadOnlyInput(juryText))
    inputWrapper.appendChild(juryWrapper)
  }

  const pleaTooltipText = [
    language.court.pleaTooltipNotGuilty,
    language.court.pleaTooltipGuilty,
    language.court.pleaTooltipNoContest
  ].filter(Boolean).join(' ')
  const pleaWrapper = document.createElement('div')
  pleaWrapper.appendChild(createLabelWithTooltip(language.court.plea || 'Plea', pleaTooltipText || null))
  const pleaSelect = document.createElement('select')
  ;(language.court.pleaMap || ['Not Guilty', 'Guilty', 'No Contest']).forEach((pleaOption) => {
    const option = document.createElement('option')
    option.value = pleaOption
    option.textContent = pleaOption
    if ((courtCase.Plea || '').toLowerCase() === pleaOption.toLowerCase()) option.selected = true
    pleaSelect.appendChild(option)
  })
  pleaWrapper.appendChild(pleaSelect)
  inputWrapper.appendChild(pleaWrapper)

  const notesWrapper = document.createElement('div')
  inputWrapper.appendChild(
    createSectionHeader(language.court.sectionDisposition || 'Disposition')
  )
  notesWrapper.classList.add('courtNotes')
  notesWrapper.appendChild(createLabel(language.court.outcomeNotes || 'Outcome Notes'))
  const notesInput = document.createElement('textarea')
  notesInput.value = courtCase.OutcomeNotes || ''
  notesWrapper.appendChild(notesInput)
  inputWrapper.appendChild(notesWrapper)

  if (courtCase.Status !== 0) {
    const dispositionSection = document.createElement('div')
    dispositionSection.classList.add('courtDispositionSection')

    const reasoningWrapper = document.createElement('div')
    reasoningWrapper.classList.add('courtNotes', 'courtVerdictBlock')
    reasoningWrapper.appendChild(
      createLabel(language.court.outcomeReasoning || 'Verdict & Outcome Reasoning')
    )
    const reasoningInput = document.createElement('textarea')
    reasoningInput.value = typeof courtCase.OutcomeReasoning === 'string' ? courtCase.OutcomeReasoning : ''
    reasoningInput.readOnly = true
    reasoningInput.rows = 5
    reasoningWrapper.appendChild(reasoningInput)
    dispositionSection.appendChild(reasoningWrapper)

    if (courtCase.Status === 1 && courtCase.SentenceReasoning) {
      const sentencingWrapper = document.createElement('div')
      sentencingWrapper.classList.add('courtNotes', 'courtSentencingBlock')
      sentencingWrapper.appendChild(
        createLabel(language.court.sentenceReasoning || 'Sentencing Rationale')
      )
      const sentencingInput = document.createElement('textarea')
      sentencingInput.value = typeof courtCase.SentenceReasoning === 'string' ? courtCase.SentenceReasoning : ''
      sentencingInput.readOnly = true
      sentencingInput.rows = 4
      sentencingWrapper.appendChild(sentencingInput)
      dispositionSection.appendChild(sentencingWrapper)
    }

    inputWrapper.appendChild(dispositionSection)
  }

  if (courtCase.Status === 1 && courtCase.LicenseRevocations && courtCase.LicenseRevocations.length > 0) {
    const revocationsWrapper = document.createElement('div')
    revocationsWrapper.classList.add('courtNotes', 'licenseRevocations')
    revocationsWrapper.appendChild(
      createLabel(language.court.licenseRevocations || 'License Revocations Ordered')
    )
    const revocationsList = document.createElement('ul')
    revocationsList.classList.add('licenseRevocationsList')
    courtCase.LicenseRevocations.forEach((r) => {
      const li = document.createElement('li')
      li.textContent = r
      revocationsList.appendChild(li)
    })
    revocationsWrapper.appendChild(revocationsList)
    inputWrapper.appendChild(revocationsWrapper)
  }

  const statusWrapper = document.createElement('div')
  statusWrapper.classList.add('courtStatusWrapper')
  statusWrapper.appendChild(createLabel(language.court.status || 'Status'))

  const currentStatusLabel = createReadOnlyInput(statusMap[courtCase.Status] || 'Unknown')
  currentStatusLabel.style.borderColor = `var(--color-${courtStatusColorMap[courtCase.Status] || 'info'})`
  statusWrapper.appendChild(currentStatusLabel)

  // Only show Save, Dismiss and Force Resolve when case is still pending
  if (courtCase.Status === 0) {
    const statusButtonWrapper = document.createElement('div')
    statusButtonWrapper.classList.add('buttonWrapper')

    const saveCaseBtn = document.createElement('button')
    saveCaseBtn.className = 'saveCaseBtn'
    saveCaseBtn.innerText = language.court.saveCase || 'Save Plea & Notes'
    saveCaseBtn.addEventListener('click', async function () {
      if (saveCaseBtn.classList.contains('loading')) return
      showLoadingOnButton(saveCaseBtn)
      const response = await (
        await fetch('/post/updateCourtCaseStatus', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            Number: courtCase.Number,
            Status: 0,
            Plea: pleaSelect.value,
            IsJuryTrial: courtCase.IsJuryTrial,
            JurySize: courtCase.JurySize,
            JuryVotesForConviction: courtCase.JuryVotesForConviction,
            JuryVotesForAcquittal: courtCase.JuryVotesForAcquittal,
            HasPublicDefender: courtCase.HasPublicDefender,
            OutcomeNotes: notesInput.value,
            OutcomeReasoning: courtCase.OutcomeReasoning || '',
          }),
        })
      ).text()
      if (response === 'OK') {
        courtCase.Plea = pleaSelect.value
        courtCase.OutcomeNotes = notesInput.value
        topWindow.showNotification(language.court.saveCaseSuccess || 'Case updated.', 'success')
        if (typeof refreshCourtList === 'function') await refreshCourtList()
      } else {
        topWindow.showNotification(language.court.saveCaseError || 'Failed to save case.', 'error')
      }
      hideLoadingOnButton(saveCaseBtn)
    })
    statusButtonWrapper.appendChild(saveCaseBtn)

    const forceResolveBtn = document.createElement('button')
    forceResolveBtn.className = 'forceResolveBtn'
    forceResolveBtn.innerText = language.court.forceResolve || 'Force Resolve'
    forceResolveBtn.style.borderColor = 'var(--color-accent)'
    forceResolveBtn.addEventListener('click', async function () {
      if (forceResolveBtn.classList.contains('loading')) return
      forceResolveBtn.classList.add('loading')
      forceResolveBtn.disabled = true

      const stages = language.court.forceResolveStages || [
        'Submitting case to court...',
        'Prosecution and defense present...',
        'Judge deliberating...',
        'Verdict being entered...',
        'Finalizing case record...',
      ]
      const totalMs = 5000
      const stageInterval = totalMs / stages.length
      const circumference = 2 * Math.PI * 36

      const overlay = document.createElement('div')
      overlay.className = 'courtProcessingOverlay'
      overlay.innerHTML = `
        <div class="courtProcessingModal">
          <div class="courtProcessingTitle">${escapeHtml(language.court.forceResolveProcessing || 'Processing case...')}</div>
          <svg class="courtProcessingCircle" viewBox="0 0 80 80">
            <circle class="courtProcessingCircleBg" cx="40" cy="40" r="36" />
            <circle class="courtProcessingCircleProgress" cx="40" cy="40" r="36" />
          </svg>
          <div class="courtProcessingStage">${escapeHtml(stages[0])}</div>
        </div>
      `
      document.body.appendChild(overlay)
      const progressCircle = overlay.querySelector('.courtProcessingCircleProgress')
      const stageEl = overlay.querySelector('.courtProcessingStage')

      let elapsed = 0
      const tick = 50
      const progressInterval = setInterval(() => {
        elapsed += tick
        const pct = Math.min(1, elapsed / totalMs)
        const dashOffset = circumference * (1 - pct)
        progressCircle.style.strokeDashoffset = dashOffset
        const stageIdx = Math.min(stages.length - 1, Math.floor(pct * stages.length))
        stageEl.textContent = stages[stageIdx]
      }, tick)

      await new Promise((r) => setTimeout(r, totalMs))
      clearInterval(progressInterval)
      overlay.remove()

      let responseText
      try {
        const res = await fetch('/post/forceResolveCourtCase', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            Number: courtCase.Number,
            Plea: pleaSelect.value,
            OutcomeNotes: notesInput.value,
          }),
        })
        responseText = await res.text()
      } catch (_) {
        responseText = ''
      }

      forceResolveBtn.classList.remove('loading')
      forceResolveBtn.disabled = false

      if (responseText === 'OK' && typeof refreshCourtList === 'function') {
        topWindow.showNotification(language.court.forceResolveSuccess || 'Case resolved.', 'success')
        await refreshCourtList()
      } else {
        topWindow.showNotification(language.court.forceResolveError || 'Could not resolve case.', 'error')
      }
    })
    statusButtonWrapper.appendChild(forceResolveBtn)

    const dismissBtn = document.createElement('button')
    dismissBtn.innerHTML = statusMap[3] || 'Dismissed'
    dismissBtn.style.borderColor = `var(--color-${courtStatusColorMap[3]})`
    dismissBtn.addEventListener('click', async function () {
      if (dismissBtn.classList.contains('loading')) return
      showLoadingOnButton(dismissBtn)
      const response = await (
        await fetch('/post/updateCourtCaseStatus', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            Number: courtCase.Number,
            Status: 3,
            Plea: pleaSelect.value,
            IsJuryTrial: courtCase.IsJuryTrial,
            JurySize: courtCase.JurySize,
            JuryVotesForConviction: courtCase.JuryVotesForConviction,
            JuryVotesForAcquittal: courtCase.JuryVotesForAcquittal,
            HasPublicDefender: courtCase.HasPublicDefender,
            OutcomeNotes: notesInput.value,
            OutcomeReasoning: courtCase.OutcomeReasoning ?? '',
          }),
        })
      ).text()
      if (response === 'OK') {
        courtCase.Status = 3
        courtCase.Plea = pleaSelect.value
        courtCase.OutcomeNotes = notesInput.value
        currentStatusLabel.value = statusMap[3] || 'Dismissed'
        currentStatusLabel.style.borderColor = `var(--color-${courtStatusColorMap[3]})`
        statusButtonWrapper.style.display = 'none'
        topWindow.showNotification(language.court.statusUpdated || 'Court case updated', 'success')
        if (typeof refreshCourtList === 'function') await refreshCourtList()
      } else {
        topWindow.showNotification(language.court.statusUpdateError || 'Failed to update status', 'error')
      }
      hideLoadingOnButton(dismissBtn)
    })
    statusButtonWrapper.appendChild(dismissBtn)
    statusWrapper.appendChild(statusButtonWrapper)
  }

  inputWrapper.appendChild(statusWrapper)

  searchResponseWrapper.appendChild(inputWrapper)
  chargesSearchResponseWrapper.appendChild(chargesInputWrapper)
  details.appendChild(searchResponseWrapper)
  details.appendChild(chargesSearchResponseWrapper)

  return listItem
}

function escapeHtml(s) {
  if (s == null || typeof s !== 'string') return '–'
  const div = document.createElement('div')
  div.textContent = s
  return div.innerHTML
}

function toggleCourtCaseExpanded(listItem) {
  if (!listItem || !listItem.classList || !listItem.classList.contains('courtCaseListItem')) return
  const details = listItem.querySelector('.courtCaseDetails')
  const row = listItem.querySelector('.courtCaseRow')
  if (!details || !row) return
  details.hidden = !details.hidden
  row.setAttribute('aria-expanded', details.hidden ? 'false' : 'true')
  listItem.classList.toggle('courtCaseListItem--expanded', !details.hidden)
}

function createLabel(text) {
  const label = document.createElement('label')
  label.textContent = text ?? ''
  return label
}

function createLabelWithTooltip(text, tooltip) {
  const wrap = document.createElement('span')
  wrap.classList.add('courtLabelWithTooltip')
  const label = document.createElement('label')
  label.textContent = text ?? ''
  wrap.appendChild(label)
  if (tooltip) {
    const icon = document.createElement('span')
    icon.classList.add('courtTooltipIcon')
    icon.setAttribute('aria-label', tooltip)
    icon.title = tooltip
    icon.textContent = 'ⓘ'
    wrap.appendChild(icon)
  }
  return wrap
}

function createSectionHeader(text) {
  const section = document.createElement('div')
  section.classList.add('courtSectionHeader')
  section.innerText = text
  return section
}

function createReadOnlyInput(value) {
  const input = document.createElement('input')
  input.type = 'text'
  input.disabled = true
  const displayValue = (value === null || value === undefined) ? '' : String(value)
  input.value = displayValue
  if (displayValue.length > 60) input.title = displayValue
  return input
}

async function formatIsoDate(value) {
  if (!value) return '-'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '-'
  return date.toLocaleString()
}

/**
 * @param {number} fine
 * @param {number|null|undefined} time — statutory (pending) or imposed days; null = life
 * @param {{ hideIncarceration?: boolean, minDays?: number, maxDays?: number|null }} [options]
 */
async function getChargeDetailsString(fine, time, options = {}) {
  const hideIncarceration = options.hideIncarceration === true
  const minDays = options.minDays
  const maxDays = options.maxDays

  const language = await getLanguage()

  const fineFormatted = await getCurrencyString(fine)
  const fineString = `${language.court.fine}: ${fineFormatted}`
  if (hideIncarceration) return fineString

  let incarcerationPart = null
  if (typeof minDays === 'number' && (minDays > 0 || (maxDays != null && maxDays > 0))) {
    const maxV = maxDays != null ? maxDays : minDays
    if (minDays === maxV) {
      incarcerationPart = `${language.court.incarceration}: ${await convertDaysToYMD(minDays)}`
    } else {
      const minStr = await convertDaysToYMD(minDays)
      const maxStr = maxDays == null ? language.units.life : await convertDaysToYMD(maxV)
      incarcerationPart = `${language.court.incarceration}: ${minStr} - ${maxStr}`
    }
  } else if (time > 0 || time === null) {
    const timeFormatted = time === null ? language.units.life : await convertDaysToYMD(time)
    incarcerationPart = `${language.court.incarceration}: ${timeFormatted}`
  }

  return incarcerationPart ? `${fineString} | ${incarcerationPart}` : fineString
}

async function getTotalTimeString(time, lifeSentences) {
  const language = await getLanguage()

  const timeString = await convertDaysToYMD(time)
  if (lifeSentences < 1) return timeString
  if (lifeSentences == 1) return `${language.units.life} + ${timeString}`
  return `${lifeSentences}x ${language.units.life} + ${timeString}`
}
