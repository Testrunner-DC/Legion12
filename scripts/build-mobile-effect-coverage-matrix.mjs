import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { buildMobileEffectMatrix } from './lib/l12-mobile-effect-matrix.mjs'

const root = path.resolve(import.meta.dirname, '..')
const [inventoryPath, promptMapPath, output] = process.argv.slice(2)
assert([inventoryPath, promptMapPath, output].every(value => value && path.isAbsolute(value)), 'Explicit absolute inventory, current prompt map and output are required')
const pwsh = process.env.L12_PWSH
assert(pwsh && path.isAbsolute(pwsh) && fs.existsSync(pwsh), 'Initialized L12_PWSH required')
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command', '$ErrorActionPreference="Stop"; . $env:L12_MATRIX_GUARD; Assert-L12GeneratedPath -Path $env:L12_MATRIX_OUT -Purpose "full card-effect touch matrix" | Out-Null'],
  { env: { ...process.env, L12_MATRIX_GUARD: path.join(root, 'ops/windows/L12GeneratedStorage.ps1'), L12_MATRIX_OUT: output }, stdio: 'pipe', timeout: 15_000, windowsHide: true })
assert(!fs.existsSync(output) || fs.readdirSync(output).length === 0, 'Refusing to overwrite matrix evidence')
const inventory = JSON.parse(fs.readFileSync(inventoryPath, 'utf8'))
const [header, ...lines] = fs.readFileSync(promptMapPath, 'utf8').trim().split(/\r?\n/)
const columns = header.split('\t')
const prompts = lines.map(line => { const values = line.split('\t'); assert.equal(values.length, columns.length); return Object.fromEntries(columns.map((column, index) => [column, values[index]])) })
const data = path.join(root, '服务端WebSocket', 'TwelveLegions', 'Data')
const catalogPaths = ['cards.s1.json', 'cards.s2.json', 'cards.st.json'].map(file => path.join(data, file))
const catalog = catalogPaths.flatMap(file => JSON.parse(fs.readFileSync(file, 'utf8')))
const result = buildMobileEffectMatrix(inventory, catalog, prompts)
assert.equal(result.counts.abilities, 686); assert.equal(result.counts.cards, 324); assert.equal(prompts.length, 126)
const sha = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')
result.sources = [inventoryPath, promptMapPath, ...catalogPaths, import.meta.filename, path.join(root, 'scripts/lib/l12-mobile-effect-matrix.mjs'), path.join(root, 'scripts/lib/l12-presentation-branches.mjs')].map(file => ({ file, sha256: sha(file) }))
result.sourceCommit = process.env.L12_SOURCE_COMMIT || null
fs.mkdirSync(output, { recursive: true })
fs.writeFileSync(path.join(output, 'coverage-matrix.json'), JSON.stringify(result, null, 2), { flag: 'wx' })
const tsv = value => String(value ?? '').replaceAll('\t', ' ').replace(/\r?\n/g, ' ↵ ')
const table = [['稳定能力ID', '卡牌', '时点', '执行模型', '费用', '预期结果', '预期语义状态', '分支精确条件', '静态归属', '完整引擎触摸实际结果'], ...result.rows.map(row => [row.abilityId, row.cardName, row.trigger, row.executionModel, row.costText, row.expectedResultText, row.expectedSemanticsStatus, JSON.stringify(row.branches), row.evidenceLayers.staticOwnership.entryEvidence, '待映射具名执行证据，不等于缺陷或已通过'])]
fs.writeFileSync(path.join(output, 'coverage-matrix.tsv'), table.map(row => row.map(tsv).join('\t')).join('\n') + '\n', { flag: 'wx' })
console.log(JSON.stringify({ status: result.status, ...result.counts, triggerTypes: result.axes.triggers.length, models: result.axes.executionModels.length, atomKinds: result.axes.atomKinds.length, promptCallSites: prompts.length, output }, null, 2))
