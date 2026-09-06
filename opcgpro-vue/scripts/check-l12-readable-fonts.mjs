import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../src')
const fix = process.argv.includes('--fix')
const boardScale = process.argv.includes('--board-scale')
const skip = new Set((process.env.L12_FONT_SKIP || '').split(',').filter(Boolean))
function walk(dir) { return fs.readdirSync(dir, {withFileTypes:true}).flatMap(item => item.isDirectory() ? walk(path.join(dir,item.name)) : [path.join(dir,item.name)]) }
const files = [...walk(path.join(root,'l12')).filter(file => /\.(vue|css)$/.test(file)), path.join(root,'style.css')]
let count = 0
let changed = 0
for (const file of files) {
  if (skip.has(path.basename(file))) continue
  const source = fs.readFileSync(file,'utf8')
  let local = 0
  function css(input) {
    return input.replace(/\b(font-size|font)\s*:\s*([^;}]+)/g, (declaration, prop, value) => {
      // Only CSS font size tokens: never change weights, line heights, images or layout lengths.
      const next = value.replace(/(^|[\s,(])((?:\d*\.)?\d+)(px|rem)\b/, (token, prefix, raw, unit) => {
        const size = Number(raw) * (unit === 'rem' ? 16 : 1)
        if (boardScale && size > 0 && !value.includes('--l12-board-readable') && !value.includes('clamp(')
          && (file.endsWith('style.css') || file.includes(path.join('l12','game')) || file.endsWith('CardTile.vue'))) {
          local++; return prefix + 'max(' + Math.max(14,size) + 'px,var(--l12-board-readable,14px))'
        }
        if (size === 0 || size >= 14) return token
        local++; return prefix + '14px'
      })
      return declaration.slice(0,declaration.indexOf(':')+1) + (declaration.includes(': ') ? ' ' : '') + next
    })
  }
  const next = file.endsWith('.vue')
    ? source.replace(/<style\b[^>]*>[\s\S]*?<\/style>/g, css)
    : css(source)
  if (local) {
    count += local; changed++
    console.log(path.relative(root,file) + ': ' + local)
    if (fix) fs.writeFileSync(file,next)
  }
}
console.log((fix ? 'Raised' : 'Below minimum') + ': ' + count + ' CSS declarations in ' + changed + ' files; baseline 14px')
if (!fix && count) process.exitCode = 1
