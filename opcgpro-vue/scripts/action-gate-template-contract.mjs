import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse } from '@vue/compiler-dom'
import ts from 'typescript'

const unwrap = node => ts.isParenthesizedExpression(node) ? unwrap(node.expression) : node
function expressionKey(value) {
  if (!value) return null
  const file = ts.createSourceFile('binding.js', `(${value})`, ts.ScriptTarget.Latest, true, ts.ScriptKind.JS)
  if (file.parseDiagnostics.length || file.statements.length !== 1 || !ts.isExpressionStatement(file.statements[0])) return null
  return ts.createPrinter({ removeComments: true }).printNode(ts.EmitHint.Expression, unwrap(file.statements[0].expression), file)
}
const expressionIs = (value, expected) => expressionKey(value) === expressionKey(expected)
const classes = node => node.props?.find(prop => prop.type === 6 && prop.name === 'class')?.value?.content.split(/\s+/) || []
const hasClass = (node, name) => classes(node).includes(name)
const directive = (node, name, arg) => (node.props || []).filter(prop => prop.type === 7 && prop.name === name
  && (arg === undefined ? !prop.arg : prop.arg?.isStatic && prop.arg.content === arg))

function parseTemplate(source) {
  try {
    const sfc = parseSfc(source)
    if (sfc.errors.length || !sfc.descriptor.template || sfc.descriptor.template.src) return null
    const ast = parse(sfc.descriptor.template.content, { comments: false })
    const nodes = [], parents = new Map()
    function walk(node, ancestors = []) {
      if (node.type === 1 && ['script', 'style'].includes(node.tag.toLowerCase())) return
      if (node.type === 1) { nodes.push(node); parents.set(node, ancestors) }
      for (const child of node.children || []) walk(child, [...ancestors, node])
    }
    walk(ast)
    return { nodes, parents }
  } catch { return null }
}

// Dynamic business branches/loops remain allowed. This only rejects definitely
// non-rendering decoys; geometry and computed CSS keep their existing guards.
function visiblePath(node, parsed) {
  return [...parsed.parents.get(node), node].every(current => current.type !== 1 || !(current.props || []).some(prop => {
    if (prop.type === 6) return ['hidden', 'inert'].includes(prop.name)
      || prop.name === 'style' && /(?:^|;)\s*(?:display\s*:\s*none|visibility\s*:\s*hidden)(?:\s*!important)?\s*(?:;|$)/i.test(prop.value?.content || '')
    return prop.type === 7 && ['if', 'else-if', 'show'].includes(prop.name)
      && ['false', '0', 'null', 'undefined', "''", '""'].includes(expressionKey(prop.exp?.content))
  }))
}

