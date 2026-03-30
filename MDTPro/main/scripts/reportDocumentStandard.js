/**
 * Paper-style chrome for browser MDT report create view (incident, citation, arrest, impound, traffic, injury).
 * Property/evidence uses its own shell in propertyEvidenceSection.js.
 */
;(function () {
  const titleKeys = {
    incident: 'incidentTitle',
    citation: 'citationTitle',
    arrest: 'arrestTitle',
    impound: 'impoundTitle',
    trafficIncident: 'trafficIncidentTitle',
    injury: 'injuryTitle'
  }

  const defaultTitles = {
    incidentTitle: 'General Incident Report (IR)',
    citationTitle: 'Uniform Traffic Citation — Violation Notice',
    arrestTitle: 'Arrest & Booking Report',
    impoundTitle: 'Vehicle Tow / Impound Report',
    trafficIncidentTitle: 'Traffic Collision Report (TCR)',
    injuryTitle: 'Injury / Medical Incident Report'
  }

  /**
   * @param {HTMLElement} reportInformationEl
   * @param {string} reportType
   * @returns {HTMLElement} Body element to append sections into
   */
  window.mdtproMountStandardReportDocument = function (reportInformationEl, reportType) {
    const shell = document.createElement('div')
    shell.className = 'standardReportDocumentShell'
    const toolbar = document.createElement('div')
    toolbar.className = 'report-doc-toolbar'
    const printBtn = document.createElement('button')
    printBtn.type = 'button'
    printBtn.className = 'reportDocToolbarButton'
    printBtn.textContent = 'Print / Save as PDF…'
    printBtn.addEventListener('click', () => window.print())
    toolbar.appendChild(printBtn)
    shell.appendChild(toolbar)

    const docRoot = document.createElement('div')
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
    const rightTitle = document.createElement('div')
    rightTitle.className = 'report-doc-right-title'
    rightCol.appendChild(mainTitle)
    rightCol.appendChild(rightTitle)
    header.appendChild(leftCol)
    header.appendChild(seal)
    header.appendChild(rightCol)

    const bodySlot = document.createElement('div')
    bodySlot.className = 'report-document-body'
    const footer = document.createElement('div')
    footer.className = 'report-doc-footer'

    docRoot.appendChild(header)
    docRoot.appendChild(bodySlot)
    docRoot.appendChild(footer)
    shell.appendChild(docRoot)
    reportInformationEl.appendChild(shell)

    const tKey = titleKeys[reportType] || 'incidentTitle'
    mainTitle.textContent = defaultTitles[tKey] || 'Report'

    ;(async function applyBranding () {
      try {
        const res = await fetch('/data/reportBranding?reportType=' + encodeURIComponent(reportType))
        const j = res.ok ? await res.json() : null
        const t = j && j.activeTemplate
        if (!t) return
        leftCol.textContent = (t.leftColumn || '').replace(/\r\n/g, '\n')
        sealFallback.textContent = String(t.centerTitle || 'LAB').trim().slice(0, 12)
        const badge = t.sealBadgeFile
        if (badge && !/\.svg$/i.test(String(badge))) {
          sealImg.src = '/plugin/DepartmentStyling/image/' + String(badge).trim() + '?v=1'
          sealImg.style.display = 'block'
          sealFallback.style.display = 'none'
        } else {
          sealImg.removeAttribute('src')
          sealImg.style.display = 'none'
          sealFallback.style.display = 'flex'
        }
        rightTitle.textContent = (t.rightTitle || '').replace(/\r\n/g, '\n')
        footer.textContent = t.footer || ''
        const docTitle = t[tKey]
        if (docTitle) mainTitle.textContent = docTitle
      } catch (_) {}
    })()

    return bodySlot
  }
})()
