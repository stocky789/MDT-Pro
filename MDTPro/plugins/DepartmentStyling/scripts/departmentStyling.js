/**
 * Department Styling plugin – apply MDT theme based on selected department.
 * Stores selection in localStorage. Injects theme CSS and updates badge images.
 */
;(function () {
  const STORAGE_KEY = 'mdtDepartmentTheme'
  const PLUGIN_ID = document.currentScript?.dataset?.pluginId || 'DepartmentStyling'

  const DEPARTMENTS = [
    { id: 'default', label: 'San Andreas Government', title: 'San Andreas Government', badgePath: `${PLUGIN_ID}/image/sagov-badge.png` },
    { id: 'lspd', label: 'Los Santos Police Department (LSPD)', title: 'LSPD', badgePath: `${PLUGIN_ID}/image/lspd-badge.png` },
    { id: 'safd', label: 'San Andreas Fire Department (SAFD)', title: 'SAFD', badgePath: `${PLUGIN_ID}/image/safd-badge.png` },
    { id: 'bcso', label: 'Blaine County Sheriff\'s Office (BCSO)', title: 'BCSO', badgePath: `${PLUGIN_ID}/image/bcso-badge.png` },
    { id: 'lssd', label: 'Los Santos County Sheriff\'s Department (LSSD)', title: 'LSSD', badgePath: `${PLUGIN_ID}/image/lssd-badge.png` },
    { id: 'sahp', label: 'San Andreas Highway Patrol (SAHP)', title: 'SAHP', badgePath: `${PLUGIN_ID}/image/sahp-badge.png` },
    { id: 'fib', label: 'Federal Investigation Bureau (FIB)', title: 'FIB', badgePath: `${PLUGIN_ID}/image/fib-badge.png` },
  ]

  /** Aurora/gradient accents – bold opacity for striking backgrounds */
  const ANIMATED_BG = (accent, secondary) =>
    `--mdt-accent-1: ${accent}40; --mdt-accent-2: ${accent}33; --mdt-accent-3: ${secondary || accent}25;`

  const THEME_CSS = {
    default: `
      [data-mdt-theme="default"] { ${ANIMATED_BG('#c9a227', '#d4c5a0')} }
    `,
    lspd: `
      [data-mdt-theme="lspd"] {
        --color-background: #0d0d0d;
        --color-surface: #1a1a1a;
        --color-surface-dimmed: #1a1a1ae6;
        --color-surface-elevated: #262626;
        --color-surface-card: #141414;
        --color-accent: #ffffff;
        --color-on-accent: #0d0d0d;
        --color-accent-dimmed: #ffffffbf;
        --color-accent-half: #ffffff66;
        --color-accent-glow: #ffffff22;
        --color-accent-secondary: #e5e5e5;
        --color-accent-secondary-dimmed: #e5e5e5bf;
        --color-accent-secondary-half: #e5e5e566;
        --color-text-primary: #ffffff;
        --color-text-primary-dimmed: #a3a3a3;
        --color-text-primary-half: #737373;
        --color-border: #404040;
        --color-border-subtle: #262626;
        --focus-ring: 0 0 0 2px rgba(255, 255, 255, 0.4);
        ${ANIMATED_BG('#ffffff', '#e5e5e5')}
      }
    `,
    safd: `
      [data-mdt-theme="safd"] {
        --color-background: #1c1917;
        --color-surface: #292524;
        --color-surface-dimmed: #292524e6;
        --color-surface-elevated: #44403c;
        --color-surface-card: #3f3a37;
        --color-accent: #dc2626;
        --color-accent-dimmed: #dc2626bf;
        --color-accent-half: #dc262666;
        --color-accent-glow: #dc262622;
        --color-accent-secondary: #f87171;
        --color-accent-secondary-dimmed: #f87171bf;
        --color-accent-secondary-half: #f8717166;
        --color-border: #57534e;
        --color-border-subtle: #44403c;
        --focus-ring: 0 0 0 2px rgba(220, 38, 38, 0.4);
        ${ANIMATED_BG('#dc2626', '#f87171')}
      }
    `,
    bcso: `
      [data-mdt-theme="bcso"] {
        --color-background: #1c1917;
        --color-surface: #292524;
        --color-surface-dimmed: #292524e6;
        --color-surface-elevated: #44403c;
        --color-surface-card: #3f3a37;
        --color-accent: #d97706;
        --color-accent-dimmed: #d97706bf;
        --color-accent-half: #d9770666;
        --color-accent-glow: #d9770622;
        --color-accent-secondary: #fbbf24;
        --color-accent-secondary-dimmed: #fbbf24bf;
        --color-accent-secondary-half: #fbbf2466;
        --color-border: #57534e;
        --color-border-subtle: #44403c;
        --focus-ring: 0 0 0 2px rgba(217, 119, 6, 0.4);
        ${ANIMATED_BG('#d97706', '#fbbf24')}
      }
    `,
    lssd: `
      [data-mdt-theme="lssd"] {
        --color-background: #0f172a;
        --color-surface: #1e293b;
        --color-surface-dimmed: #1e293be6;
        --color-surface-elevated: #334155;
        --color-surface-card: #1e293b;
        --color-accent: #16a34a;
        --color-accent-dimmed: #16a34abf;
        --color-accent-half: #16a34a66;
        --color-accent-glow: #16a34a22;
        --color-accent-secondary: #4ade80;
        --color-accent-secondary-dimmed: #4ade80bf;
        --color-accent-secondary-half: #4ade8066;
        --color-border: #334155;
        --color-border-subtle: #1e293b;
        --focus-ring: 0 0 0 2px rgba(22, 163, 74, 0.4);
        ${ANIMATED_BG('#16a34a', '#4ade80')}
      }
    `,
    sahp: `
      [data-mdt-theme="sahp"] {
        --color-background: #0f172a;
        --color-surface: #1e293b;
        --color-surface-dimmed: #1e293be6;
        --color-surface-elevated: #334155;
        --color-surface-card: #1e293b;
        --color-accent: #166534;
        --color-accent-dimmed: #166534bf;
        --color-accent-half: #16653466;
        --color-accent-glow: #16653422;
        --color-accent-secondary: #22c55e;
        --color-accent-secondary-dimmed: #22c55ebf;
        --color-accent-secondary-half: #22c55e66;
        --color-border: #334155;
        --color-border-subtle: #1e293b;
        --focus-ring: 0 0 0 2px rgba(22, 101, 52, 0.4);
        ${ANIMATED_BG('#166534', '#22c55e')}
      }
    `,
    fib: `
      [data-mdt-theme="fib"] {
        --color-background: #0c1222;
        --color-surface: #0f172a;
        --color-surface-dimmed: #0f172ae6;
        --color-surface-elevated: #1e293b;
        --color-surface-card: #0f172a;
        --color-accent: #64748b;
        --color-accent-dimmed: #64748bbf;
        --color-accent-half: #64748b66;
        --color-accent-glow: #64748b22;
        --color-accent-secondary: #94a3b8;
        --color-accent-secondary-dimmed: #94a3b8bf;
        --color-accent-secondary-half: #94a3b866;
        --color-border: #334155;
        --color-border-subtle: #1e293b;
        --focus-ring: 0 0 0 2px rgba(100, 116, 139, 0.4);
        ${ANIMATED_BG('#64748b', '#94a3b8')}
      }
    `,
  }

  function getTheme() {
    try {
      const t = localStorage.getItem(STORAGE_KEY)
      return t && DEPARTMENTS.some((d) => d.id === t) ? t : 'default'
    } catch {
      return 'default'
    }
  }

  function setTheme(id) {
    localStorage.setItem(STORAGE_KEY, id)
  }

  function getBaseUrl() {
    const script = document.currentScript
    if (script && script.src) {
      const m = script.src.match(/^(https?:\/\/[^/]+)/)
      if (m) return m[1]
    }
    return ''
  }

  function applyThemeToDocument(doc, themeId) {
    if (!doc || !doc.documentElement) return
    const root = doc.documentElement
    root.setAttribute('data-mdt-theme', themeId)
    const existing = doc.getElementById('mdt-department-theme-style')
    if (existing) existing.remove()
    const css = THEME_CSS[themeId]
    if (css) {
      const style = doc.createElement('style')
      style.id = 'mdt-department-theme-style'
      style.textContent = css.trim()
      doc.head.appendChild(style)
    }
  }

  function applyTheme(themeId) {
    applyThemeToDocument(document, themeId)

    document.querySelectorAll('iframe').forEach((iframe) => {
      try {
        if (iframe.contentDocument && iframe.contentDocument !== document) {
          applyThemeToDocument(iframe.contentDocument, themeId)
        }
      } catch (_) {}
    })

    const dept = DEPARTMENTS.find((d) => d.id === themeId)
    const base = getBaseUrl() || (typeof location !== 'undefined' ? location.origin : '')
    const badgeImg = dept?.badgePath
      ? (base ? base : '') + '/plugin/' + dept.badgePath
      : '/image/badge'

    document.querySelectorAll('.taskbarBadge, .taskbarLogo.taskbarBadge, .sidebarBadge img').forEach((el) => {
      if (el.tagName === 'IMG') {
        el.src = badgeImg + '?v=' + (themeId || 'default')
        el.alt = dept?.title || 'MDT Pro'
      }
    })

    const titleEl = document.querySelector('.taskbarTitle')
    if (titleEl) titleEl.textContent = dept?.title || 'San Andreas Government'
  }

  function observeIframes() {
    const target = document.body || document.documentElement
    if (!target) return
    const observer = new MutationObserver(() => {
      document.querySelectorAll('iframe').forEach((iframe) => {
        try {
          const doc = iframe.contentDocument
          if (doc && doc !== document && !doc.getElementById('mdt-department-theme-style')) {
            if (doc.readyState === 'complete') {
              applyThemeToDocument(doc, getTheme())
            } else {
              iframe.addEventListener('load', () => applyThemeToDocument(doc, getTheme()), { once: true })
            }
          }
        } catch (_) {}
      })
    })
    observer.observe(target, { childList: true, subtree: true })
  }

  function injectThemeSwitcher() {
    const settingsContent = document.querySelector('.overlay .settings .settingsContent')
    if (!settingsContent) return

    const existing = document.getElementById('mdt-department-theme-section')
    if (existing) return

    const section = document.createElement('div')
    section.id = 'mdt-department-theme-section'
    section.className = 'settingsSection'
    section.innerHTML = `
      <div class="titleRow">
        <span class="title">Department Theme</span>
      </div>
      <div class="departmentThemeRow">
        <label for="mdt-department-select">MDT theme:</label>
        <select id="mdt-department-select" class="departmentThemeSelect">
          ${DEPARTMENTS.map((d) => `<option value="${d.id}">${d.label}</option>`).join('')}
        </select>
        <p class="settingsHint">Changes colors, badge, and animated background for your department. Child windows inherit the theme.</p>
      </div>
    `

    settingsContent.insertBefore(section, settingsContent.firstChild)

    const select = document.getElementById('mdt-department-select')
    select.value = getTheme()
    select.addEventListener('change', function () {
      const id = this.value
      setTheme(id)
      applyTheme(id)
      try {
        if (typeof showNotification === 'function') {
          showNotification('Theme updated.', 'success')
        } else if (window.topWindow && typeof window.topWindow.showNotification === 'function') {
          window.topWindow.showNotification('Theme updated.', 'success')
        }
      } catch (_) {}
    })
  }

  function init() {
    const themeId = getTheme()
    applyTheme(themeId)
    observeIframes()
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', injectThemeSwitcher)
    } else {
      injectThemeSwitcher()
    }
  }

  init()
})()
