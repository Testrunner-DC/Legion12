import { createRequire } from 'node:module'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const frontendRoot = process.env.L12_SOURCE_ROOT
  ? resolve(process.env.L12_SOURCE_ROOT)
  : resolve(dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(join(frontendRoot, 'package.json'))
const { parse: parseSfc } = require('@vue/compiler-sfc')
const { parse } = require('@vue/compiler-dom')
const ts = require('typescript')

const visibilityNames = new Set(['if', 'else-if', 'else', 'show', 'for'])
const transparentTemplate = node => node?.type === 1 && node.tag === 'template' && node.props.length === 0
const componentTag = (node, pascal, kebab) => node.tag === pascal || node.tag === kebab
const classes = node => node.props?.find(prop => prop.type === 6 && prop.name === 'class')
  ?.value?.content?.split(/\s+/).filter(Boolean) || []
const hasClass = (node, name) => classes(node).includes(name)
const unwrap = node => node && ts.isParenthesizedExpression(node) ? unwrap(node.expression) : node

function parsedExpression(value) {
  if (!value) return null
  const file = ts.createSourceFile('rule-center-expression.ts', `(${value})`, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  if (file.parseDiagnostics.length || file.statements.length !== 1 || !ts.isExpressionStatement(file.statements[0])) return null
  return { node: unwrap(file.statements[0].expression), file }
}

function expressionKey(value) {
  const parsed = parsedExpression(value)
  return parsed && ts.createPrinter({ removeComments: true }).printNode(ts.EmitHint.Expression, parsed.node, parsed.file)
}

const expressionIs = (value, expected) => expressionKey(value) === expressionKey(expected)

function parseComponent(source) {
  const sfc = parseSfc(source)
  if (sfc.errors.length || !sfc.descriptor.template || !sfc.descriptor.scriptSetup) return null
  let template
  try { template = parse(sfc.descriptor.template.content, { comments: false }) } catch { return null }
  const script = ts.createSourceFile('rule-center.ts', sfc.descriptor.scriptSetup.content,
    ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  if (script.parseDiagnostics.length) return null
  const nodes = [], parents = new Map()
  function walk(node, ancestors = []) {
    if (node.type === 1 && !['script', 'style'].includes(node.tag.toLowerCase())) {
      nodes.push(node)
      parents.set(node, ancestors)
    }
    for (const child of node.children || []) walk(child, [...ancestors, node])
  }
  walk(template)
  return { nodes, parents, script }
}

function unique(nodes, predicate) {
  const matches = nodes.filter(predicate)
  return matches.length === 1 ? matches[0] : null
}

function renderedChildren(node) {
  return (node.children || []).flatMap(child => transparentTemplate(child)
    ? renderedChildren(child)
    : child.type === 1 && !['script', 'style'].includes(child.tag.toLowerCase()) ? [child] : [])
}

function directives(node, name, argument) {
  return (node.props || []).filter(prop => prop.type === 7 && prop.name === name
    && (argument === undefined ? !prop.arg : prop.arg?.isStatic && prop.arg.content === argument))
}

function exactDirective(node, name, argument, expected) {
  const matches = directives(node, name, argument)
  return matches.length === 1 && (matches[0].modifiers || []).length === 0
    && expressionIs(matches[0].exp?.content, expected)
}

function exactVisibility(node, name, expected) {
  const visible = (node.props || []).filter(prop => prop.type === 7 && visibilityNames.has(prop.name))
  return visible.length === 1 && visible[0].name === name && (visible[0].modifiers || []).length === 0
    && (expected === undefined ? !visible[0].exp : expressionIs(visible[0].exp?.content, expected))
}

const noVisibility = node => !(node.props || []).some(prop => prop.type === 7 && visibilityNames.has(prop.name))

function hasRenderingOverride(node) {
  return (node.props || []).some(prop => {
    if (prop.type === 6 && ['hidden', 'inert', 'style'].includes(prop.name)) return true
    if (prop.type === 6 && prop.name === 'class') return classes(node).some(name => ['hidden', 'invisible', 'is-hidden'].includes(name))
    if (prop.type === 7 && ['html', 'text'].includes(prop.name)) return true
    return prop.type === 7 && prop.name === 'bind'
      && (!prop.arg || !prop.arg.isStatic || ['class', 'hidden', 'inert', 'style'].includes(prop.arg.content))
  })
}

function staticAttribute(node, name, expected) {
  if ((node.props || []).some(prop => prop.type === 7 && prop.name === 'bind'
    && (!prop.arg || !prop.arg.isStatic || prop.arg.content === name))) return false
  const matches = (node.props || []).filter(prop => prop.type === 6 && prop.name === name)
  return matches.length === 1 && matches[0].value?.content === expected
}

function templateRootReachable(component, node) {
  return (component.parents.get(node) || []).every(ancestor => ancestor.type !== 1
    || transparentTemplate(ancestor))
}

function pathAllows(component, node, boundary, allowedVisibility = new Set()) {
  const ancestors = component.parents.get(node) || []
  const index = ancestors.indexOf(boundary)
  if (index < 0) return false
  return ancestors.slice(index + 1).every(ancestor => ancestor.type !== 1
    || (!hasRenderingOverride(ancestor) && (noVisibility(ancestor) || allowedVisibility.has(ancestor))))
}

function exactFor(node, local, collection) {
  const visible = (node.props || []).filter(prop => prop.type === 7 && visibilityNames.has(prop.name))
  if (visible.length !== 1 || visible[0].name !== 'for' || (visible[0].modifiers || []).length) return false
  const match = (visible[0].exp?.content || '').match(/^\s*\(?\s*([A-Za-z_$][\w$]*)\s*\)?\s+(?:in|of)\s+([\s\S]+?)\s*$/)
  return Boolean(match && match[1] === local && expressionIs(match[2], collection))
}

function exactSurface(node, allowedBindings = [], allowedEvents = [], visibility = null) {
  if (hasRenderingOverride(node)) return false
  if ((node.props || []).some(prop => prop.type === 6 && prop.name === 'disabled')) return false
  const visible = (node.props || []).filter(prop => prop.type === 7 && visibilityNames.has(prop.name))
  if (visibility) {
    const valid = visibility.name === 'for'
      ? exactFor(node, visibility.local, visibility.collection)
      : exactVisibility(node, visibility.name, visibility.expected)
    if (!valid) return false
  } else if (visible.length) return false
  return !(node.props || []).some(prop => {
    if (prop.type !== 7 || visibilityNames.has(prop.name)) return false
    if (prop.name === 'bind') return !prop.arg?.isStatic || !allowedBindings.includes(prop.arg.content)
    if (prop.name === 'on') return !prop.arg?.isStatic || !allowedEvents.includes(prop.arg.content)
    return true
  })
}

function singleInterpolation(node, expected) {
  let count = 0, valid = true
  function walk(current, descendant = false) {
    if (current.type === 2 && current.content.trim()) valid = false
    else if (current.type === 5) {
      count++
      if (!expressionIs(current.content.content, expected)) valid = false
    } else if (current.type === 1) {
      if (hasRenderingOverride(current) || (descendant && !noVisibility(current))) valid = false
      for (const child of current.children || []) walk(child, true)
    }
  }
  walk(node)
  return valid && count === 1
}

function hasDefaultImport(script, localName, moduleName) {
  return script.statements.filter(statement => ts.isImportDeclaration(statement)
    && ts.isStringLiteral(statement.moduleSpecifier) && statement.moduleSpecifier.text === moduleName
    && statement.importClause?.name?.text === localName).length === 1
}

function toggleLoadsLinkedCatalog(script) {
  const functions = script.statements.filter(statement => ts.isFunctionDeclaration(statement)
    && statement.name?.text === 'toggleEntry' && statement.parameters.length === 1
    && ts.isIdentifier(statement.parameters[0].name) && statement.parameters[0].name.text === 'item')
  if (functions.length !== 1 || !functions[0].body) return false
  let guardedCalls = 0
  for (const statement of functions[0].body.statements) {
    if (!ts.isIfStatement(statement) || !expressionIs(statement.expression.getText(script),
      'next.has(item.id) && item.cardIds.length')) continue
    let calls = 0
    const visit = node => {
      if (ts.isCallExpression(node) && ts.isIdentifier(unwrap(node.expression))
        && unwrap(node.expression).text === 'ensureCardCatalog' && node.arguments.length === 0) calls++
      ts.forEachChild(node, visit)
    }
    visit(statement.thenStatement)
    if (calls === 1 && !statement.elseStatement) guardedCalls++
  }
  return guardedCalls === 1
}

function propertyPath(node) {
  node = unwrap(node)
  if (ts.isIdentifier(node)) return node.text
  if (ts.isPropertyAccessExpression(node)) {
    const base = propertyPath(node.expression)
    return base && `${base}.${node.name.text}`
  }
  if (ts.isElementAccessExpression(node)) {
    const base = propertyPath(node.expression)
    const argument = unwrap(node.argumentExpression)
    if (base && argument && (ts.isStringLiteral(argument) || ts.isNoSubstitutionTemplateLiteral(argument))) return `${base}.${argument.text}`
  }
  return null
}

const forbiddenCenterPaths = new Set(['item.row.id', 'item.row.page', 'item.row.topic', 'item.collection'])

function expressionContainsForbidden(value) {
  const parsed = parsedExpression(value)
  if (!parsed) return true
  let found = false
  const visit = node => {
    if (forbiddenCenterPaths.has(propertyPath(node))) found = true
    if (ts.isElementAccessExpression(node) && propertyPath(node.expression) === 'item.row') {
      const argument = unwrap(node.argumentExpression)
      if (!argument || (!ts.isStringLiteral(argument) && !ts.isNoSubstitutionTemplateLiteral(argument))) found = true
    }
    if (!found) ts.forEachChild(node, visit)
  }
  visit(parsed.node)
  return found
}

function forbiddenCenterControl(control) {
  return (control.props || []).some(prop => {
    if (prop.type !== 7) return false
    if (prop.name === 'model') return expressionContainsForbidden(prop.exp?.content)
    if (prop.name === 'bind') {
      if (!prop.arg?.isStatic) return true
      return ['value', 'model-value', 'modelValue'].includes(prop.arg.content)
        && expressionContainsForbidden(prop.exp?.content)
    }
    if (prop.name === 'on' && (!prop.arg?.isStatic || ['input', 'change', 'update:modelValue'].includes(prop.arg.content))) {
      return expressionContainsForbidden(prop.exp?.content)
    }
    return false
  })
}

function eventButtons(component, handler) {
  return component.nodes.filter(node => node.tag === 'button' && directives(node, 'on', 'click').some(prop => {
    const parsed = parsedExpression(prop.exp?.content)
    return parsed && ts.isCallExpression(parsed.node) && ts.isIdentifier(unwrap(parsed.node.expression))
      && unwrap(parsed.node.expression).text === handler
  }))
}

const empty = {
  publicCoreDirectory: false,
  publicExpandableRulings: false,
  publicLinkedCardDisclosure: false,
  adminCenterImmutableFields: false,
  adminCenterPublishOwnership: false,
  adminRulingPublishOwnership: false,
}

export const ruleCenterPredicateNames = Object.freeze(Object.keys(empty))

export const ruleCenterLegacyPredicateMap = Object.freeze([
  Object.freeze({ group: 'public reachable rule presentation', removed: 2,
    old: Object.freeze(['line 51: v-if="openIds.has(item.id)"',
      'line 56: aria-label="规则手册章节目录"']) }),
  Object.freeze({ group: 'admin immutable material schema', removed: 4,
    old: Object.freeze(['line 72: editable item.row.id adjacency', 'line 73: 页码（可留空）',
      'line 73: editable item.row.topic adjacency', 'line 74: item.collection value adjacency']) }),
  Object.freeze({ group: 'per-object publication ownership', removed: 1,
    old: Object.freeze(['line 77: class="publish-queue" absence']) }),
])

export function ruleCenterTemplateContract(sources) {
  const player = parseComponent(sources.player)
  const admin = parseComponent(sources.admin)
  if (!player || !admin) return { ...empty }

  const playerRoot = unique(player.nodes, node => node.tag === 'div' && hasClass(node, 'rules-page')
    && noVisibility(node) && !hasRenderingOverride(node) && templateRootReachable(player, node))
  const branch = expected => playerRoot && unique(player.nodes, node => node.tag === 'template'
    && exactVisibility(node, 'else-if', expected) && pathAllows(player, node, playerRoot))

  const coreBranch = branch("tab === 'core'")
  const coreDirectory = coreBranch && unique(player.nodes, node => node.tag === 'nav'
    && staticAttribute(node, 'aria-label', '规则手册章节目录')
    && exactVisibility(node, 'if', 'coreTableOfContents.length') && pathAllows(player, node, coreBranch))
  const coreButton = coreDirectory && unique(player.nodes, node => node.tag === 'button'
    && exactFor(node, 'chapter', 'coreTableOfContents') && pathAllows(player, node, coreDirectory)
    && exactDirective(node, 'bind', 'key', 'chapter.chapter')
    && exactDirective(node, 'on', 'click', 'scrollToCoreChapter(chapter.firstId)')
    && exactSurface(node, ['key'], ['click'], { name: 'for', local: 'chapter', collection: 'coreTableOfContents' }))

  function disclosure(expectedBranch, collection, supplemental) {
    const ownerBranch = branch(expectedBranch)
    if (!ownerBranch) return null
    const container = supplemental
      ? unique(player.nodes, node => node.tag === 'section' && hasClass(node, 'supplemental-qa')
        && exactVisibility(node, 'if', `${collection}.length`) && pathAllows(player, node, ownerBranch))
      : unique(player.nodes, node => node.tag === 'div' && hasClass(node, 'faq-list')
        && noVisibility(node) && !hasRenderingOverride(node) && pathAllows(player, node, ownerBranch))
    const article = container && unique(player.nodes, node => node.tag === 'article'
      && exactFor(node, 'item', collection) && pathAllows(player, node, container)
      && exactDirective(node, 'bind', 'id', '`rule-entry-${item.id}`')
      && exactDirective(node, 'bind', 'key', 'item.id')
      && exactDirective(node, 'bind', 'class', '{ open: openIds.has(item.id) }'))
    const children = article ? renderedChildren(article) : []
    const question = unique(children, node => node.tag === 'button' && hasClass(node, 'faq-question')
      && exactSurface(node, ['aria-expanded'], ['click'])
      && exactDirective(node, 'bind', 'aria-expanded', 'openIds.has(item.id)')
      && exactDirective(node, 'on', 'click', 'toggleEntry(item)'))
    const answer = unique(children, node => node.tag === 'div' && hasClass(node, 'faq-answer')
      && exactSurface(node, [], [], { name: 'if', expected: 'openIds.has(item.id)' }))
    return { article, question, answer }
  }

  const faq = disclosure("tab === 'faq'", 'faqResults', false)
  const construction = disclosure("tab === 'construction'", 'constructionRulings', true)
  const tournament = disclosure("tab === 'tournament'", 'tournamentRulings', true)

  const rulingCards = faq?.answer && unique(player.nodes, node => node.tag === 'div' && hasClass(node, 'ruling-cards')
    && exactVisibility(node, 'if', 'item.cardIds.length') && pathAllows(player, node, faq.answer))
  const linkedLoop = rulingCards && unique(renderedChildren(rulingCards), node => node.tag === 'template'
    && exactFor(node, 'linked', 'linkedCards(item)') && exactDirective(node, 'bind', 'key', 'linked.id'))
  const linkedButton = linkedLoop && unique(renderedChildren(linkedLoop), node => node.tag === 'button'
    && exactSurface(node, ['aria-label'], ['click'], { name: 'if', expected: 'linked.card' })
    && staticAttribute(node, 'type', 'button')
    && exactDirective(node, 'bind', 'aria-label', '`查看${linked.card.nameZh}卡牌详情`')
    && exactDirective(node, 'on', 'click', 'detailCard = linked.card'))
  const linkedImage = linkedButton && unique(renderedChildren(linkedButton), node => componentTag(node, 'CardImage', 'card-image')
    && exactSurface(node, ['card-id', 'legacy-url', 'alt'], [])
    && exactDirective(node, 'bind', 'card-id', 'linked.card.id')
    && exactDirective(node, 'bind', 'legacy-url', 'linked.card.imageUrl')
    && exactDirective(node, 'bind', 'alt', 'linked.card.nameZh')
    && staticAttribute(node, 'intent', 'thumb'))

  const adminRoot = unique(admin.nodes, node => node.tag === 'section' && hasClass(node, 'ruling-admin')
    && noVisibility(node) && !hasRenderingOverride(node) && templateRootReachable(admin, node))
  const workspacePanel = adminRoot && unique(admin.nodes, node => node.tag === 'section' && hasClass(node, 'workspace-panel')
    && exactVisibility(node, 'if', "workspace === 'drafts' || workspace === 'published'")
    && pathAllows(admin, node, adminRoot))
  const centerGroup = workspacePanel && unique(admin.nodes, node => node.tag === 'section' && hasClass(node, 'item-group')
    && exactFor(node, 'group', 'activeCenterGroups') && pathAllows(admin, node, workspacePanel))
  const centerEditor = centerGroup && unique(admin.nodes, node => node.tag === 'details' && hasClass(node, 'center-editor')
    && exactFor(node, 'item', 'group.items') && pathAllows(admin, node, centerGroup)
    && exactDirective(node, 'bind', 'id', '`admin-rule-item-${item.id}`')
    && exactDirective(node, 'bind', 'key', '`${item.collection}-${item.id}`'))
  const centerDraft = centerEditor && unique(renderedChildren(centerEditor), node => node.tag === 'template'
    && exactVisibility(node, 'else', undefined) && !hasRenderingOverride(node))
  const centerControls = centerDraft ? admin.nodes.filter(node => ['input', 'select', 'textarea'].includes(node.tag)
    && (admin.parents.get(node) || []).includes(centerDraft)) : []

  const centerPublishButtons = eventButtons(admin, 'publishCenterItem')
  const centerPublish = centerPublishButtons.length === 1 ? centerPublishButtons[0] : null
  const centerActions = centerPublish && (admin.parents.get(centerPublish) || []).find(node => node.type === 1
    && node.tag === 'div' && hasClass(node, 'item-actions'))
  const centerPublishValid = Boolean(centerPublish && centerDraft && centerActions
    && pathAllows(admin, centerPublish, centerEditor, new Set([centerDraft])))
    && hasClass(centerPublish, 'publish')
    && exactSurface(centerPublish, ['disabled'], ['click'], { name: 'if', expected: "hasPermission('admin.content.publish')" })
    && exactDirective(centerPublish, 'bind', 'disabled', 'publishingCenterIds.has(item.id)')
    && exactDirective(centerPublish, 'on', 'click', 'publishCenterItem(item.collection, item.id)')
    && singleInterpolation(centerPublish, "publishingCenterIds.has(item.id) ? '发布中…' : '审核并发布此项'")

  const rulingGroup = workspacePanel && unique(admin.nodes, node => node.tag === 'section' && hasClass(node, 'item-group')
    && exactVisibility(node, 'if', 'activeRulingItems.length') && pathAllows(admin, node, workspacePanel))
  const rulingEditor = rulingGroup && unique(admin.nodes, node => node.tag === 'details' && hasClass(node, 'ruling-editor')
    && exactFor(node, 'row', 'activeRulingItems') && pathAllows(admin, node, rulingGroup)
    && exactDirective(node, 'bind', 'id', '`admin-rule-item-${row.item.id}`')
    && exactDirective(node, 'bind', 'key', 'row.item.id'))
  const rulingOpen = rulingEditor && unique(renderedChildren(rulingEditor), node => node.tag === 'template'
    && exactVisibility(node, 'if', 'openRulingEditors.has(row.item.id)') && !hasRenderingOverride(node))
  const rulingDraft = rulingOpen && unique(renderedChildren(rulingOpen), node => node.tag === 'template'
    && exactVisibility(node, 'else', undefined) && !hasRenderingOverride(node))
  const rulingPublishButtons = eventButtons(admin, 'publishRuling')
  const rulingPublish = rulingPublishButtons.length === 1 ? rulingPublishButtons[0] : null
  const rulingActions = rulingPublish && (admin.parents.get(rulingPublish) || []).find(node => node.type === 1
    && node.tag === 'div' && hasClass(node, 'item-actions'))
  const rulingPublishValid = Boolean(rulingPublish && rulingDraft && rulingActions
    && pathAllows(admin, rulingPublish, rulingEditor, new Set([rulingOpen, rulingDraft])))
    && hasClass(rulingPublish, 'publish')
    && exactSurface(rulingPublish, ['disabled'], ['click'], { name: 'if', expected: "hasPermission('admin.content.publish')" })
    && exactDirective(rulingPublish, 'bind', 'disabled', 'publishingRulingIds.has(row.item.id)')
    && exactDirective(rulingPublish, 'on', 'click', 'publishRuling(row.item)')
    && singleInterpolation(rulingPublish, "publishingRulingIds.has(row.item.id) ? '发布中…' : '审核并发布此项'")

  return {
    publicCoreDirectory: Boolean(playerRoot && coreBranch && coreDirectory && coreButton),
    publicExpandableRulings: [faq, construction, tournament].every(item => item?.article && item.question && item.answer),
    publicLinkedCardDisclosure: Boolean(rulingCards && linkedLoop && linkedButton && linkedImage
      && hasDefaultImport(player.script, 'CardImage', '@/l12/CardImage.vue') && toggleLoadsLinkedCatalog(player.script)),
    adminCenterImmutableFields: Boolean(adminRoot && centerEditor && centerDraft && centerControls.length
      && !centerControls.some(forbiddenCenterControl)),
    adminCenterPublishOwnership: Boolean(centerEditor && centerDraft && centerActions && centerPublishValid),
    adminRulingPublishOwnership: Boolean(rulingEditor && rulingOpen && rulingDraft && rulingActions && rulingPublishValid),
  }
}

export const ruleCenterGroups = contract => [
  ['public reachable rule presentation: 2 legacy string predicates', [contract.publicCoreDirectory,
    contract.publicExpandableRulings, contract.publicLinkedCardDisclosure]],
  ['admin immutable material schema: 4 legacy string predicates', [contract.adminCenterImmutableFields]],
  ['per-object publication ownership: 1 legacy string predicate', [contract.adminCenterPublishOwnership,
    contract.adminRulingPublishOwnership]],
]
