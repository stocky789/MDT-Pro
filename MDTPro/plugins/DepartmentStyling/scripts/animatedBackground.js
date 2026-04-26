/**
 * Canvas-based particle animation for department-themed backgrounds.
 * Floating particles in the department accent color – smooth, calm motion.
 */
;(function () {
  const PARTICLE_COUNT = 100
  let rafId = null
  let particles = []

  function hexToRgba(hex, alpha = 1) {
    const m = hex.match(/^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})?$/i)
    if (!m) return `rgba(255,255,255,${alpha})`
    const r = parseInt(m[1], 16)
    const g = parseInt(m[2], 16)
    const b = parseInt(m[3], 16)
    return `rgba(${r},${g},${b},${alpha})`
  }

  function getAccentColor() {
    const root = document.documentElement
    const style = getComputedStyle(root)
    return style.getPropertyValue('--color-accent').trim() || '#ffffff'
  }

  function createParticle(width, height, color) {
    return {
      x: Math.random() * width,
      y: Math.random() * height,
      vx: (Math.random() - 0.5) * 0.1,
      vy: (Math.random() - 0.5) * 0.1,
      size: 1 + Math.random() * 2,
      alpha: 0.2 + Math.random() * 0.5,
    }
  }

  function initParticles(width, height) {
    const color = getAccentColor()
    particles = []
    for (let i = 0; i < PARTICLE_COUNT; i++) {
      particles.push(createParticle(width, height, color))
    }
  }

  function draw(canvas, ctx) {
    const width = canvas.width
    const height = canvas.height
    const color = getAccentColor()

    ctx.clearRect(0, 0, width, height)

    particles.forEach((p) => {
      p.x += p.vx
      p.y += p.vy
      if (p.x < 0 || p.x > width) p.vx *= -1
      if (p.y < 0 || p.y > height) p.vy *= -1
      p.x = Math.max(0, Math.min(width, p.x))
      p.y = Math.max(0, Math.min(height, p.y))

      ctx.beginPath()
      ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2)
      ctx.fillStyle = hexToRgba(color, p.alpha)
      ctx.fill()
    })

    rafId = requestAnimationFrame(() => draw(canvas, ctx))
  }

  function startAnimation(container) {
    if (!container) return

    const canvas = document.createElement('canvas')
    canvas.id = 'mdt-particle-canvas'
    canvas.style.cssText =
      'position:absolute;inset:0;width:100%;height:100%;pointer-events:none;display:block;'
    container.appendChild(canvas)

    const ctx = canvas.getContext('2d')
    if (!ctx) return

    function resize() {
      const rect = container.getBoundingClientRect()
      const dpr = window.devicePixelRatio || 1
      canvas.width = rect.width * dpr
      canvas.height = rect.height * dpr
      canvas.style.width = rect.width + 'px'
      canvas.style.height = rect.height + 'px'
      ctx.scale(dpr, dpr)
      initParticles(rect.width, rect.height)
    }

    const ro = new ResizeObserver(resize)
    ro.observe(container)
    resize()
    draw(canvas, ctx)
  }

  function stopAnimation() {
    if (rafId) {
      cancelAnimationFrame(rafId)
      rafId = null
    }
    const canvas = document.getElementById('mdt-particle-canvas')
    if (canvas) canvas.remove()
  }

  function useCustomWallpaper() {
    return document.documentElement.getAttribute('data-mdt-custom-wallpaper') === '1'
  }

  function run() {
    const root = document.documentElement
    const container = document.querySelector('.desktopWallpaper')
    if (useCustomWallpaper()) {
      stopAnimation()
      return
    }
    const theme = root.getAttribute('data-mdt-theme')
    if (theme && container) {
      stopAnimation()
      startAnimation(container)
    } else {
      stopAnimation()
    }
  }

  function observeTheme() {
    run()
    const observer = new MutationObserver((mutations) => {
      for (const m of mutations) {
        if (m.attributeName === 'data-mdt-theme' || m.attributeName === 'data-mdt-custom-wallpaper') {
          run()
          return
        }
      }
    })
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-mdt-theme', 'data-mdt-custom-wallpaper'] })
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', observeTheme)
  } else {
    observeTheme()
  }
})()
