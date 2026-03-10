;(async function () {
  const language = await getLanguage()
  const config = await getConfig()
  if (config.updateDomWithLanguageOnLoad) await updateDomWithLanguage('court')

  let courtCases = await (await fetch('/data/court')).json()
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
    await render()
  }

  const render = async () => {
    listContainer.innerHTML = ''

    const filteredCases = courtCases
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

  const row = document.createElement('div')
  row.classList.add('courtCaseRow')
  row.setAttribute('role', 'button')
  row.setAttribute('tabindex', '0')
  row.setAttribute('aria-expanded', 'false')
  row.innerHTML = `
    <span class="courtCaseRowExpandIcon" aria-hidden="true"></span>
    <span class="courtCaseRowName">${escapeHtml(courtCase.PedName || '–')}</span>
    <span class="courtCaseRowCaseNumber">${language.court.number || 'Case'}: ${escapeHtml(courtCase.Number || '–')}</span>
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
  chargesSearchResponseTitle.innerHTML = language.court.charges
  chargesSearchResponseWrapper.appendChild(chargesSearchResponseTitle)

  const chargesInputWrapper = document.createElement('div')
  chargesInputWrapper.classList.add('inputWrapper', 'grid')

  let totalFine = 0
  let totalTime = 0
  let lifeSentences = 0
  for (const charge of courtCase.Charges || []) {
    totalFine += charge.Fine || 0
    totalTime += charge.Time || 0
    if (charge.Time === null) lifeSentences++

    const chargeWrapper = document.createElement('div')
    const chargeLabel = document.createElement('label')
    chargeLabel.innerHTML = charge.Name
    chargeWrapper.appendChild(chargeLabel)
    const chargeInput = document.createElement('input')
    chargeInput.value = await getChargeDetailsString(charge.Fine || 0, charge.Time)
    chargeInput.type = 'text'
    chargeInput.disabled = true
    chargeWrapper.appendChild(chargeInput)
    chargesInputWrapper.appendChild(chargeWrapper)
  }

  const searchResponseWrapper = document.createElement('div')
  searchResponseWrapper.classList.add('searchResponseWrapper', 'section')

  const searchResponseTitle = document.createElement('div')
  searchResponseTitle.classList.add('searchResponseSectionTitle')
  searchResponseTitle.innerHTML = `${language.court.number}: ${courtCase.Number}`
  searchResponseWrapper.appendChild(searchResponseTitle)

  const inputWrapper = document.createElement('div')
  inputWrapper.classList.add('inputWrapper', 'grid')

  inputWrapper.appendChild(
    createSectionHeader(language.court.sectionCaseProfile || 'Case Profile')
  )

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

  const severityWrapper = document.createElement('div')
  severityWrapper.appendChild(createLabel(language.court.severityScore || 'Severity Score'))
  severityWrapper.appendChild(createReadOnlyInput(`${courtCase.SeverityScore || 0}`))
  inputWrapper.appendChild(severityWrapper)

  const evidenceWrapper = document.createElement('div')
  evidenceWrapper.appendChild(createLabel(language.court.evidenceScore || 'Evidence Score'))
  evidenceWrapper.appendChild(createReadOnlyInput(`${courtCase.EvidenceScore || 0}`))
  inputWrapper.appendChild(evidenceWrapper)

  const evidenceToggleWrapper = document.createElement('div')
  evidenceToggleWrapper.classList.add('evidenceToggleWrapper')
  const evidenceToggleBtn = document.createElement('button')
  evidenceToggleBtn.classList.add('evidenceToggleBtn')
  evidenceToggleBtn.innerText = language.court.viewEvidenceBtn || 'View Evidence Breakdown'
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

  // Scene evidence flags — only show items we can track reliably (see DataController PedEvidenceContext comment)
  const evidenceItems = [
    { label: language.court.evidenceWeapon || 'Armed at Arrest', value: courtCase.EvidenceHadWeapon ?? false, active: courtCase.EvidenceHadWeapon ?? false },
    { label: language.court.evidenceWanted || 'Active Warrant at Encounter', value: courtCase.EvidenceWasWanted ?? false, active: courtCase.EvidenceWasWanted ?? false },
    { label: language.court.evidenceAssault || 'Assaulted Another Person', value: courtCase.EvidenceAssaultedPed ?? false, active: courtCase.EvidenceAssaultedPed ?? false },
    { label: language.court.evidenceVehicleDamage || 'Damaged Vehicle / Property', value: courtCase.EvidenceDamagedVehicle ?? false, active: courtCase.EvidenceDamagedVehicle ?? false },
    { label: language.court.evidenceResisted || 'Resisted Arrest', value: courtCase.EvidenceResisted ?? false, active: courtCase.EvidenceResisted ?? false },
    { label: language.court.evidenceDrugs || 'Drugs Found on Person', value: courtCase.EvidenceHadDrugs ?? false, active: courtCase.EvidenceHadDrugs ?? false },
    { label: language.court.evidenceUseOfForce || 'Use of Force Documented', value: courtCase.EvidenceUseOfForce ?? false, active: courtCase.EvidenceUseOfForce ?? false },
    { label: language.court.evidenceDrunk || 'Intoxicated at Encounter', value: courtCase.EvidenceWasDrunk ?? false, active: courtCase.EvidenceWasDrunk ?? false },
    { label: language.court.evidenceFleeing || 'Attempted to Flee', value: courtCase.EvidenceWasFleeing ?? false, active: courtCase.EvidenceWasFleeing ?? false },
    { label: language.court.evidenceSupervision || 'Supervision Violation', value: courtCase.EvidenceViolatedSupervision ?? false, active: courtCase.EvidenceViolatedSupervision ?? false },
    { label: language.court.evidencePatDown || 'Pat-Down / Search', value: courtCase.EvidenceWasPatDown ?? false, active: courtCase.EvidenceWasPatDown ?? false },
    { label: language.court.evidenceIllegalWeapon || 'Illegal Weapon', value: courtCase.EvidenceIllegalWeapon ?? false, active: courtCase.EvidenceIllegalWeapon ?? false },
  ]

  for (const item of evidenceItems) {
    const row = document.createElement('div')
    row.classList.add('evidenceBreakdownRow')
    if (item.active) row.classList.add('evidenceBreakdownRowActive')
    const lbl = document.createElement('span')
    lbl.innerText = item.label
    const val = document.createElement('span')
    val.classList.add('evidenceBreakdownValue')
    val.innerText = item.value
      ? (language.court.evidenceYes || 'YES')
      : (language.court.evidenceNo || 'NO')
    row.appendChild(lbl)
    row.appendChild(val)
    evidenceBreakdown.appendChild(row)
  }

  // Charges filed
  const charges = courtCase.Charges || []
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
  prosecutionWrapper.appendChild(
    createLabel(language.court.prosecutionStrength || 'Prosecution Strength')
  )
  prosecutionWrapper.appendChild(
    createReadOnlyInput(`${(courtCase.ProsecutionStrength || 0).toFixed(1)}`)
  )
  inputWrapper.appendChild(prosecutionWrapper)

  const defenseWrapper = document.createElement('div')
  defenseWrapper.appendChild(createLabel(language.court.defenseStrength || 'Defense Strength'))
  defenseWrapper.appendChild(
    createReadOnlyInput(`${(courtCase.DefenseStrength || 0).toFixed(1)}`)
  )
  inputWrapper.appendChild(defenseWrapper)

  const convictionChanceWrapper = document.createElement('div')
  convictionChanceWrapper.appendChild(createLabel(language.court.convictionChance || 'Conviction Probability'))
  convictionChanceWrapper.appendChild(createReadOnlyInput(`${courtCase.ConvictionChance || 0}%`))
  inputWrapper.appendChild(convictionChanceWrapper)

  const docketWrapper = document.createElement('div')
  docketWrapper.appendChild(createLabel(language.court.docketPressure || 'Docket Pressure'))
  docketWrapper.appendChild(
    createReadOnlyInput(`${((courtCase.DocketPressure || 0) * 100).toFixed(0)}%`)
  )
  inputWrapper.appendChild(docketWrapper)

  const policyWrapper = document.createElement('div')
  policyWrapper.appendChild(
    createLabel(language.court.policyAdjustment || 'District Policy Adjustment')
  )
  policyWrapper.appendChild(
    createReadOnlyInput(`${((courtCase.PolicyAdjustment || 0) * 100).toFixed(1)}%`)
  )
  inputWrapper.appendChild(policyWrapper)

  const juryWrapper = document.createElement('div')
  juryWrapper.appendChild(createLabel(language.court.jury || 'Jury'))
  const juryText = courtCase.IsJuryTrial
    ? `${courtCase.JuryVotesForConviction || 0}-${courtCase.JuryVotesForAcquittal || 0} / ${courtCase.JurySize || 0}`
    : language.court.benchTrial || 'Bench Trial'
  juryWrapper.appendChild(createReadOnlyInput(juryText))
  inputWrapper.appendChild(juryWrapper)

  const pleaWrapper = document.createElement('div')
  pleaWrapper.appendChild(createLabel(language.court.plea || 'Plea'))
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
      showLoadingOnButton(forceResolveBtn)
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
      if (responseText === 'OK' && typeof refreshCourtList === 'function') {
        topWindow.showNotification(language.court.forceResolveSuccess || 'Case resolved.', 'success')
        await refreshCourtList()
      } else {
        topWindow.showNotification(language.court.forceResolveError || 'Could not resolve case.', 'error')
      }
      hideLoadingOnButton(forceResolveBtn)
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
  label.innerHTML = text
  return label
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
  input.value = value
  return input
}

async function formatIsoDate(value) {
  if (!value) return '-'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString()
}

async function getChargeDetailsString(fine, time) {
  const language = await getLanguage()

  const fineFormatted = await getCurrencyString(fine)
  let fineString = `${language.court.fine}: ${fineFormatted}`
  const timeFormatted =
    time === null ? language.units.life : await convertDaysToYMD(time)
  let timeString = `${language.court.incarceration}: ${timeFormatted}`

  return time > 0 || time === null
    ? `${fineString} | ${timeString}`
    : fineString
}

async function getTotalTimeString(time, lifeSentences) {
  const language = await getLanguage()

  const timeString = await convertDaysToYMD(time)
  if (lifeSentences < 1) return timeString
  if (lifeSentences == 1) return `${language.units.life} + ${timeString}`
  return `${lifeSentences}x ${language.units.life} + ${timeString}`
}
