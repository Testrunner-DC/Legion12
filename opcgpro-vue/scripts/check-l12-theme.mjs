import { readFileSync } from 'node:fs'
const style = readFileSync(new URL('../src/style.css', import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const layer = style.slice(style.indexOf('@layer l12-theme{'))
const compact = layer.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\s+/g, '')
const checks = [
 ['does not fetch external fonts', !/@import[^;]*(?:fonts\.googleapis|font)/i.test(style) && !/fonts\.googleapis/i.test(style)],
 ['declares the approved local Chinese sans stack', /--l12-font-sans:[^;]*Microsoft YaHei[^;]*PingFang SC[^;]*Noto Sans SC[^;]*sans-serif/i.test(layer)],
 ['uses a named important layer against scoped shorthands', compact.includes('@layerl12-theme{') && compact.includes('font-family:var(--l12-font-sans)!important')],
 ['covers all elements, generated text and placeholders', compact.includes('*,*::before,*::after,*::placeholder{font-family:var(--l12-font-sans)!important;}')],
 ['preserves local font weight size and line height', !/(?:font-weight|font-size|line-height|\bfont)\s*:/i.test(layer)],
 ['requests a dark native widget palette', /color-scheme:\s*dark\s*!important/i.test(layer)],
 ['styles native select controls and popup entries', compact.includes('select,selectoption,selectoptgroup{') && compact.includes('background-color:var(--l12-native-control-bg)!important')],
 ['keeps native surface tokens dark', ['--l12-native-control-bg:#0b1011;', '--l12-native-control-bg-disabled:#111617;'].every(token=>compact.includes(token))],
 ['keeps select sizing contained and readable', compact.includes('min-width:0!important') && compact.includes('max-width:100%!important') && compact.includes('min-height:36px!important')],
 ['keeps checked focus and disabled states explicit', /select option:checked\s*\{/i.test(layer) && /select:focus-visible\s*\{/i.test(layer) && /select:disabled\s*\{/i.test(layer)],
 ['keeps native select semantics', !/appearance\s*:\s*none/i.test(layer)],
]
const failures = checks.filter(([, passed]) => !passed)
for (const [label, passed] of checks) console.log(`${passed ? 'PASS' : 'FAIL'} ${label}`)
if (failures.length) { console.error(`L12 theme contract failed: ${failures.length}/${checks.length}`); process.exit(1) }
console.log(`L12 theme contract passed: ${checks.length}/${checks.length}`)
