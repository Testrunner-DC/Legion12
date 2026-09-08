import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const srcRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../src')
function walk(dir) {
  return fs.readdirSync(dir, { withFileTypes: true })
    .flatMap(item => item.isDirectory() ? walk(path.join(dir, item.name)) : [path.join(dir, item.name)])
}

const files = [...walk(path.join(srcRoot, 'l12')).filter(file => /\.(vue|css)$/.test(file)), path.join(srcRoot, 'style.css')]
const findings = []
const inventory = { display: 0, copy: 0, meta: 0, micro: 0, adaptive: 0 }

for (const file of files) {
  const source = fs.readFileSync(file, 'utf8')
  const relative = path.relative(srcRoot, file)
  const legacyPatterns = [
    ['legacy board-readable token', /--l12-board-readable/g],
    ['legacy project-wide 14px wrapper', /max\(14px,var\(--l12-board-[^)]+\)\)/g],
    ['blanket page-wide forced font size', /\.[\w-]+-page\s+[^{}]+(?:,[^{}]+){3,}\{[^{}]*font-size\s*:\s*14px\s*!important/gi],
  ]
  for (const [label, pattern] of legacyPatterns) {
    const matches = source.match(pattern) ?? []
    if (matches.length) findings.push(`${relative}: ${label} (${matches.length})`)
  }

  for (const match of source.matchAll(/\b(?:font-size|font)\s*:\s*([^;}]+)/g)) {
    const value = match[1]
    const numeric = value.match(/(?:^|[\s,(])((?:\d*\.)?\d+)(px|rem)\b/)
    if (numeric) {
      const px = Number(numeric[1]) * (numeric[2] === 'rem' ? 16 : 1)
      if (px <= 0) findings.push(`${relative}: non-positive font size (${numeric[0].trim()})`)
      else if (px >= 18) inventory.display++
      else if (px >= 13) inventory.copy++
      else if (px >= 10) inventory.meta++
      else inventory.micro++
    }
    if (/clamp\(|--l12-board-(?:copy|meta|micro)|--l12-effect-copy/.test(value)) inventory.adaptive++
  }
}

const globalStyle = fs.readFileSync(path.join(srcRoot, 'style.css'), 'utf8')
const gameBoard = fs.readFileSync(path.join(srcRoot, 'l12/game/GameBoard.vue'), 'utf8')
const contracts = [
  [gameBoard.includes("'--l12-board-copy'") && gameBoard.includes("'--l12-board-meta'") && gameBoard.includes("'--l12-board-micro'") && gameBoard.includes("'--l12-effect-copy'"), 'board scale must publish copy, metadata, micro-label and effect-copy tiers'],
  [globalStyle.includes('.l12-effect-body{font-size:var(--l12-effect-copy,clamp(') && globalStyle.includes('overflow-wrap:anywhere;white-space:pre-wrap'), 'effect prose must use its adaptive semantic tier and preserve authoritative wrapping'],
  [globalStyle.includes('small { font-size:var(--l12-board-meta,clamp(') && globalStyle.includes('.kicker {') && globalStyle.includes('--l12-board-meta'), 'auxiliary labels must use the metadata tier rather than inherit a project-wide body floor'],
]
for (const [ok, message] of contracts) if (!ok) findings.push(`semantic contract: ${message}`)

console.log(`Semantic typography inventory: display=${inventory.display}, copy=${inventory.copy}, meta=${inventory.meta}, micro=${inventory.micro}, adaptive=${inventory.adaptive}`)
if (findings.length) {
  console.error(findings.join('\n'))
  process.exitCode = 1
} else {
  console.log('L12 semantic typography and containment contracts passed; no project-wide 14px floor remains.')
}
