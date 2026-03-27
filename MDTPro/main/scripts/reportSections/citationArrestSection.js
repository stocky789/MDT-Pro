async function getCitationArrestSection(type, isList = false, list = []) {
  const language = await getLanguage()
  const options =
    type == 'citation' ? await getCitationOptions() : await getArrestOptions()

  let arrestNarcoticScheduleFilter = 'all'

  function getNarcoticScheduleRoman (chargeName) {
    const n = (chargeName || '').toLowerCase()
    if (/\bschedule\s+iv\b/.test(n)) return 'IV'
    if (/\bschedule\s+v\b/.test(n)) return 'V'
    if (/\bschedule\s+iii\b/.test(n)) return 'III'
    if (/\bschedule\s+ii\b/.test(n)) return 'II'
    if (/\bschedule\s+i\b/.test(n)) return 'I'
    return null
  }

  function chargeMatchesArrestScheduleFilter (chargeName, filter) {
    if (type !== 'arrest' || !filter || filter === 'all') return true
    const roman = getNarcoticScheduleRoman(chargeName)
    if (filter === 'other') return roman == null
    return roman === filter
  }

  const title = document.createElement('div')
  if (isList) {
    title.classList.add('searchResponseSectionTitle')
  } else {
    title.classList.add('title')
  }
  title.innerHTML = language.reports.sections[type].title
  title.style.borderBottom = 'none'
  title.style.paddingBottom = '0'

  const sectionWrapper = document.createElement('div')
  sectionWrapper.classList.add('section')
  sectionWrapper.classList.add(`${type}Section`)
  if (isList) sectionWrapper.classList.add('searchResponseWrapper')

  const additionalWrapper = document.createElement('div')
  const canEdit = !isList

  const optionsWrapper = document.createElement('div')
  optionsWrapper.classList.add('optionsWrapper')

  const optionsList = document.createElement('div')
  optionsList.classList.add('optionsList')

  const optionsSearchInput = document.createElement('input')
  optionsSearchInput.type = 'text'
  optionsSearchInput.placeholder =
    language.reports.sections[type].searchChargesPlaceholder
  optionsSearchInput.autocomplete = 'off'
  optionsSearchInput.id = `${type}OptionsSearchInput`
  optionsSearchInput.addEventListener('input', async function () {
    await performSearch(optionsSearchInput.value.trim())
  })
  optionsWrapper.appendChild(optionsSearchInput)

  if (type === 'arrest' && canEdit) {
    const schedRow = document.createElement('div')
    schedRow.className = 'arrestChargeScheduleFilterRow'
    schedRow.style.marginBottom = '8px'
    const schedLabel = document.createElement('label')
    schedLabel.style.display = 'block'
    schedLabel.style.fontSize = '13px'
    schedLabel.style.marginBottom = '4px'
    schedLabel.textContent = language.reports?.sections?.arrest?.narcoticScheduleFilterLabel || 'Narcotic charges by schedule'
    const scheduleFilterSelect = document.createElement('select')
    scheduleFilterSelect.className = 'arrestNarcoticScheduleFilter'
    scheduleFilterSelect.setAttribute('aria-label', schedLabel.textContent)
    const schedOpts = [
      { value: 'all', text: language.reports?.sections?.arrest?.allSchedulesOption || 'All charges' },
      { value: 'I', text: 'Schedule I' },
      { value: 'II', text: 'Schedule II' },
      { value: 'III', text: 'Schedule III' },
      { value: 'IV', text: 'Schedule IV' },
      { value: 'V', text: 'Schedule V' },
      { value: 'other', text: language.reports?.sections?.arrest?.nonScheduledNarcoticsOption || 'Other / non-schedule wording' }
    ]
    for (const so of schedOpts) {
      const o = document.createElement('option')
      o.value = so.value
      o.textContent = so.text
      scheduleFilterSelect.appendChild(o)
    }
    scheduleFilterSelect.addEventListener('change', async function () {
      arrestNarcoticScheduleFilter = scheduleFilterSelect.value
      await performSearch(optionsSearchInput.value.trim())
    })
    schedRow.appendChild(schedLabel)
    schedRow.appendChild(scheduleFilterSelect)
    optionsWrapper.appendChild(schedRow)
  }

  async function performSearch(search) {
    optionsWrapper.querySelectorAll('details').forEach((el) => el.remove())
    for (const group of options) {
      const details = document.createElement('details')
      if (search) details.open = true

      const summary = document.createElement('summary')
      summary.innerHTML = group.name
      details.appendChild(summary)
      summary.addEventListener('click', function () {
        optionsWrapper.querySelectorAll('details').forEach((el) => {
          if (el != details) el.open = false
        })
      })

      for (const charge of group.charges) {
        if (!chargeMatchesArrestScheduleFilter(charge.name, arrestNarcoticScheduleFilter)) {
          continue
        }
        if (
          search &&
          !charge.name.toLowerCase().includes(search.toLowerCase())
        ) {
          continue
        }
        const button = document.createElement('button')
        button.innerHTML = charge.name
        button.addEventListener('click', async function () {
          button.blur()
          await addChargeToOptionsList(charge)
        })

        const chargeDetailsOnButton = document.createElement('span')
        chargeDetailsOnButton.classList.add('chargeDetailsOnButton')
        chargeDetailsOnButton.innerHTML = await getChargeDetailsString(
          type,
          charge
        )

        button.appendChild(chargeDetailsOnButton)
        details.appendChild(button)
      }
      if (search && details.children.length < 2) continue
      optionsWrapper.appendChild(details)
    }
  }

  async function addChargeToOptionsList(charge) {
    const chargeWrapper = document.createElement('div')
    chargeWrapper.classList.add('chargeWrapper')
    chargeWrapper.dataset.charge = JSON.stringify(charge)

    const chargeName = document.createElement('div')
    chargeName.classList.add('chargeName')
    chargeName.innerHTML = charge.name

    const chargeDetails = document.createElement('div')
    chargeDetails.classList.add('chargeDetails')
    chargeDetails.innerHTML = await getChargeDetailsString(type, charge)

    const deleteChargeButton = document.createElement('button')
    deleteChargeButton.classList.add('deleteChargeButton')
    deleteChargeButton.innerHTML =
      topDoc.querySelector('.iconAccess .trash').innerHTML
    deleteChargeButton.addEventListener('click', function () {
      chargeWrapper.remove()
    })

    chargeWrapper.appendChild(chargeName)
    chargeWrapper.appendChild(chargeDetails)
    if (canEdit) chargeWrapper.appendChild(deleteChargeButton)

    optionsList.appendChild(chargeWrapper)
  }

  if (list.length > 0) {
    for (const charge of list) {
      charge.addedByReportInEdit = true
      await addChargeToOptionsList(charge)
    }
  }

  sectionWrapper.appendChild(title)
  if (canEdit) {
    if (list.length > 0) {
      const addMoreLabel = document.createElement('div')
      addMoreLabel.classList.add('addChargesLabel')
      addMoreLabel.style.marginBottom = '8px'
      addMoreLabel.style.fontSize = '13px'
      addMoreLabel.style.fontWeight = '600'
      addMoreLabel.style.color = 'var(--color-accent)'
      addMoreLabel.textContent = language.reports.sections?.addMoreCharges ?? 'Add more charges (search and click below)'
      additionalWrapper.appendChild(addMoreLabel)
    }
    additionalWrapper.appendChild(optionsWrapper)
    await performSearch()
  }
  additionalWrapper.appendChild(optionsList)
  sectionWrapper.appendChild(additionalWrapper)

  return sectionWrapper
}

async function getChargeDetailsString(type, charge) {
  const language = await getLanguage()

  let fineString = `${language.reports.sections.fine}: `
  if (charge.minFine == charge.maxFine) {
    fineString += await getCurrencyString(charge.minFine)
  } else {
    const minFineStr = await getCurrencyString(charge.minFine)
    const maxFineStr = await getCurrencyString(charge.maxFine)
    fineString += `${minFineStr} - ${maxFineStr}`
  }

  if (type == 'citation') {
    return fineString
  }

  let incarcerationString = `${language.reports.sections.incarceration}: `
  if (charge.minDays != null && charge.maxDays != null && charge.minDays == charge.maxDays) {
    incarcerationString += await convertDaysToYMD(charge.minDays)
  } else if (charge.minDays != null) {
    const minDaysStr = await convertDaysToYMD(charge.minDays)
    const maxDaysStr =
      charge.maxDays == null
        ? language.units.life
        : await convertDaysToYMD(charge.maxDays)
    incarcerationString += `${minDaysStr} - ${maxDaysStr}`
  } else {
    incarcerationString += '–'
  }
  return `${fineString} | ${incarcerationString}`
}
