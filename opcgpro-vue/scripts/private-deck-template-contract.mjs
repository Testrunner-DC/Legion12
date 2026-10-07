import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse } from '@vue/compiler-dom'
import ts from 'typescript'

function component(source) {
  const sfc = parseSfc(source)
  if (sfc.errors.length || !sfc.descriptor.template || !sfc.descriptor.scriptSetup) return null
  const script = ts.createSourceFile('private-consumer.ts', sfc.descriptor.scriptSetup.content, ts.ScriptTarget.Latest, true)
  if (script.parseDiagnostics.length) return null
  const nodes = [], parents = new Map()
  function walk(node, ancestors = []) {
    if (node.type === 1 && !['style', 'script'].includes(node.tag)) { nodes.push(node); parents.set(node, ancestors) }
    for (const child of node.children || []) walk(child, [...ancestors, node])
  }
  try { walk(parse(sfc.descriptor.template.content, { comments: false })) } catch { return null }
  return { nodes, parents, script }
}
const tokens = node => node.props?.find(prop => prop.type === 6 && prop.name === 'class')?.value?.content?.split(/\s+/) || []
const has = (node, name) => tokens(node).includes(name)
const attr = (node, name) => node.props?.find(prop => prop.type === 6 && prop.name === name)?.value?.content
const directive = (node, name, argument) => node.props?.find(prop => prop.type === 7 && prop.name === name
  && (argument === undefined ? !prop.arg : prop.arg?.isStatic && prop.arg.content === argument))?.exp?.content
function expression(value) {
  if (!value) return ''
  const file = ts.createSourceFile('expression.ts', `(${value})`, ts.ScriptTarget.Latest, true)
  if (file.parseDiagnostics.length) return ''
  const statement = file.statements[0]
  if (!statement || !ts.isExpressionStatement(statement) || file.statements.length !== 1) return ''
  const normalized = ts.transform(statement.expression, [context => {
    const visit = node => ts.isParenthesizedExpression(node)
      ? ts.visitNode(node.expression, visit)
      : ts.visitEachChild(node, visit, context)
    return root => ts.visitNode(root, visit)
  }])
  try {
    // The printer restores necessary precedence parentheses. Never erase whitespace
    // inside literal values: 'not available' and 'notavailable' are not equivalent.
    const printed = ts.createPrinter({ removeComments: true }).printNode(ts.EmitHint.Expression, normalized.transformed[0], file)
    const scanner = ts.createScanner(ts.ScriptTarget.Latest, true, ts.LanguageVariant.Standard, printed)
    const tokens = []
    for (let kind = scanner.scan(); kind !== ts.SyntaxKind.EndOfFileToken; kind = scanner.scan())
      tokens.push([kind, scanner.getTokenText()])
    return JSON.stringify(tokens)
  } finally { normalized.dispose() }
}
const equal = (left, right) => Boolean(expression(left)) && expression(left) === expression(right)
function activeValue(node) {
  const raw = directive(node, 'bind', 'class')
  if (!raw) return ''
  const file = ts.createSourceFile('class.ts', `const value=${raw}`, ts.ScriptTarget.Latest, true)
  if (file.parseDiagnostics.length) return ''
  let value = file.statements[0]?.declarationList?.declarations[0]?.initializer
  while (value && ts.isParenthesizedExpression(value)) value = value.expression
  if (!value || !ts.isObjectLiteralExpression(value)) return ''
  const property = value.properties.find(item => ts.isPropertyAssignment(item) && ['active', "'active'", '"active"'].includes(item.name.getText(file)))
  return property?.initializer.getText(file) || ''
}
function descendants(parsed, root) { return parsed.nodes.filter(node => parsed.parents.get(node).includes(root)) }

