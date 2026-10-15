function objectPositionFraction(token: string | undefined, fallback: number) {
  if (!token) return fallback
  if (token === 'left' || token === 'top') return 0
  if (token === 'right' || token === 'bottom') return 1
  if (token === 'center') return .5
  const percentage = token.match(/^(-?\d+(?:\.\d+)?)%$/)?.[1]
  return percentage === undefined ? fallback : Number(percentage) / 100
}

/**
 * Replace every decoded image in an authoritative card clone with a canvas
 * containing the pixels that are already visible in the live source. A cloned
 * <img> loses its decode state until the browser loads it again; preserving the
 * current pixels lets presentation layers take ownership synchronously without
 * hiding the authority early or depending on a second network/decode pass.
 */
export function snapshotDecodedAuthoritativeCardVisual(sourceRoot: ParentNode, cloneRoot: ParentNode) {
  const sources = [...sourceRoot.querySelectorAll<HTMLImageElement>('img')]
  const clones = [...cloneRoot.querySelectorAll<HTMLImageElement>('img')]
  if (!sources.length || sources.length !== clones.length) return false
  const captures = sources.map((source, index) => {
    // offset geometry excludes rotation applied by an ancestor card. Viewport
    // geometry would swap width/height for a rested source, then distort it
    // again when the visual owner applies the source rotation.
    const layoutWidth = source.offsetWidth
    const layoutHeight = source.offsetHeight
    if (!source.complete || source.naturalWidth <= 0 || source.naturalHeight <= 0
      || layoutWidth <= 0 || layoutHeight <= 0) return null
    const scale = Math.max(1, window.devicePixelRatio || 1)
    const width = Math.max(1, Math.round(layoutWidth * scale))
    const height = Math.max(1, Math.round(layoutHeight * scale))
    const canvas = document.createElement('canvas')
    canvas.dataset.l12PixelSnapshot = 'true'
    canvas.width = width
    canvas.height = height
    for (const attribute of [...clones[index]!.attributes]) {
      if (!['src', 'srcset', 'loading', 'decoding', 'fetchpriority'].includes(attribute.name))
        canvas.setAttribute(attribute.name, attribute.value)
    }
    const context = canvas.getContext('2d')
    if (!context) return null
    const style = getComputedStyle(source)
    if (style.backgroundColor && style.backgroundColor !== 'rgba(0, 0, 0, 0)') {
      context.fillStyle = style.backgroundColor
      context.fillRect(0, 0, width, height)
    }
    const sourceRatio = source.naturalWidth / source.naturalHeight
    const targetRatio = width / height
    const fit = style.objectFit
    let drawWidth = width
    let drawHeight = height
    if (fit === 'contain' || fit === 'scale-down' || fit === 'cover') {
      const useWidth = fit === 'cover' ? sourceRatio < targetRatio : sourceRatio > targetRatio
      if (useWidth) drawHeight = width / sourceRatio
      else drawWidth = height * sourceRatio
      if (fit === 'scale-down' && source.naturalWidth <= width && source.naturalHeight <= height) {
        drawWidth = source.naturalWidth
        drawHeight = source.naturalHeight
      }
    }
    const [positionX, positionY] = style.objectPosition.split(/\s+/)
    const x = (width - drawWidth) * objectPositionFraction(positionX, .5)
    const y = (height - drawHeight) * objectPositionFraction(positionY, .5)
    try { context.drawImage(source, x, y, drawWidth, drawHeight) } catch { return null }
    return { clone: clones[index]!, canvas }
  })
  if (captures.some(capture => !capture)) return false
  for (const capture of captures) capture!.clone.replaceWith(capture!.canvas)
  return true
}
