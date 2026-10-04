import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse } from '@vue/compiler-dom'
import ts from 'typescript'

const classes = node => node.props?.find(prop => prop.type === 6 && prop.name === 'class')?.value?.content?.split(/\s+/) || []
const attribute = (node, name) => node.props?.find(prop => prop.type === 6 && prop.name === name)?.value?.content
const directive = (node, name, argument) => node.props?.find(prop => prop.type === 7 && prop.name === name
  && (argument === undefined || (prop.arg?.isStatic && prop.arg.content === argument)))?.exp?.content
const unwrap = node => ts.isParenthesizedExpression(node) ? unwrap(node.expression) : node

function expression(value) {
  if (!value) return null
  const file = ts.createSourceFile('binding.js', `(${value})`, ts.ScriptTarget.Latest, true, ts.ScriptKind.JS)
  if (file.parseDiagnostics.length || file.statements.length !== 1 || !ts.isExpressionStatement(file.statements[0])) return null
  return unwrap(file.statements[0].expression)
}
const identifier = (node, name) => node && ts.isIdentifier(unwrap(node)) && unwrap(node).text === name
function pendingGuard(node) {
  if (!node) return false
  node = unwrap(node)
  if (ts.isPropertyAccessExpression(node)) return identifier(node.expression, 'l12State') && node.name.text === 'pendingAction'
  return ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.BarBarToken
    && (pendingGuard(node.left) || pendingGuard(node.right))
}
function supplementalBinding(node) {
  const loop = expression(directive(node, 'for'))
  return loop && ts.isBinaryExpression(loop) && loop.operatorToken.kind === ts.SyntaxKind.InKeyword
    && identifier(loop.left, 'choice') && identifier(loop.right, 'supplementalChoices')
}

// Vue <template> wrappers are transparent; real wrappers change footer ownership.
function renderedChildren(node) {
  return (node.children || []).flatMap(child => child.tag === 'template' ? renderedChildren(child) : child.type === 1 ? [child] : [])
}

function descendantButtons(node) {
  return (node.children || []).flatMap(child => [
    ...(child.tag === 'button' ? [child] : []), ...descendantButtons(child),
  ])
}

export function promptFooterContract(source) {
  const failed = { root: false, ordered: false, pending: false, noExtraDeclineRow: false }
  const sfc = parseSfc(source)
  if (sfc.errors.length || !sfc.descriptor.template) return failed
  let ast
  try { ast = parse(sfc.descriptor.template.content, { comments: false }) } catch { return failed }
  const nodes = []
  function walk(node) {
    if (node.type === 1) nodes.push(node)
    for (const child of node.children || []) walk(child)
  }
  walk(ast)
  const noExtraDeclineRow = !nodes.some(node => classes(node).includes('prompt-supplemental-choices'))
  const panels = nodes.filter(node => node.tag === 'section' && classes(node).includes('prompt-choice-panel')
    && identifier(expression(directive(node, 'else-if')), 'prompt'))
  if (panels.length !== 1) return { ...failed, noExtraDeclineRow }
  const footers = renderedChildren(panels[0]).filter(node => classes(node).includes('prompt-action-footer'))
  if (footers.length !== 1) return { ...failed, noExtraDeclineRow }
  const footer = footers[0]
  const condition = expression(directive(footer, 'if'))
  const root = footer.tag === 'footer' && attribute(footer, 'data-ui-contract') === 'equal-action-group'
    && Boolean(condition && ts.isPrefixUnaryExpression(condition) && condition.operator === ts.SyntaxKind.ExclamationToken
      && identifier(condition.operand, 'isPureEffectDecision'))
  const children = renderedChildren(footer)
  const supplemental = children.filter(node => classes(node).includes('prompt-footer-choice'))
  const confirms = children.filter(node => classes(node).includes('prompt-confirm-choice'))
  const choice = supplemental[0]
  const ordered = root && supplemental.length === 1 && choice.tag === 'button' && Boolean(supplementalBinding(choice))
    && confirms.length > 0 && confirms.every(node => node.tag === 'button' && node !== choice
      && children.indexOf(choice) < children.indexOf(node))
  const buttons = descendantButtons(footer)
  const pending = root && ordered && buttons.length > 1
    && buttons.every(node => pendingGuard(expression(directive(node, 'bind', 'disabled'))))
  return { root, ordered, pending, noExtraDeclineRow }
}