export function privateEditorSavedPanelsContract(source) {
  const parsed = component(source)
  if (!parsed) return false
  const desktop = parsed.nodes.find(node => has(node, 'saved-decks-panel') && has(node, 'grand-panel')
    && parsed.parents.get(node).some(parent => has(parent, 'deck-side-column')))
  const mobile = parsed.nodes.find(node => has(node, 'mobile-saved-decks-dialog') && attr(node, 'role') === 'dialog'
    && attr(node, 'aria-modal') === 'true' && parsed.parents.get(node).some(parent => has(parent, 'mobile-saved-decks-mask')))
  if (!desktop || !mobile || parsed.nodes.some(node => node.tag === 'select'
    && parsed.parents.get(node).some(parent => has(parent, 'deck-builder-topbar')))) return false
  const selected = 'deck.id ? deck.id === activeDeckId : deck.name === activeDeckName'
  const desktopNodes = descendants(parsed, desktop), mobileNodes = descendants(parsed, mobile)
  const row = desktopNodes.find(node => node.tag === 'article' && equal(directive(node, 'for'), 'deck in savedDirectoryDecks'))
  const mobileRow = mobileNodes.find(node => node.tag === 'button' && equal(directive(node, 'for'), 'deck in savedDirectoryDecks'))
  if (!row || !mobileRow || !equal(activeValue(row), selected) || !equal(activeValue(mobileRow), selected)) return false
  if (!equal(directive(mobileRow, 'on', 'click'), 'chooseMobileSavedDeck(deck)')) return false
  const button = desktopNodes.find(node => node.tag === 'button' && parsed.parents.get(node).includes(row)
    && equal(directive(node, 'on', 'click'), 'requestLoadDeck(deck)'))
  if (!button) return false
  return [desktopNodes, mobileNodes].every(nodes => nodes.some(node => node.tag === 'DeckProfile'
    && equal(directive(node, 'bind', 'selected'), selected)
    && equal(directive(node, 'bind', 'meta'), 'savedDeckCountLabel(deck)')))
}

export function privateModeLegalityContract(source) {
  const parsed = component(source)
  if (!parsed) return false
  const declarations = parsed.script.statements.filter(ts.isVariableStatement).flatMap(node => node.declarationList.declarations)
  const init = name => declarations.find(node => node.name.getText(parsed.script) === name)?.initializer?.getText(parsed.script) || ''
  if (!equal(init('rows'), 'computed(() => authenticated.value ? serverRows.value : guestRows.value)')) return false
  if (!equal(init('noUsableGuestDeck'), 'computed(() => !authenticated.value && !props.loading && rows.value.length > 0 && rows.value.every(row => Boolean(row.error)))')) return false
  const guest = init('guestRows')
  const file = ts.createSourceFile('guest.ts', guest, ts.ScriptTarget.Latest, true)
  let validates = false
  function scan(node) {
    if (ts.isCallExpression(node) && node.expression.getText(file) === 'validateDeck'
      && node.arguments.length === 3 && equal(node.arguments[0].getText(file), 'deck')
      && equal(node.arguments[1].getText(file), 'props.catalog') && equal(node.arguments[2].getText(file), 'props.restrictions')) validates = true
    ts.forEachChild(node, scan)
  }
  scan(file)
  if (!validates) return false
  const rows = parsed.nodes.filter(node => node.tag === 'button' && equal(directive(node, 'for'), 'row in rows'))
  if (rows.length !== 1 || !equal(directive(rows[0], 'bind', 'disabled'), '!!row.error || disabled || confirming')) return false
  const status = descendants(parsed, rows[0]).find(node => has(node, 'legality'))
  const interpolation = status?.children?.find(node => node.type === 5)?.content?.content
  return equal(interpolation, 'rowStatus(row.deck, row.error)')
    && parsed.nodes.some(node => has(node, 'no-usable-deck') && equal(directive(node, 'if'), 'noUsableGuestDeck'))
    && parsed.nodes.some(node => has(node, 'selector-empty') && equal(directive(node, 'else-if'), '!rows.length && !directoryError'))
}

export function privateLibraryActionsContract(source) {
  const parsed = component(source)
  if (!parsed) return false
  const grid = parsed.nodes.find(node => node.tag === 'section' && has(node, 'mine-grid'))
  const row = grid && descendants(parsed, grid).find(node => node.tag === 'article'
    && equal(directive(node, 'for'), 'deck in pagedMine'))
  if (!row) return false
  const visible = node => [...parsed.parents.get(node), node].every(parent =>
    !(parent.props || []).some(prop => prop.type === 6 && ['hidden', 'inert'].includes(prop.name))
    && !['if', 'show'].some(name => equal(directive(parent, name), 'false')))
  return ['duplicateMine(deck)', 'copyMineCode(deck)', 'deleteMine(deck)'].every(handler => {
    const buttons = descendants(parsed, row).filter(node => node.tag === 'button'
      && equal(directive(node, 'on', 'click'), handler))
    return buttons.length === 1 && visible(buttons[0])
  }) && source.includes('window.confirm(message)')
}
