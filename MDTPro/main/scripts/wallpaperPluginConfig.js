;(function () {
  function active() {
    try {
      const a = JSON.parse(localStorage.getItem('activePlugins') || '[]')
      return Array.isArray(a) && a.indexOf('CustomWallpaper') >= 0
    } catch (_) {
      return false
    }
  }

  function ensureStyles() {
    if (document.getElementById('mdt-custom-wallpaper-css')) return
    const l = document.createElement('link')
    l.id = 'mdt-custom-wallpaper-css'
    l.rel = 'stylesheet'
    l.href = '/plugin/CustomWallpaper/style/wallpaper.css'
    document.head.appendChild(l)
  }

  function injectSection() {
    var old = document.getElementById('mdt-wp-config')
    if (old) old.remove()
    if (!active()) return
    ensureStyles()
    const wrapper = document.querySelector('.main .configWrapper')
    const sticky = document.querySelector('.main .configStickyBar')
    if (!wrapper || !sticky) return

    const card = document.createElement('div')
    card.id = 'mdt-wp-config'
    card.className = 'mdt-wp-card'
    card.innerHTML =
      '<div class="titleRow"><span class="title mdt-wp-title">Desktop wallpaper</span></div>' +
      '<p class="mdt-wp-status" id="mdt-wp-status"></p>' +
      '<div class="mdt-wp-row">' +
      '<input type="file" id="mdt-wp-file" accept="image/png,image/jpeg" />' +
      '</div>' +
      '<div class="mdt-wp-row"><img id="mdt-wp-preview" class="mdt-wp-preview hidden" alt="" /></div>' +
      '<div class="mdt-wp-actions">' +
      '<button type="button" id="mdt-wp-save">Apply</button>' +
      '<button type="button" id="mdt-wp-reset">Use default</button>' +
      '</div>'

    wrapper.parentNode.insertBefore(card, wrapper)

    const statusEl = card.querySelector('#mdt-wp-status')
    const fileInput = card.querySelector('#mdt-wp-file')
    const preview = card.querySelector('#mdt-wp-preview')
    const saveBtn = card.querySelector('#mdt-wp-save')
    const resetBtn = card.querySelector('#mdt-wp-reset')

    var pendingBase64 = null

    function setStatus(text, isError) {
      statusEl.textContent = text || ''
      statusEl.style.color = isError ? 'var(--color-accent-warn, #c44)' : ''
    }

    function showPreviewFromState(data) {
      pendingBase64 = null
      fileInput.value = ''
      if (data && data.useCustom && data.hasImage) {
        var t = data.updated ? encodeURIComponent(data.updated) : String(Date.now())
        preview.src = '/image/desktop?t=' + t
        preview.classList.remove('hidden')
        setStatus('Using custom image.')
      } else {
        preview.removeAttribute('src')
        preview.classList.add('hidden')
        setStatus('Using default (solid background, no image).')
      }
    }

    fetch('/wallpaperSettings')
      .then(function (r) {
        return r.json()
      })
      .then(showPreviewFromState)
      .catch(function () {
        setStatus('Could not load wallpaper state.', true)
      })

    fileInput.addEventListener('change', function () {
      var f = fileInput.files && fileInput.files[0]
      if (!f) return
      if (f.size > 14 * 1024 * 1024) {
        setStatus('File is too large (max 12 MB).', true)
        return
      }
      var reader = new FileReader()
      reader.onload = function () {
        var result = reader.result
        if (typeof result !== 'string') return
        pendingBase64 = result
        preview.src = result
        preview.classList.remove('hidden')
        setStatus('Not saved — click Apply.')
      }
      reader.readAsDataURL(f)
    })

    saveBtn.addEventListener('click', function () {
      if (!pendingBase64) {
        setStatus('Choose an image first.', true)
        return
      }
      saveBtn.disabled = true
      fetch('/post/wallpaperUser', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ imageBase64: pendingBase64 }),
      })
        .then(function (r) {
          return r.json().then(function (j) {
            return { ok: r.ok, j: j }
          })
        })
        .then(function (_ref) {
          if (_ref.ok) {
            setStatus('Saved. Refresh the main MDT window if it is open.')
            showPreviewFromState({ useCustom: true, hasImage: true, updated: new Date().toISOString() })
            try {
              if (window.opener && !window.opener.closed) window.opener.location.reload()
            } catch (_) {}
          } else {
            setStatus((_ref.j && _ref.j.error) || 'Save failed', true)
          }
        })
        .catch(function () {
          setStatus('Save failed.', true)
        })
        .then(function () {
          saveBtn.disabled = false
        })
    })

    resetBtn.addEventListener('click', function () {
      resetBtn.disabled = true
      fetch('/post/wallpaperUser', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reset: true }),
      })
        .then(function (r) {
          return r.json().then(function (j) {
            return { ok: r.ok, j: j }
          })
        })
        .then(function (_ref) {
          if (_ref.ok) {
            setStatus('Restored default.')
            showPreviewFromState({ useCustom: false, hasImage: false })
            try {
              if (window.opener && !window.opener.closed) window.opener.location.reload()
            } catch (_) {}
          } else {
            setStatus((_ref.j && _ref.j.error) || 'Reset failed', true)
          }
        })
        .catch(function () {
          setStatus('Reset failed.', true)
        })
        .then(function () {
          resetBtn.disabled = false
        })
    })
  }

  window.__mdtCustomWallpaperAfterConfigRender = function () {
    injectSection()
  }
})()
