import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse } from '@vue/compiler-dom'
import ts from 'typescript'

const visibilityDirectives = new Set(['if', 'else-if', 'else', 'show', 'for'])
const transparentTemplate = node => node?.type === 1 && node.tag === 'template' && node.props.length === 0
const classTokens = node => node.props?.find(prop => prop.type === 6 && prop.name === 'class')
  ?.value?.content?.split(/\s+/).filter(Boolean) || []
const unwrap = node => ts.isParenthesizedExpression(node) ? unwrap(node.expression) : node

function expressionKey(value) {
  if (!value) return null
  const file = ts.createSourceFile('ranked-brand-binding.js', `(${value})`, ts.ScriptTarget.Latest, true, ts.ScriptKind.JS)
  if (file.parseDiagnostics.length || file.statements.length !== 1 || !ts.isExpressionStatement(file.statements[0])) return null
  const expression = unwrap(file.statements[0].expression)
  return ts.createPrinter({ removeComments: true }).printNode(ts.EmitHint.Expression, expression, file)
}

const expressionIs = (value, expected) => expressionKey(value) === expressionKey(expected)

function renderedChildren(node) {
  return (node.children || []).flatMap(child => transparentTemplate(child)
    ? renderedChildren(child)
    : child.type === 1 && !['script', 'style'].includes(child.tag.toLowerCase()) ? [child] : [])
}

function staticAttribute(node, name, expected) {
  const matches = (node.props || []).filter(prop => prop.type === 6 && prop.name === name)
  return matches.length === 1 && matches[0].value?.content === expected
}

function hasVisualHidingOverride(node) {
  return (node.props || []).some(prop => {
    if (prop.type === 6 && prop.name === 'hidden') return true
    if (prop.type === 6 && prop.name === 'style') return /(?:display\s*:\s*none|visibility\s*:\s*hidden)/i.test(prop.value?.content || '')
    return prop.type === 7 && prop.name === 'bind'
      && (!prop.arg || !prop.arg.isStatic || ['hidden', 'style'].includes(prop.arg.content))
  })
}

const overridesContent = node => (node.props || []).some(prop =>
  prop.type === 7 && ['html', 'text'].includes(prop.name))

export function hasMasterTitleBrandImage(source) {
  const sfc = parseSfc(source)
  if (sfc.errors.length || !sfc.descriptor.template) return false
  let ast
  try { ast = parse(sfc.descriptor.template.content, { comments: false }) } catch { return false }

  const roots = renderedChildren(ast).filter(node => node.tag === 'span'
    && classTokens(node).includes('ranked-identity-badge'))
  if (roots.length !== 1) return false
  const [root] = roots
  if ((root.props || []).some(prop => prop.type === 7 && visibilityDirectives.has(prop.name))
    || hasVisualHidingOverride(root) || overridesContent(root)) return false

  const candidates = renderedChildren(root).filter(node => node.tag === 'img')
  if (candidates.length !== 1) return false
  const [image] = candidates

  const visibility = (image.props || []).filter(prop => prop.type === 7 && visibilityDirectives.has(prop.name))
  const condition = visibility.filter(prop => prop.name === 'if' && !prop.arg)
  if (visibility.length !== 1 || condition.length !== 1
    || !expressionIs(condition[0].exp?.content, "variant === 'master-title'")) return false

  const bindings = (image.props || []).filter(prop => prop.type === 7 && prop.name === 'bind')
  if (bindings.length !== 1 || !bindings[0].arg?.isStatic || bindings[0].arg.content !== 'src'
    || bindings[0].modifiers.length || !expressionIs(bindings[0].exp?.content, 'siteBrandIcon')) return false

  if ((image.props || []).some(prop => prop.type === 6 && prop.name === 'src')
    || hasVisualHidingOverride(image) || overridesContent(image)) return false
  const classAttributes = (image.props || []).filter(prop => prop.type === 6 && prop.name === 'class')
  return classAttributes.length === 1
    && classTokens(image).includes('identity-brand-logo')
    && staticAttribute(image, 'alt', '')
    && staticAttribute(image, 'aria-hidden', 'true')
}
