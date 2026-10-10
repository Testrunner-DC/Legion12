import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse } from '@vue/compiler-dom'
import ts from 'typescript'

const source = readFileSync(new URL('../src/l12/site/RankingsPage.vue', import.meta.url), 'utf8')
const key = value => {
  const file = ts.createSourceFile('ranking-binding.ts', `(${value})`, ts.ScriptTarget.Latest, true)
  if (file.parseDiagnostics.length || file.statements.length !== 1) return null
  let expression = file.statements[0].expression
  while (expression && ts.isParenthesizedExpression(expression)) expression = expression.expression
  return expression && ts.createPrinter({ removeComments: true }).printNode(ts.EmitHint.Expression, expression, file)
}
const binding = (node, name, value) => {
  if (node.props.some(p => p.type === 7 && p.name === 'bind' && (!p.arg?.isStatic || !p.arg))) return false
  const matches = node.props.filter(p => p.type === 7 && p.name === 'bind' && p.arg?.content === name)
  return matches.length === 1 && matches[0].modifiers.length === 0 && key(matches[0].exp?.content) === key(value)
}
const attribute = (node, name, value) => !node.props.some(p => p.type === 7 && p.name === 'bind' && (!p.arg?.isStatic || p.arg.content === name))
  && node.props.filter(p => p.type === 6 && p.name === name && p.value?.content === value).length === 1
const hasClass = (node, name) => node.props.some(p => p.type === 6 && p.name === 'class' && p.value?.content.split(/\s+/).includes(name))
const event = (node, expression) => node.props.some(p => p.type === 7 && p.name === 'on' && p.arg?.isStatic && p.arg.content === 'click' && key(p.exp?.content) === key(expression))
function contract(input) {
  const parsed = parseSfc(input)
  if (parsed.errors.length || !parsed.descriptor.template) return false
  let tree
  try { tree = parse(parsed.descriptor.template.content, { comments: false }) } catch { return false }
  const nodes = []
  function walk(node, ancestors = []) {
    if (node.type === 1 && !['script', 'style'].includes(node.tag)) nodes.push({ node, ancestors })
    if (node.type === 1 && ['script', 'style'].includes(node.tag)) return
    for (const child of node.children || []) walk(child, [...ancestors, node])
  }
  walk(tree)
  const buttons = group => nodes.filter(({ node, ancestors }) => node.tag === 'button' && ancestors.some(p => p.type === 1 && hasClass(p, group))).map(p => p.node)
  const tabs = buttons('tabs'), ranges = buttons('ranges'), factions = buttons('faction-filter')
  const refresh = buttons('page-actions').filter(n => event(n, 'load'))
  const error = nodes.filter(({ node }) => node.tag === 'p' && hasClass(node, 'error') && node.props.some(p => p.type === 7 && p.name === 'if' && key(p.exp?.content) === key('error'))).map(p => p.node)
  return tabs.length === 4 && ['players', 'masters', 'matchups', 'history'].every(value => tabs.filter(n => event(n, `tab = '${value}'`) && binding(n, 'aria-pressed', `tab === '${value}'`)).length === 1)
    && ranges.length === 1 && binding(ranges[0], 'aria-pressed', 'range === item.id') && binding(ranges[0], 'disabled', "tab === 'history'") && event(ranges[0], 'range = item.id')
    && factions.length === 1 && binding(factions[0], 'aria-pressed', 'faction === item.id') && event(factions[0], 'faction = item.id')
    && refresh.length === 1 && binding(refresh[0], 'disabled', 'loading') && binding(refresh[0], 'aria-busy', 'loading || undefined')
    && error.length === 1 && attribute(error[0], 'role', 'alert') && attribute(error[0], 'aria-live', 'assertive')
}
let checks = 0
const accept = (input, why) => { assert.equal(contract(input), true, why); checks++ }
const reject = (before, after, why) => {
  assert(source.includes(before), `Mutation anchor missing: ${why}`)
  assert.equal(contract(source.replace(before, after)), false, why); checks++
}
accept(source, 'Real ranking controls expose their actual selected/busy/error state')
accept(source.replace(':aria-pressed="tab === \'players\'"', ':aria-pressed="( tab === \'players\' )"'), 'Equivalent binding parentheses/whitespace')
for (const value of ['players', 'masters', 'matchups', 'history']) reject(`:aria-pressed="tab === '${value}'"`, ':aria-pressed="true"', `No false pressed state for ${value}`)
reject(':aria-pressed="range === item.id"', ':aria-pressed="faction === item.id"', 'Range cannot borrow faction model')
reject(':aria-pressed="faction === item.id"', ':aria-pressed="range === item.id"', 'Faction cannot borrow range model')
reject(':disabled="tab === \'history\'"', '', 'History range remains unavailable')
reject(':aria-busy="loading || undefined"', ':aria-busy="undefined"', 'Loading is announced on real refresh control')
reject(':disabled="loading"', '', 'Busy refresh keeps native submission guard')
reject('@click="load"', '@click="tab = \'history\'"', 'Busy state cannot be borrowed by another action')
reject('class="error" role="alert" aria-live="assertive"', 'class="error" role="status" aria-live="polite"', 'Service error retains correct severity')
reject(':aria-pressed="range === item.id"', 'v-bind="override" :aria-pressed="range === item.id"', 'Dynamic bindings cannot override pressed state')
console.log(`E1 ranking state contract passed: ${checks} positive/negative cases`)
