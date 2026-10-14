import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import ts from 'typescript'
import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse as parseTemplate } from '@vue/compiler-dom'

const root = path.resolve(import.meta.dirname, '..')
const sourcePath = path.join(root, 'src/l12/game/PromptOverlay.vue')
const source = fs.readFileSync(sourcePath, 'utf8').replace(/\r\n?/g, '\n')
const sfc = parseSfc(source, { filename: sourcePath })
assert.deepEqual(sfc.errors, [], 'PromptOverlay.vue must parse as an SFC')
assert(sfc.descriptor.scriptSetup, 'PromptOverlay.vue must retain script setup')
assert(sfc.descriptor.template, 'PromptOverlay.vue must retain a template')

const script = sfc.descriptor.scriptSetup.content
const file = ts.createSourceFile('PromptOverlay.ts', script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
assert.deepEqual(file.parseDiagnostics, [], 'PromptOverlay script setup must parse as TypeScript')

const declarations = new Map()
for (const statement of file.statements) {
  if (ts.isFunctionDeclaration(statement) && statement.name)
    declarations.set(statement.name.text, statement)
  if (ts.isVariableStatement(statement)) {
    for (const declaration of statement.declarationList.declarations)
      if (ts.isIdentifier(declaration.name)) declarations.set(declaration.name.text, statement)
  }
}
const required = ['declineChoices', 'isDeclineChoiceForPrompt', 'isActivationCancellationSelection', 'isPromptSelectionSubmittable']
for (const name of required) assert(declarations.has(name), `Missing actual PromptOverlay declaration: ${name}`)

function calls(node, functionName) {
  let found = false
  function visit(current) {
    if (ts.isCallExpression(current) && ts.isIdentifier(current.expression)
      && current.expression.text === functionName) found = true
    ts.forEachChild(current, visit)
  }
  visit(node)
  return found
}
assert(calls(declarations.get('isPromptSelectionSubmittable'), 'isActivationCancellationSelection'),
  'The shared validity helper must explicitly recognize protocol activation cancellation')
assert(calls(declarations.get('confirm'), 'isPromptSelectionSubmittable'),
  'confirm() must use the shared validity helper')

const templateAst = parseTemplate(sfc.descriptor.template.content, { comments: false })
const confirmBindings = []
function walkTemplate(node) {
  if (node.type === 1 && node.tag === 'button') {
    const classAttribute = node.props.find(prop => prop.type === 6 && prop.name === 'class')
    const classes = classAttribute?.value?.content?.split(/\s+/) ?? []
    if (classes.includes('prompt-confirm-choice')) {
      const disabled = node.props.find(prop => prop.type === 7 && prop.name === 'bind'
        && prop.arg?.isStatic && prop.arg.content === 'disabled')
      confirmBindings.push(disabled?.exp?.content ?? '')
    }
  }
  for (const child of node.children ?? []) walkTemplate(child)
}
walkTemplate(templateAst)
assert.equal(confirmBindings.length, 2, 'Trigger-order and ordinary confirmation buttons must remain distinct')
assert.equal(confirmBindings.filter(value => value.includes('isPromptSelectionSubmittable')).length, 1,
  'The ordinary confirmation button must use the same shared validity helper exactly once')
assert(confirmBindings.every(value => value.includes('l12State.pendingAction')),
  'Every confirmation button must retain the pending-action lock')

const declarationText = name => declarations.get(name).getText(file)
const harness = `
const naturalChoiceLabel = (value, fallback) => typeof value === 'string' && value.trim() ? value.trim() : fallback;
${declarationText('declineChoices')}
${declarationText('isDeclineChoiceForPrompt')}
${declarationText('isActivationCancellationSelection')}
${declarationText('isPromptSelectionSubmittable')}
export { isPromptSelectionSubmittable };
`
const javascript = ts.transpileModule(harness, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const { isPromptSelectionSubmittable } = await import(`data:text/javascript;base64,${Buffer.from(javascript).toString('base64')}`)

const prompt = (extra = {}) => ({
  promptId: 'prompt-1', playerIndex: 0, kind: 'order', text: 'synthetic',
  validChoices: ['a', 'b', 'c', 'd', 'skip'], minChoose: 4, maxChoose: 4,
  activationId: 'activation-1', choiceLabels: { skip: '不发动' }, data: {}, ...extra,
})
const matrix = [
  ['activation skip alone bypasses four-card cardinality', prompt(), ['skip'], true],
  ['skip mixed with one real choice is rejected', prompt(), ['skip', 'a'], false],
  ['skip mixed to the nominal exact count is rejected', prompt(), ['skip', 'a', 'b', 'c'], false],
  ['skip mixed above max is rejected', prompt(), ['skip', 'a', 'b', 'c', 'd'], false],
  ['missing activation identity cannot bypass cardinality', prompt({ activationId: null }), ['skip'], false],
  ['skip absent from valid choices cannot bypass cardinality', prompt({ validChoices: ['a', 'b', 'c', 'd'] }), ['skip'], false],
  ['ordinary exact selection still requires exactly four', prompt(), ['a', 'b', 'c'], false],
  ['ordinary exact selection still accepts exactly four', prompt(), ['a', 'b', 'c', 'd'], true],
  ['ordinary ranged selection accepts an in-range count', prompt({ validChoices: ['a', 'b', 'c'], minChoose: 2, maxChoose: 3, activationId: null }), ['a', 'b'], true],
  ['ordinary ranged selection rejects an underflow', prompt({ validChoices: ['a', 'b', 'c'], minChoose: 2, maxChoose: 3, activationId: null }), ['a'], false],
  ['NotAllowed-style prompt without skip rejects skip', prompt({ validChoices: ['a', 'b'], minChoose: 2, maxChoose: 2 }), ['skip'], false],
  ['a natural-language decline label cannot invent cancellation', prompt({ validChoices: ['decline-looking', 'b'], minChoose: 2, maxChoose: 2, choiceLabels: { 'decline-looking': '不发动' } }), ['decline-looking'], false],
  ['invalid ordinary choice is rejected even at valid cardinality', prompt({ validChoices: ['a', 'b'], minChoose: 2, maxChoose: 2, activationId: null }), ['a', 'foreign'], false],
  ['duplicate choices are rejected rather than satisfying exact cardinality', prompt({ validChoices: ['a', 'b'], minChoose: 2, maxChoose: 2, activationId: null }), ['a', 'a'], false],
]
for (const [name, value, selection, expected] of matrix)
  assert.equal(isPromptSelectionSubmittable(value, selection), expected, name)

const serverRoot = path.resolve(root, '../服务端WebSocket/TwelveLegions')
const sameTypeSites = []
for (const name of fs.readdirSync(serverRoot).filter(name => name.endsWith('.cs')).sort()) {
  const text = fs.readFileSync(path.join(serverRoot, name), 'utf8').replace(/\r\n?/g, '\n')
  const lines = text.split('\n')
  lines.forEach((line, index) => {
    if (/\bGrave(?:Cost|Effect)SelectionStep\s*\(/.test(line)
      || /\bMinChoose\s*=\s*(?:[2-9]|[1-9]\d+)/.test(line)
      || /PublicTriggerStep\s*\(\s*"order"/.test(line)) {
      const context = lines.slice(Math.max(0, index - 2), Math.min(lines.length, index + 5)).join(' ').trim()
      sameTypeSites.push({ file: name, line: index + 1, context })
    }
  })
}
assert(sameTypeSites.length >= 15, 'The complete shared-handler scan must not collapse to the reported card')
const scanOutput = process.env.L12_ACTIVATION_CANCELLATION_SCAN_OUT
if (scanOutput) {
  assert(!fs.existsSync(scanOutput), `CreateNew guard: ${scanOutput}`)
  fs.mkdirSync(path.dirname(scanOutput), { recursive: true })
  fs.writeFileSync(scanOutput, `${JSON.stringify({
    schemaVersion: 1,
    query: ['GraveCostSelectionStep(', 'GraveEffectSelectionStep(', 'MinChoose >= 2', 'PublicTriggerStep("order"'],
    interpretation: 'Raw current-source candidate inventory. A site is affected only when its runtime prompt also exposes skip and minChoose exceeds one; NotAllowed and runtime-min-one sites remain controls.',
    sites: sameTypeSites,
  }, null, 2)}\n`, { flag: 'wx' })
}

console.log(`Activation cancellation selection: ${matrix.length}/${matrix.length}; template 2/2; same-type candidates ${sameTypeSites.length}`)
