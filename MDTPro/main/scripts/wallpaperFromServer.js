/**
 * Apply custom desktop image from the server (MDTPro/data/wallpaperUser.json + img).
 * Runs on every main MDT load so the wallpaper persists across sessions and does not
 * depend on the CustomWallpaper plugin being enabled (upload UI is still plugin-gated in Customization).
 */
;(function () {
  const path = typeof location !== 'undefined' ? location.pathname || '' : ''
  if (/page\/customization/i.test(path) || /^\/customization\/?$/i.test(path)) return

  function apply(state) {
    const wall = document.querySelector('.desktopWallpaper')
    if (!state || !state.useCustom || !state.hasImage) {
      document.documentElement.removeAttribute('data-mdt-custom-wallpaper')
      if (wall) wall.style.removeProperty('background-image')
      return
    }
    document.documentElement.setAttribute('data-mdt-custom-wallpaper', '1')
    if (wall) {
      const t = state.updated ? encodeURIComponent(state.updated) : String(Date.now())
      wall.style.setProperty('background-image', 'url("/image/desktop?t=' + t + '")', 'important')
    }
  }

  async function run() {
    try {
      const res = await fetch('/wallpaperSettings', { cache: 'no-store' })
      if (!res.ok) return
      apply(await res.json())
    } catch (_) {}
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', run)
  else run()
})()
