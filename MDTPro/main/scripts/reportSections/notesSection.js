async function getNotesSection(notes, isList = false) {
  const language = await getLanguage()

  const title = document.createElement('div')
  title.classList.add('title')
  title.innerHTML = language.reports.sections.notes
  title.style.borderBottom = 'none'
  title.style.paddingBottom = '0'

  const notesTextarea = document.createElement('textarea')
  notesTextarea.classList.add('notesTextarea')
  notesTextarea.value = notes || ''
  notesTextarea.id = 'notesSectionTextarea'
  notesTextarea.disabled = isList

  const sectionWrapper = document.createElement('div')
  sectionWrapper.classList.add('section')

  sectionWrapper.appendChild(title)
  sectionWrapper.appendChild(notesTextarea)

  return sectionWrapper
}
