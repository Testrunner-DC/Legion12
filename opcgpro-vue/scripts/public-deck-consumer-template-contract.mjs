import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse } from '@vue/compiler-dom'
import ts from 'typescript'

const visibilityNames = new Set(['if', 'else-if', 'else', 'show', 'for'])
const ignoredTag = node => node?.type === 1 && ['script', 'style'].includes(node.tag.toLowerCase())
const transparentTemplate = node => node?.type === 1 && node.tag === 'template' && node.props.length === 0
const componentTag = (node, pascal, kebab) => node.tag === pascal || node.tag === kebab
const classes = node => node.props?.find(prop => prop.type === 6 && prop.name === 'class')
  ?.value?.content?.split(/\s+/).filter(Boolean) || []
const hasClass = (node, name) => classes(node).includes(name)
const unwrap = node => node && ts.isParenthesizedExpression(node) ? unwrap(node.expression) : node

function parsedExpression(value) {
  if (!value) return null
  const file = ts.createSourceFile('public-deck-consumer-binding.ts', `(${value})`,
    ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
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
  const script = ts.createSourceFile('public-deck-consumer.ts', sfc.descriptor.scriptSetup.content,
    ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  if (script.parseDiagnostics.length) return null
  const nodes = [], parents = new Map()
  function walk(node, ancestors = []) {
    if (node.type === 1 && !ignoredTag(node) && !ancestors.some(ignoredTag)) {
      nodes.push(node)
      parents.set(node, ancestors)
    }
    for (const child of node.children || []) walk(child, [...ancestors, node])
  }
  walk(template)
  return { template, script, nodes, parents }
}

function renderedChildren(node) {
  return (node.children || []).flatMap(child => transparentTemplate(child)
    ? renderedChildren(child)
    : child.type === 1 && !ignoredTag(child) ? [child] : [])
}

function unique(nodes, predicate) {
  const matches = nodes.filter(predicate)
  return matches.length === 1 ? matches[0] : null
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

function noVisibility(node) {
  return !(node.props || []).some(prop => prop.type === 7 && visibilityNames.has(prop.name))
}

function hasRenderingOverride(node) {
  return (node.props || []).some(prop => {
    if (prop.type === 6 && ['hidden', 'style'].includes(prop.name)) return true
    if (prop.type === 6 && prop.name === 'class') return classes(node).some(name => ['hidden', 'invisible', 'is-hidden'].includes(name))
    if (prop.type === 7 && ['html', 'text'].includes(prop.name)) return true
    return prop.type === 7 && prop.name === 'bind'
      && (!prop.arg || !prop.arg.isStatic || ['hidden', 'style'].includes(prop.arg.content))
  })
}

function visiblePath(component, node, boundary) {
  const ancestors = component.parents.get(node) || []
  const index = ancestors.indexOf(boundary)
  if (index < 0) return false
  return ancestors.slice(index + 1).every(ancestor => ancestor.type !== 1
    || (noVisibility(ancestor) && !hasRenderingOverride(ancestor)
      && !(ancestor.props || []).some(prop => prop.type === 7 && prop.name === 'bind'
        && prop.arg?.isStatic && prop.arg.content === 'class')))
}

function staticAttribute(node, name, expected) {
  if ((node.props || []).some(prop => prop.type === 7 && prop.name === 'bind'
    && (!prop.arg || !prop.arg.isStatic || prop.arg.content === name))) return false
  const matches = (node.props || []).filter(prop => prop.type === 6 && prop.name === name)
  return matches.length === 1 && matches[0].value?.content === expected
}

function booleanAttribute(node, name) {
  if ((node.props || []).some(prop => prop.type === 7 && prop.name === 'bind'
    && (!prop.arg || !prop.arg.isStatic || prop.arg.content === name))) return false
  const matches = (node.props || []).filter(prop => prop.type === 6 && prop.name === name)
  return matches.length === 1 && !matches[0].value
}

function exactDynamicSurface(node, allowedBindings, allowedEvents) {
  return !(node.props || []).some(prop => {
    if (prop.type !== 7) return false
    if (prop.name === 'bind') return !prop.arg?.isStatic || !allowedBindings.includes(prop.arg.content)
    if (prop.name === 'on') return !prop.arg?.isStatic || !allowedEvents.includes(prop.arg.content)
    return !visibilityNames.has(prop.name)
  })
}

function exactComponentNode(node, bindings, staticAttributes, booleanAttributes, events, visibility = null) {
  if (hasRenderingOverride(node)
    || !exactDynamicSurface(node, Object.keys(bindings), Object.keys(events))) return false
  if (visibility ? !exactVisibility(node, visibility.name, visibility.expected) : !noVisibility(node)) return false
  if ((node.props || []).some(prop => prop.type === 6 && Object.hasOwn(bindings, prop.name))) return false
  return Object.entries(bindings).every(([name, value]) => exactDirective(node, 'bind', name, value))
    && Object.entries(staticAttributes).every(([name, value]) => staticAttribute(node, name, value))
    && booleanAttributes.every(name => booleanAttribute(node, name))
    && Object.entries(events).every(([name, value]) => exactDirective(node, 'on', name, value))
}

function staticText(node) {
  let valid = true
  function collect(current) {
    if (current.type === 2) return current.content
    if (current.type === 3) return ''
    if (current.type === 5 || ignoredTag(current)) { valid = false; return '' }
    if (current.type === 1 && hasRenderingOverride(current)) { valid = false; return '' }
    return (current.children || []).map(collect).join('')
  }
  const value = collect(node).replace(/\s+/g, ' ').trim()
  return valid ? value : null
}

function hasInterpolation(node, expected) {
  let found = 0, valid = true
  function walk(current) {
    if (current.type === 2 || current.type === 3) return
    if (current.type === 5) {
      found++
      if (!expressionIs(current.content.content, expected)) valid = false
      return
    }
    if (current.type === 1) {
      if (hasRenderingOverride(current)) valid = false
      for (const child of current.children || []) walk(child)
    }
  }
  walk(node)
  return valid && found === 1
}

function summaryRow(section, condition, label, value) {
  return unique(renderedChildren(section), node => {
    if (node.tag !== 'p' || !exactVisibility(node, 'if', condition) || hasRenderingOverride(node)) return false
    const strong = unique(renderedChildren(node), child => child.tag === 'strong'
      && noVisibility(child) && !hasRenderingOverride(child) && hasInterpolation(child, value))
    if (!strong) return false
    let text = '', valid = true, strongCount = 0
    function walk(current) {
      if (current.type === 2) text += current.content
      else if (current.type === 3) return
      else if (current === strong) strongCount++
      else if (transparentTemplate(current)) for (const child of current.children || []) walk(child)
      else valid = false
    }
    for (const child of node.children || []) walk(child)
    return valid && strongCount === 1 && text.replace(/\s+/g, ' ').trim() === label
  })
}

function hasDefaultImport(script, localName, moduleName) {
  return script.statements.filter(statement => ts.isImportDeclaration(statement)
    && ts.isStringLiteral(statement.moduleSpecifier) && statement.moduleSpecifier.text === moduleName
    && statement.importClause?.name?.text === localName).length === 1
}

const empty = {
  detailSummaryHost: false,
  sharedBrowserConsumer: false,
  filterBridge: false,
  summaryRows: false,
  uniqueDetailOwnership: false,
}

export const publicDeckConsumerPredicateNames = Object.freeze(Object.keys(empty))

export function publicDeckConsumerTemplateContract(sources) {
  const detail = parseComponent(sources.publicDeckDetail)
  const browser = parseComponent(sources.deckConstructionBrowser)
  if (!detail || !browser) return { ...empty }

  const main = unique(renderedChildren(detail.template), node => node.tag === 'main' && hasClass(node, 'public-deck-detail')
    && staticAttribute(node, 'data-ui-contract', 'public-deck-detail-page')
    && noVisibility(node) && !hasRenderingOverride(node))
  const entryBranch = main && unique(renderedChildren(main), node => node.tag === 'template'
    && exactVisibility(node, 'else', undefined) && !hasRenderingOverride(node))
  const constructionHost = entryBranch && unique(detail.nodes, node => node.tag === 'section'
    && staticAttribute(node, 'id', 'public-deck-construction') && hasClass(node, 'deck-layout')
    && noVisibility(node) && !hasRenderingOverride(node) && visiblePath(detail, node, entryBranch))
  const summaryAside = constructionHost && unique(renderedChildren(constructionHost), node => node.tag === 'aside'
    && !hasClass(node, 'public-card-detail') && noVisibility(node) && !hasRenderingOverride(node))
  const costSection = summaryAside && unique(renderedChildren(summaryAside), node => node.tag === 'section'
    && noVisibility(node) && !hasRenderingOverride(node)
    && Boolean(unique(renderedChildren(node), child => child.tag === 'b' && staticText(child) === '费用曲线')))
  const summarySection = summaryAside && unique(renderedChildren(summaryAside), node => node.tag === 'section'
    && noVisibility(node) && !hasRenderingOverride(node)
    && Boolean(unique(renderedChildren(node), child => child.tag === 'b' && staticText(child) === '构筑摘要')))
  const filterAnchor = summaryAside && detail.nodes.filter(node => staticAttribute(node, 'id', 'public-deck-construction-filters')).length === 1
    && unique(renderedChildren(summaryAside), node => node.tag === 'section'
    && staticAttribute(node, 'id', 'public-deck-construction-filters')
    && staticAttribute(node, 'aria-label', '构筑筛选') && noVisibility(node) && !hasRenderingOverride(node)
    && (node.children || []).every(child => child.type === 3 || child.type === 2 && !child.content.trim()))

  const browserHost = constructionHost && unique(renderedChildren(constructionHost), node => node.tag === 'div'
    && hasClass(node, 'public-deck-main') && noVisibility(node) && !hasRenderingOverride(node))
  const sharedBrowser = browserHost && detail.nodes.filter(node => componentTag(node,
    'DeckConstructionBrowser', 'deck-construction-browser')).length === 1
    && unique(renderedChildren(browserHost), node => componentTag(node,
    'DeckConstructionBrowser', 'deck-construction-browser') && exactComponentNode(node, {
    entries: 'entries', catalog: 'catalog', 'master-faction': 'master?.faction',
    title: '`${entry.deck.name} · 全部构筑`',
  }, { 'filter-target': '#public-deck-construction-filters' }, ['hide-header', 'external-details'],
  { select: 'selectCard' }))

  const browserRoot = unique(renderedChildren(browser.template), node => node.tag === 'section' && hasClass(node, 'construction-browser')
    && staticAttribute(node, 'data-ui-contract', 'shared-deck-construction-browser')
    && noVisibility(node) && !hasRenderingOverride(node))
  const browserChildren = browserRoot ? renderedChildren(browserRoot) : []
  const teleport = unique(browserChildren, node => componentTag(node, 'Teleport', 'teleport')
    && exactVisibility(node, 'if', 'filterTarget && filterTargetReady')
    && exactDirective(node, 'bind', 'to', 'filterTarget')
    && exactDynamicSurface(node, ['to'], []) && !hasRenderingOverride(node))
  const teleportedFilter = teleport && unique(renderedChildren(teleport), node => node.tag === 'nav'
    && hasClass(node, 'construction-filter-rail') && staticAttribute(node, 'aria-label', '构筑筛选')
    && noVisibility(node) && !hasRenderingOverride(node))
  const localFilter = unique(browserChildren, node => node.tag === 'nav'
    && exactVisibility(node, 'else', undefined) && staticAttribute(node, 'aria-label', '构筑筛选')
    && !hasRenderingOverride(node))

  const desktopOwner = constructionHost && unique(renderedChildren(constructionHost), node => node.tag === 'aside'
    && hasClass(node, 'archive-detail') && hasClass(node, 'public-card-detail')
    && staticAttribute(node, 'aria-label', '卡牌详情') && noVisibility(node) && !hasRenderingOverride(node))
  const desktopDetails = desktopOwner && unique(renderedChildren(desktopOwner), node => componentTag(node,
    'CardDetailContent', 'card-detail-content') && exactComponentNode(node,
    { card: 'selectedCard', 'show-catalog-only': 'false' }, {}, [], {}, { name: 'if', expected: 'selectedCard' }))
  const mobileDetails = unique(detail.nodes, node => componentTag(node, 'CatalogCardDetails', 'catalog-card-details')
    && exactComponentNode(node, { card: 'selectedCard', 'show-catalog-only': 'false' }, {}, [],
      { close: 'mobileDetailsOpen = false' }, { name: 'if', expected: 'mobileDetailsOpen && selectedCard' })
    && visiblePath(detail, node, detail.template))
  const internalDetails = browserRoot && unique(browserChildren, node => componentTag(node,
    'CatalogCardDetails', 'catalog-card-details') && exactComponentNode(node,
    { card: 'selected', 'show-catalog-only': 'false' }, {}, [], { close: "selectedId = ''" },
    { name: 'if', expected: 'selected && !externalDetails' }))

  const rowDefinitions = [
    ['entry.deck.cardIds.length', '主牌', 'entry.deck.cardIds.length'],
    ['entry.deck.moraleIds.length', '士气', 'entry.deck.moraleIds.length'],
    ['entry.deck.specialIds?.length', '额外', 'entry.deck.specialIds.length'],
    ['automaticExtraCardIdsForMaster(entry.deck.masterId).length', '自动额外',
      'automaticExtraCardIdsForMaster(entry.deck.masterId).length'],
  ]
  const rows = summarySection && rowDefinitions.map(definition => summaryRow(summarySection, ...definition))
  return {
    detailSummaryHost: Boolean(main && entryBranch && constructionHost && summaryAside && costSection && summarySection),
    sharedBrowserConsumer: Boolean(sharedBrowser
      && hasDefaultImport(detail.script, 'DeckConstructionBrowser', './DeckConstructionBrowser.vue')),
    filterBridge: Boolean(filterAnchor && sharedBrowser && browserRoot && teleport && teleportedFilter && localFilter),
    summaryRows: Boolean(rows && rows.every(Boolean)),
    uniqueDetailOwnership: Boolean(sharedBrowser && desktopDetails && mobileDetails && internalDetails
      && hasDefaultImport(detail.script, 'CardDetailContent', '@/l12/CardDetailContent.vue')
      && hasDefaultImport(detail.script, 'CatalogCardDetails', '@/l12/CatalogCardDetails.vue')
      && hasDefaultImport(browser.script, 'CatalogCardDetails', '@/l12/CatalogCardDetails.vue')
      && detail.nodes.filter(node => componentTag(node, 'CardDetailContent', 'card-detail-content')).length === 1
      && detail.nodes.filter(node => componentTag(node, 'CatalogCardDetails', 'catalog-card-details')).length === 1
      && browser.nodes.filter(node => componentTag(node, 'CatalogCardDetails', 'catalog-card-details')).length === 1),
  }
}

export const publicDeckConsumerGroups = contract => [
  ['detail owns construction summary', [contract.detailSummaryHost, contract.summaryRows]],
  ['detail mounts shared browser filter bridge', [contract.sharedBrowserConsumer, contract.filterBridge]],
  ['detail ownership stays external', [contract.uniqueDetailOwnership]],
]

// The old leaves required full-list local comparers and an ownerId string.
// The directory is now server-paged and authority is the pinned canEdit field.
// Keep both original risks: all six selected sort modes reach the server query,
// and neither author action renders without its authoritative permission gate.
export function publicDeckAuthorActionsContract(source) {
  const component = parseComponent(source)
  if (!component) return false
  return ['editDeck', 'deleteDeck'].every(handler => {
    const action = unique(component.nodes, node => node.tag === 'button'
      && exactDirective(node, 'on', 'click', handler))
    return action && !hasRenderingOverride(action)
      && exactVisibility(action, 'if', 'entry.canEdit')
  })
}

export function publicDeckServerSummarySortContract(source) {
  const component = parseComponent(source)
  if (!component) return false
  const query = component.script.statements.find(node => ts.isFunctionDeclaration(node)
    && node.name?.text === 'summaryQuery')
  const loader = component.script.statements.find(node => ts.isFunctionDeclaration(node)
    && node.name?.text === 'loadSummarySources')
  if (!query?.body || !loader?.body) return false
  const request = loader.body.statements.filter(ts.isVariableStatement)
    .flatMap(node => node.declarationList.declarations).find(node => node.name.getText(component.script) === 'request')
  if (!request?.initializer || !expressionIs(request.initializer.getText(component.script), 'summaryQuery()')) return false
  let forwardsRequest = 0
  const walk = node => {
    if (ts.isCallExpression(node) && expressionIs(node.expression.getText(component.script), 'deckLibraryApi.summaries')
      && node.arguments.length === 1 && expressionIs(node.arguments[0].getText(component.script), 'request')) forwardsRequest++
    ts.forEachChild(node, walk)
  }
  walk(loader.body)
  if (forwardsRequest !== 1) return false
  const dependencies = { plazaPage: { value: 3 }, PAGE_SIZE: 30, query: { value: '  名称  ' },
    masterFilter: { value: 'M' }, factionFilter: { value: 'fate' }, legalFilter: { value: 'legal' },
    environmentFilter: { value: '2.5' }, cardFilter: { value: 'C' }, updatedSince: { value: '2026-10-01T00:00:00Z' } }
  try {
    const body = ts.transpileModule(query.getText(component.script), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
    const evaluate = new Function(...Object.keys(dependencies), 'sortMode', body + '; return summaryQuery();')
    return ['trend', 'copies', 'likes', 'views', 'latest', 'name'].every(sort => {
      const result = evaluate(...Object.values(dependencies), { value: sort })
      return result.source === 'all' && result.page === 3 && result.pageSize === 30 && result.sort === sort
        && result.keyword === '名称' && result.masterId === 'M' && result.faction === 'fate'
        && result.legal === true && result.environment === '2.5' && result.cardId === 'C'
        && result.updatedAfter === dependencies.updatedSince.value
    })
  } catch { return false }
}
