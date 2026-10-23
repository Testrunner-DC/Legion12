import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse } from '@vue/compiler-dom'
import ts from 'typescript'

const classTokens = node => node.props?.find(prop => prop.type === 6 && prop.name === 'class')
  ?.value?.content?.split(/\s+/) ?? []
const bound = (node, name, expected) => node.props?.filter(prop => prop.type === 7
  && prop.name === 'bind' && prop.arg?.isStatic && prop.arg.content === name
  && prop.exp?.content?.trim() === expected).length === 1
const staticValue = (node, name) => node.props?.find(prop => prop.type === 6 && prop.name === name)?.value?.content
const hidden = node => node.props?.some(prop => prop.type === 6 && (prop.name === 'hidden'
  || prop.name === 'style' && /display\s*:\s*none|visibility\s*:\s*hidden/i.test(prop.value?.content ?? ''))
  || prop.type === 7 && ['if', 'else-if', 'else', 'show', 'for', 'html', 'text'].includes(prop.name)
  || prop.type === 7 && prop.name === 'bind' && (!prop.arg || ['style', 'hidden'].includes(prop.arg.content)))

function template(source) {
  const sfc = parseSfc(source)
  if (sfc.errors.length || !sfc.descriptor.template) return null
  try { return parse(sfc.descriptor.template.content, { comments: false }) } catch { return null }
}

function findAll(root, predicate) {
  const result = []
  function visit(node, ancestors) {
    if (node.type === 1 && ['script', 'style'].includes(node.tag)) return
    if (node.type === 1 && predicate(node)) result.push({ node, ancestors })
    for (const child of node.children ?? []) visit(child, [...ancestors, node])
  }
  if (root) visit(root, [])
  return result
}

export function hasPersistentMobilePlayerIdentities(source) {
  const summaries = findAll(template(source), node => classTokens(node).includes('mobile-player-summary-strip'))
  if (summaries.length !== 1) return false
  const { node: summary } = summaries[0]
  const guards = summary.props.filter(prop => prop.type === 7)
  if (guards.length !== 1 || guards[0].name !== 'if'
    || guards[0].exp?.content?.trim() !== 'mobileLandscapeViewport'
    || summary.props.some(prop => prop.type === 6 && ['hidden', 'style'].includes(prop.name))) return false
  const identities = findAll(summary, node => node.tag === 'BattlePlayerIdentity')
  if (identities.length !== 2) return false
  return ['viewEnemy', 'viewMe'].every(player => identities.some(({ node, ancestors }) =>
    ancestors.every(parent => parent === summary || parent.type !== 1 || !hidden(parent))
    && !hidden(node) && bound(node, 'player', player)
    && bound(node, 'rank', player === 'viewEnemy' ? 'battleRank(enemyBadge)' : 'battleRank(myBadge)')
    && bound(node, 'connection-label', `connectionLabel(${player}.playerIndex)`)
    && node.props.some(prop => prop.type === 6 && prop.name === 'show-master-details')))
}

export function hasSharedBattleCardDetails(source) {
  const sfc = parseSfc(source)
  if (sfc.errors.length || !sfc.descriptor.scriptSetup) return false
  const file = ts.createSourceFile('GameBoard.ts', sfc.descriptor.scriptSetup.content,
    ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  if (file.parseDiagnostics.length) return false
  const aliases = new Map()
  let focusType
  let imported = false
  for (const statement of file.statements) {
    if (ts.isImportDeclaration(statement) && statement.moduleSpecifier.text === '../CardDetailContent.vue'
      && statement.importClause?.name?.text === 'CardDetailContent') imported = true
    if (ts.isTypeAliasDeclaration(statement)) aliases.set(statement.name.text, statement.type)
    if (ts.isVariableStatement(statement)) for (const declaration of statement.declarationList.declarations) {
      if (declaration.name.getText(file) === 'focusDetailCard'
        && declaration.initializer && ts.isCallExpression(declaration.initializer)
        && declaration.initializer.expression.getText(file) === 'computed')
        focusType = declaration.initializer.typeArguments?.[0]
    }
  }
  function includesDeckCard(type, visited = new Set()) {
    if (!type) return false
    if (ts.isUnionTypeNode(type) || ts.isIntersectionTypeNode(type))
      return type.types.some(member => includesDeckCard(member, visited))
    if (!ts.isTypeReferenceNode(type)) return false
    const name = type.typeName.getText(file)
    if (name === 'DeckCard') return true
    if (visited.has(name)) return false
    return includesDeckCard(aliases.get(name), new Set([...visited, name]))
  }
  if (!imported || !includesDeckCard(focusType)) return false
  const details = findAll(template(source), node => node.tag === 'CardDetailContent')
  if (details.length !== 2) return false
  return ['card-inspector', 'mobile-card-detail-body'].every(container => details.some(({ node, ancestors }) =>
    bound(node, 'card', 'focusDetailCard') && bound(node, 'show-catalog-only', 'false')
    && ancestors.some(parent => classTokens(parent).includes(container))
    && !node.props.some(prop => prop.type === 7 && prop.name === 'bind' && !prop.arg)))
    && !findAll(template(source), node => classTokens(node)
      .some(token => token === 'inspector-effect' || token === 'mobile-inspector-effect')).length
}

export function hasProportionalMobileActionSize(style) {
  const withoutComments = style.replace(/\/\*[\s\S]*?\*\//g, '')
  const declarations = [...withoutComments.matchAll(/--l12-mobile-action-h\s*:\s*([^;{}]+);/g)]
  return declarations.length === 1
    && /^clamp\(40px,\s*calc\(var\(--l12-viewport-height,100vh\)\s*\*\s*\.11\),\s*44px\)$/.test(declarations[0][1].trim())
    && /min-height\s*:\s*var\(--l12-mobile-action-h\)\s*!important/.test(withoutComments)
}