function noOverride(node, names) {
  return !(node.props || []).some(prop => prop.type === 7
    && (prop.name === 'bind' && (!prop.arg || !prop.arg.isStatic || names.includes(prop.arg.content))
      || prop.name === 'on' && (!prop.arg || !prop.arg.isStatic)))
}
function binding(node, name, expected) {
  const matches = directive(node, 'bind', name)
  return noOverride(node, []) && matches.length === 1 && !matches[0].modifiers.length
    && !(node.props || []).some(prop => prop.type === 6 && prop.name === name)
    && expressionIs(matches[0].exp?.content, expected)
}
export const publicContentSaveDisabledBinding = 'loading || !readGeneration || refreshRequired || isPending(actionKey)'
function publicContentDisabled(node) {
  const value = directive(node, 'bind', 'disabled')[0]?.exp?.content
  if (!value || !binding(node, 'disabled', value)) return false
  const file = ts.createSourceFile('public-content-disabled.ts', `(${value})`, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  if (file.parseDiagnostics.length || file.statements.length !== 1 || !ts.isExpressionStatement(file.statements[0])) return false
  const terms = []
  function collect(raw) {
    const expression = unwrap(raw)
    if (ts.isBinaryExpression(expression) && expression.operatorToken.kind === ts.SyntaxKind.BarBarToken) {
      collect(expression.left); collect(expression.right)
    } else terms.push(expressionKey(expression.getText(file)))
  }
  collect(file.statements[0].expression)
  const expected = ['loading', '!readGeneration', 'refreshRequired', 'isPending(actionKey)'].map(expressionKey).sort()
  return terms.length === expected.length && terms.sort().every((term, index) => term === expected[index])
}
function click(node, expected) {
  const handlers = directive(node, 'on', 'click')
  return noOverride(node, ['onClick', 'onclick']) && handlers.length === 1
    && !(node.props || []).some(prop => prop.type === 6 && prop.name.toLowerCase() === 'onclick')
    && handlers[0].modifiers.every(modifier => ['stop', 'prevent'].includes(modifier.content))
    && expressionIs(handlers[0].exp?.content, expected)
}
function unique(nodes, predicate) {
  const matches = nodes.filter(predicate)
  return matches.length === 1 ? matches[0] : null
}
function host(parsed, tag, className) {
  return parsed && unique(parsed.nodes, node => node.tag === tag && hasClass(node, className) && visiblePath(node, parsed))
}
const under = (node, owner, parsed) => parsed.parents.get(node).includes(owner)
const friendActions = [
  ['invite(selected)', 'selected', 'main', null],
  ['spectate(selected)', 'selected', 'main', null],
  ['remove(selected)', 'selected', 'main', null],
  ['blockPlayer(selected)', 'selected', 'main', null],
  ['blockPlayer(player)', 'player', 'article', 'player in incoming'],
  ['resolve(player, false)', 'player', 'article', 'player in incoming'],
  ['resolve(player, true)', 'player', 'article', 'player in incoming'],
  ['add(player)', 'player', 'article', 'player in results'],
  ['unblock(player)', 'player', 'article', 'player in blocked'],
]
function hasOwner(node, root, parsed, tag, loop) {
  return parsed.parents.get(node).some(parent => parent.type === 1 && parent.tag === tag && under(parent, root, parsed)
    && (loop ? directive(parent, 'for').length === 1 && expressionIsLoop(directive(parent, 'for')[0].exp?.content, loop)
      : directive(parent, 'if').length === 1 && expressionIs(directive(parent, 'if')[0].exp?.content, 'selected')))
}
// v-for is not a JavaScript expression. Compare its alias/source expressions,
// rather than accepting an identically named player from another collection.
function expressionIsLoop(value, expected) {
  const split = text => text?.match(/^\s*(.*?)\s+(?:in|of)\s+(.+?)\s*$/s)
  const actual = split(value), target = split(expected)
  return !!actual && !!target && expressionIs(actual[1], target[1]) && expressionIs(actual[2], target[2])
}

export function actionGateTemplateContract(friendsSource, publicSource, tournamentSource) {
  const friends = parseTemplate(friendsSource), publicDeck = parseTemplate(publicSource), tournament = parseTemplate(tournamentSource)
  const friendsRoot = host(friends, 'div', 'friends-page')
  const publicRoot = host(publicDeck, 'section', 'public-content-editor')
  const tournamentRoot = host(tournament, 'div', 'tournament-page')
  const friendsPending = !!friendsRoot && friendActions.every(([handler, player, tag, loop]) => {
    const control = unique(friends.nodes, node => node.tag === 'button' && click(node, handler)
      && under(node, friendsRoot, friends) && visiblePath(node, friends))
    return !!control && hasOwner(control, friendsRoot, friends, tag, loop)
      && binding(control, 'disabled', `actionPending(actionKey(${player}))`)
  })
  const publicControl = publicRoot && unique(publicDeck.nodes, node => node.tag === 'button'
    && click(node, 'saveContent') && under(node, publicRoot, publicDeck) && visiblePath(node, publicDeck))
  const tournamentControl = tournamentRoot && unique(tournament.nodes, node => node.tag === 'button'
    && click(node, 'join(item)') && under(node, tournamentRoot, tournament) && visiblePath(node, tournament))
  return {
    friendsPending,
    friendsBusy: !!friendsRoot && binding(friendsRoot, 'aria-busy', 'actionBusy'),
    publicPending: !!publicControl && publicContentDisabled(publicControl),
    tournamentPending: !!tournamentControl && hasOwner(tournamentControl, tournamentRoot, tournament, 'article', 'item in visibleTournaments')
      && binding(tournamentControl, 'disabled', 'actionPending(tournamentActionKey(item))'),
  }
}
