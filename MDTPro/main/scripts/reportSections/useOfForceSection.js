async function getUseOfForceSection (data = {}, isList = false) {
  const language = await getLanguage()
  const section = document.createElement('div')
  section.classList.add('section', 'useOfForceSection')
  if (isList) section.classList.add('searchResponseWrapper')

  const title = document.createElement('div')
  title.classList.add(isList ? 'searchResponseSectionTitle' : 'title')
  title.innerHTML = language.reports?.sections?.useOfForce?.title || 'Use of Force'

  const typeLabel = document.createElement('label')
  typeLabel.innerHTML = language.reports?.sections?.useOfForce?.type || 'Type'
  typeLabel.htmlFor = 'useOfForceTypeSelect'
  const typeSelect = document.createElement('select')
  typeSelect.id = 'useOfForceTypeSelect'
  typeSelect.disabled = isList
  const types = ['', 'Taser', 'Baton', 'Fist', 'Firearm', 'Other']
  types.forEach((t) => {
    const opt = document.createElement('option')
    opt.value = t
    opt.textContent = t === '' ? '' : (t || '—')
    typeSelect.appendChild(opt)
  })
  typeSelect.value = data?.Type || ''

  const typeOtherWrapper = document.createElement('div')
  typeOtherWrapper.classList.add('useOfForceTypeOtherWrapper')
  const typeOtherLabel = document.createElement('label')
  typeOtherLabel.innerHTML = language.reports?.sections?.useOfForce?.typeOther || 'Type (if Other)'
  typeOtherLabel.htmlFor = 'useOfForceTypeOtherInput'
  const typeOtherInput = document.createElement('input')
  typeOtherInput.type = 'text'
  typeOtherInput.id = 'useOfForceTypeOtherInput'
  typeOtherInput.placeholder = 'e.g. Pepper spray'
  typeOtherInput.disabled = isList
  typeOtherInput.value = data?.TypeOther || ''
  typeOtherWrapper.appendChild(typeOtherLabel)
  typeOtherWrapper.appendChild(typeOtherInput)

  const justificationLabel = document.createElement('label')
  justificationLabel.innerHTML = language.reports?.sections?.useOfForce?.justification || 'Justification'
  justificationLabel.htmlFor = 'useOfForceJustificationInput'
  const justificationInput = document.createElement('textarea')
  justificationInput.id = 'useOfForceJustificationInput'
  justificationInput.rows = 3
  justificationInput.placeholder = language.reports?.sections?.useOfForce?.justificationPlaceholder || 'Describe circumstances requiring use of force'
  justificationInput.disabled = isList
  justificationInput.value = data?.Justification || ''

  const injuryWrapper = document.createElement('div')
  injuryWrapper.classList.add('inputWrapper', 'grid')
  const injurySuspectLabel = document.createElement('label')
  injurySuspectLabel.innerHTML = language.reports?.sections?.useOfForce?.injuryToSuspect || 'Injury to suspect'
  const injurySuspectCheck = document.createElement('input')
  injurySuspectCheck.type = 'checkbox'
  injurySuspectCheck.id = 'useOfForceInjurySuspect'
  injurySuspectCheck.disabled = isList
  injurySuspectCheck.checked = !!data?.InjuryToSuspect
  injurySuspectLabel.appendChild(injurySuspectCheck)

  const injuryOfficerLabel = document.createElement('label')
  injuryOfficerLabel.innerHTML = language.reports?.sections?.useOfForce?.injuryToOfficer || 'Injury to officer'
  const injuryOfficerCheck = document.createElement('input')
  injuryOfficerCheck.type = 'checkbox'
  injuryOfficerCheck.id = 'useOfForceInjuryOfficer'
  injuryOfficerCheck.disabled = isList
  injuryOfficerCheck.checked = !!data?.InjuryToOfficer
  injuryOfficerLabel.appendChild(injuryOfficerCheck)

  injuryWrapper.appendChild(injurySuspectLabel)
  injuryWrapper.appendChild(injuryOfficerLabel)

  const witnessesLabel = document.createElement('label')
  witnessesLabel.innerHTML = language.reports?.sections?.useOfForce?.witnesses || 'Witnesses'
  witnessesLabel.htmlFor = 'useOfForceWitnessesInput'
  const witnessesInput = document.createElement('input')
  witnessesInput.type = 'text'
  witnessesInput.id = 'useOfForceWitnessesInput'
  witnessesInput.placeholder = 'Names of witnesses (optional)'
  witnessesInput.disabled = isList
  witnessesInput.value = data?.Witnesses || ''

  const toggleTypeOther = () => {
    typeOtherWrapper.style.display = typeSelect.value === 'Other' ? '' : 'none'
  }
  typeSelect.addEventListener('change', toggleTypeOther)
  toggleTypeOther()

  const wrapper = document.createElement('div')
  wrapper.classList.add('inputWrapper')
  wrapper.appendChild(typeLabel)
  wrapper.appendChild(typeSelect)
  wrapper.appendChild(typeOtherWrapper)
  wrapper.appendChild(justificationLabel)
  wrapper.appendChild(justificationInput)
  wrapper.appendChild(injuryWrapper)
  wrapper.appendChild(witnessesLabel)
  wrapper.appendChild(witnessesInput)
  section.appendChild(title)
  section.appendChild(wrapper)
  return section
}
