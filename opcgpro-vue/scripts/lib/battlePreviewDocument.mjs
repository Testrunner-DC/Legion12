function parseAttributes(tag) {
  const attributes = new Map()
  const source = tag.replace(/^<meta\b/i, '').replace(/\/?>\s*$/, '')
  const pattern = /([^\s=/>]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+)))?/g
  for (const match of source.matchAll(pattern)) {
    const name = match[1].toLowerCase()
    if (attributes.has(name)) throw new Error(`Viewport meta repeats the ${name} attribute`)
    attributes.set(name, match[2] ?? match[3] ?? match[4] ?? '')
  }
  return attributes
}

function productionViewportMeta(productionIndexHtml) {
  if (typeof productionIndexHtml !== 'string') throw new TypeError('Production index HTML must be a string')
  const viewportTags = [...productionIndexHtml.matchAll(/<meta\b[^>]*>/gi)]
    .map(match => ({ tag: match[0], attributes: parseAttributes(match[0]) }))
    .filter(({ attributes }) => attributes.get('name')?.toLowerCase() === 'viewport')
  if (viewportTags.length !== 1)
    throw new Error(`Production index must contain exactly one viewport meta; found ${viewportTags.length}`)

  const [{ tag, attributes }] = viewportTags
  const content = attributes.get('content')
  if (!content) throw new Error('Production viewport meta must have content')
  const directives = new Map()
  for (const part of content.split(',')) {
    const directive = part.trim()
    if (!directive) continue
    const separator = directive.indexOf('=')
    const name = (separator < 0 ? directive : directive.slice(0, separator)).trim().toLowerCase()
    const value = (separator < 0 ? '' : directive.slice(separator + 1)).trim().toLowerCase()
    if (!name || directives.has(name))
      throw new Error(`Production viewport meta repeats or omits a directive name: ${name || '<empty>'}`)
    directives.set(name, value)
  }
  if (directives.get('width') !== 'device-width')
    throw new Error('Production viewport meta must declare width=device-width')
  if (!directives.has('initial-scale') || Number(directives.get('initial-scale')) !== 1)
    throw new Error('Production viewport meta must declare initial-scale=1')
  return tag
}

export function buildBattlePreviewDocument(productionIndexHtml) {
  const viewportMeta = productionViewportMeta(productionIndexHtml)
  return `<!doctype html>
<html lang="zh">
  <head>
    <meta charset="UTF-8" />
    ${viewportMeta}
  </head>
  <body>
    <div id="l12-landscape-teleports"></div>
    <div id="app"></div>
    <script type="module" src="/@vite/client"></script>
    <script type="module" src="/__l12_battle_preview__.js"></script>
  </body>
</html>`
}
