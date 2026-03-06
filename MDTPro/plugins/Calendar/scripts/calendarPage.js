;(async function () {
  const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

  let currentDate = new Date()
  let reportsByDate = {}

  const monthTitle = document.querySelector('.monthTitle')
  const prevBtn = document.querySelector('.navBtn.prev')
  const nextBtn = document.querySelector('.navBtn.next')
  const weekdayHeaders = document.querySelector('.weekdayHeaders')
  const daysEl = document.querySelector('.calendarGrid .days')
  const dayDetailTitle = document.querySelector('.dayDetailTitle')
  const dayDetailContent = document.querySelector('.dayDetailContent')

  async function loadReports() {
    const [citations, arrests, incidents, shiftHistory, currentShift] = await Promise.all([
      fetch('/data/citationReports').then((r) => r.json()).catch(() => []),
      fetch('/data/arrestReports').then((r) => r.json()).catch(() => []),
      fetch('/data/incidentReports').then((r) => r.json()).catch(() => []),
      fetch('/data/shiftHistory').then((r) => r.json()).catch(() => []),
      fetch('/data/currentShift').then((r) => r.json()).catch(() => ({})),
    ])

    const map = {}
    function add(key, type, report) {
      if (!map[key]) map[key] = { arrests: [], citations: [], incidents: [], shifts: [] }
      map[key][type].push(report)
    }

    for (const r of citations || []) {
      const d = r.TimeStamp ? new Date(r.TimeStamp) : null
      if (d && !isNaN(d)) add(dateKey(d), 'citations', r)
    }
    for (const r of arrests || []) {
      const d = r.TimeStamp ? new Date(r.TimeStamp) : null
      if (d && !isNaN(d)) add(dateKey(d), 'arrests', r)
    }
    for (const r of incidents || []) {
      const d = r.TimeStamp ? new Date(r.TimeStamp) : null
      if (d && !isNaN(d)) add(dateKey(d), 'incidents', r)
    }

    for (const shift of shiftHistory || []) {
      if (shift.startTime) {
        const d = new Date(shift.startTime)
        if (!isNaN(d)) {
          const key = dateKey(d)
          if (!map[key]) map[key] = { arrests: [], citations: [], incidents: [], shifts: [] }
          map[key].shifts.push({ type: 'onDuty', time: shift.startTime })
        }
      }
      if (shift.endTime) {
        const d = new Date(shift.endTime)
        if (!isNaN(d)) {
          const key = dateKey(d)
          if (!map[key]) map[key] = { arrests: [], citations: [], incidents: [], shifts: [] }
          map[key].shifts.push({ type: 'offDuty', time: shift.endTime })
        }
      }
    }
    if (currentShift && currentShift.startTime) {
      const d = new Date(currentShift.startTime)
      if (!isNaN(d)) {
        const key = dateKey(d)
        if (!map[key]) map[key] = { arrests: [], citations: [], incidents: [], shifts: [] }
        map[key].shifts = map[key].shifts || []
        map[key].shifts.push({ type: 'onDuty', time: currentShift.startTime })
      }
    }

    reportsByDate = map
    return map
  }

  function dateKey(d) {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  }

  function toDate(key) {
    const [y, m, d] = key.split('-').map(Number)
    return new Date(y, m - 1, d)
  }

  function renderMonth() {
    const year = currentDate.getFullYear()
    const month = currentDate.getMonth()
    monthTitle.textContent = `${currentDate.toLocaleString('default', { month: 'long' })} ${year}`

    weekdayHeaders.innerHTML = WEEKDAYS.map((w) => `<span class="weekday">${w}</span>`).join('')

    const first = new Date(year, month, 1)
    const last = new Date(year, month + 1, 0)
    const startPad = first.getDay()
    const numDays = last.getDate()

    const cells = []
    for (let i = 0; i < startPad; i++) {
      cells.push(`<div class="dayCell empty"></div>`)
    }
    for (let d = 1; d <= numDays; d++) {
      const key = `${year}-${String(month + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`
      const data = reportsByDate[key] || { arrests: [], citations: [], incidents: [], shifts: [] }
      const total = data.arrests.length + data.citations.length + data.incidents.length
      const hasShift = (data.shifts && data.shifts.length) > 0
      const badges = []
      if (data.arrests.length) badges.push(`<span class="badge arrest" title="${data.arrests.length} arrest(s)">${data.arrests.length}</span>`)
      if (data.citations.length) badges.push(`<span class="badge citation" title="${data.citations.length} citation(s)">${data.citations.length}</span>`)
      if (data.incidents.length) badges.push(`<span class="badge incident" title="${data.incidents.length} incident(s)">${data.incidents.length}</span>`)
      if (hasShift) badges.push(`<span class="badge shift" title="Shift">Shift</span>`)
      cells.push(
        `<div class="dayCell${total || hasShift ? ' hasActivity' : ''}" data-date="${key}">
          <span class="dayNum">${d}</span>
          ${badges.length ? `<div class="badges">${badges.join('')}</div>` : ''}
        </div>`
      )
    }
    daysEl.innerHTML = cells.join('')

    daysEl.querySelectorAll('.dayCell[data-date]').forEach((cell) => {
      cell.addEventListener('click', () => showDay(cell.dataset.date))
    })
  }

  function showDay(key) {
    const data = reportsByDate[key] || { arrests: [], citations: [], incidents: [], shifts: [] }
    const d = toDate(key)
    dayDetailTitle.textContent = d.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' })

    const parts = []

    if (data.shifts && data.shifts.length > 0) {
      const sorted = [...data.shifts].sort((a, b) => new Date(a.time) - new Date(b.time))
      const shiftItems = sorted.map((s) => {
        const time = new Date(s.time).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
        const label = s.type === 'onDuty' ? 'On duty' : 'Off duty'
        return `<li class="shiftEvent"><span class="reportTime">${time}</span> <span class="reportName">${label}</span></li>`
      }).join('')
      parts.push(`<section class="reportGroup"><h3>Shift</h3><ul>${shiftItems}</ul></section>`)
    }

    if (data.arrests.length) {
      parts.push(`<section class="reportGroup"><h3>Arrests (${data.arrests.length})</h3><ul>${data.arrests.map((r) => formatReport(r, 'arrest')).join('')}</ul></section>`)
    }
    if (data.citations.length) {
      parts.push(`<section class="reportGroup"><h3>Citations (${data.citations.length})</h3><ul>${data.citations.map((r) => formatReport(r, 'citation')).join('')}</ul></section>`)
    }
    if (data.incidents.length) {
      parts.push(`<section class="reportGroup"><h3>Incidents (${data.incidents.length})</h3><ul>${data.incidents.map((r) => formatReport(r, 'incident')).join('')}</ul></section>`)
    }

    if (parts.length) {
      dayDetailContent.innerHTML = parts.join('')
      dayDetailContent.querySelectorAll('li[data-report-id]').forEach((li) => {
        const id = li.dataset.reportId
        if (!id) return
        li.classList.add('clickable')
        li.addEventListener('click', function () {
          const type = this.dataset.reportType
          const topWin = window.top || window
          if (type && typeof topWin.openIdInReport === 'function') {
            topWin.openIdInReport(id, type)
          }
        })
      })
    } else {
      dayDetailContent.innerHTML = '<p class="noActivity">No shift activity, arrests, citations, or incidents on this day.</p>'
    }
  }

  function formatReport(r, reportType) {
    const time = r.TimeStamp ? new Date(r.TimeStamp).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' }) : ''
    const name = r.OffenderPedName || (r.OffenderPedsNames && r.OffenderPedsNames[0]) || '—'
    const id = r.Id ? `#${r.Id}` : ''
    return `<li data-report-id="${escapeHtml(r.Id || '')}" data-report-type="${reportType}"><span class="reportTime">${time}</span> <span class="reportName">${escapeHtml(name)}</span> ${id ? `<span class="reportId">${escapeHtml(id)}</span>` : ''}</li>`
  }

  function escapeHtml(s) {
    if (s == null) return ''
    const div = document.createElement('div')
    div.textContent = s
    return div.innerHTML
  }

  function goPrevMonth() {
    currentDate.setMonth(currentDate.getMonth() - 1)
    renderMonth()
  }

  function goNextMonth() {
    currentDate.setMonth(currentDate.getMonth() + 1)
    renderMonth()
  }

  prevBtn.addEventListener('click', goPrevMonth)
  nextBtn.addEventListener('click', goNextMonth)

  await loadReports()
  renderMonth()
})()
