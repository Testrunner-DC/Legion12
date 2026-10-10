import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse } from '@vue/compiler-dom'
import ts from 'typescript'

const visibilityNames = new Set(['if', 'else-if', 'else', 'show', 'for'])
const ignoredTag = node => node?.type === 1 && ['script', 'style'].includes(node.tag.toLowerCase())
const transparentTemplate = node => node?.type === 1 && node.tag === 'template' && node.props.length === 0
const classes = node => node.props?.find(prop => prop.type === 6 && prop.name === 'class')
  ?.value?.content?.split(/\s+/).filter(Boolean) || []
const hasClass = (node, name) => classes(node).includes(name)
const unwrap = node => node && ts.isParenthesizedExpression(node) ? unwrap(node.expression) : node
const componentTag = (node, pascal, kebab) => node.tag === pascal || node.tag === kebab

function parsedExpression(value) {
  if (!value) return null
  const file = ts.createSourceFile('deck-snapshot-binding.ts', `(${value})`, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
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
  const script = ts.createSourceFile('component.ts', sfc.descriptor.scriptSetup.content,
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

function descendants(node) {
  return (node.children || []).flatMap(child => child.type === 1 && !ignoredTag(child)
    ? [child, ...descendants(child)] : [])
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
  const allowedModifiers = name === 'on' ? new Set(['stop', 'prevent']) : new Set()
  const modifiers = matches[0]?.modifiers || []
  return matches.length === 1
    && modifiers.every(modifier => allowedModifiers.has(typeof modifier === 'string' ? modifier : modifier.content))
    && expressionIs(matches[0].exp?.content, expected)
}

function exactVisibility(node, name, expected) {
  const visible = (node.props || []).filter(prop => prop.type === 7 && visibilityNames.has(prop.name))
  return visible.length === 1 && visible[0].name === name
    && (visible[0].modifiers || []).length === 0
    && (expected === undefined ? !visible[0].exp : expressionIs(visible[0].exp?.content, expected))
}

function noVisibility(node) {
  return !(node.props || []).some(prop => prop.type === 7 && visibilityNames.has(prop.name))
}

function hasVisualHidingOverride(node) {
  return (node.props || []).some(prop => {
    if (prop.type === 6 && prop.name === 'hidden') return true
    if (prop.type === 6 && prop.name === 'style') return true
    if (prop.type === 6 && prop.name === 'class') return classes(node).some(name => ['hidden', 'invisible', 'is-hidden'].includes(name))
    if (prop.type === 7 && ['html', 'text'].includes(prop.name)) return true
    return prop.type === 7 && prop.name === 'bind'
      && (!prop.arg || !prop.arg.isStatic || ['hidden', 'style'].includes(prop.arg.content))
  })
}

function visibleAncestorPath(component, node, boundary, allowedVisibility = []) {
  const ancestors = component.parents.get(node) || []
  const boundaryIndex = ancestors.indexOf(boundary)
  if (boundaryIndex < 0) return false
  return ancestors.slice(boundaryIndex + 1).every(ancestor => {
    if (ancestor.type !== 1) return true
    if (hasVisualHidingOverride(ancestor)) return false
    if ((ancestor.props || []).some(prop => prop.type === 7 && prop.name === 'bind'
      && prop.arg?.isStatic && prop.arg.content === 'class')) return false
    if (noVisibility(ancestor)) return true
    return allowedVisibility.some(predicate => predicate(ancestor))
  })
}

function staticAttribute(node, name, expected) {
  if ((node.props || []).some(prop => prop.type === 7 && prop.name === 'bind'
    && (!prop.arg || !prop.arg.isStatic || prop.arg.content === name))) return false
  const matches = (node.props || []).filter(prop => prop.type === 6 && prop.name === name)
  return matches.length === 1 && matches[0].value?.content === expected
}

function exactDynamicSurface(node, allowedBindings, allowedEvents) {
  return !(node.props || []).some(prop => {
    if (prop.type !== 7) return false
    if (prop.name === 'bind') return !prop.arg?.isStatic || !allowedBindings.includes(prop.arg.content)
    if (prop.name === 'on') return !prop.arg?.isStatic || !allowedEvents.includes(prop.arg.content)
    return false
  })
}

function staticText(node) {
  let valid = true
  function collect(current) {
    if (current.type === 2) return current.content
    if (current.type === 3) return ''
    if (current.type === 5 || ignoredTag(current)) { valid = false; return '' }
    if (current.type === 1 && (current.props || []).some(prop => prop.type === 7 && ['text', 'html'].includes(prop.name))) {
      valid = false
      return ''
    }
    return (current.children || []).map(collect).join('')
  }
  const text = collect(node).replace(/\s+/g, ' ').trim()
  return valid ? text : null
}

function staticTextStartsWith(node, expected) {
  let valid = true, text = ''
  function collect(current) {
    if (current.type === 2) text += current.content
    else if (current.type === 1) {
      if ((current.props || []).some(prop => prop.type === 7 && ['text', 'html'].includes(prop.name))) valid = false
      for (const child of current.children || []) collect(child)
    }
  }
  collect(node)
  return valid && text.replace(/\s+/g, ' ').trim().startsWith(expected)
}

function hasInterpolation(node, expected, prefix = '') {
  let found = 0, valid = true, text = ''
  function walk(current) {
    if (current.type === 2) text += current.content
    else if (current.type === 5) {
      found++
      if (!expressionIs(current.content.content, expected)) valid = false
    } else if (current.type === 1) {
      if ((current.props || []).some(prop => prop.type === 7 && ['text', 'html'].includes(prop.name))) valid = false
      for (const child of current.children || []) walk(child)
    }
  }
  walk(node)
  return valid && found === 1 && text.replace(/\s+/g, ' ').trim() === prefix
}

function hasDefaultImport(script, localName, moduleName) {
  return script.statements.filter(statement => ts.isImportDeclaration(statement)
    && ts.isStringLiteral(statement.moduleSpecifier) && statement.moduleSpecifier.text === moduleName
    && statement.importClause?.name?.text === localName).length === 1
}

function variableInitializer(script, name) {
  for (const statement of script.statements) {
    if (!ts.isVariableStatement(statement)) continue
    for (const declaration of statement.declarationList.declarations) {
      if (ts.isIdentifier(declaration.name) && declaration.name.text === name) return declaration.initializer
    }
  }
  return null
}

function declaresAsyncCallable(script, name) {
  const functions = script.statements.filter(statement => ts.isFunctionDeclaration(statement)
    && statement.name?.text === name && statement.modifiers?.some(modifier => modifier.kind === ts.SyntaxKind.AsyncKeyword))
  if (functions.length === 1) return true
  const initializer = unwrap(variableInitializer(script, name))
  return Boolean(initializer && (ts.isArrowFunction(initializer) || ts.isFunctionExpression(initializer))
    && initializer.modifiers?.some(modifier => modifier.kind === ts.SyntaxKind.AsyncKeyword))
}

function specialExpansion(script) {
  const initializer = unwrap(variableInitializer(script, 'deck'))
  if (!initializer || !ts.isCallExpression(initializer) || !ts.isIdentifier(initializer.expression)
    || initializer.expression.text !== 'computed' || initializer.arguments.length !== 1) return false
  const callback = unwrap(initializer.arguments[0])
  if (!ts.isArrowFunction(callback) && !ts.isFunctionExpression(callback)) return false
  const returns = []
  function visit(node) {
    if (ts.isReturnStatement(node) && node.expression && ts.isObjectLiteralExpression(unwrap(node.expression))) returns.push(unwrap(node.expression))
    ts.forEachChild(node, visit)
  }
  visit(callback.body)
  if (returns.length !== 1) return false
  const properties = returns[0].properties.filter(property => ts.isPropertyAssignment(property)
    && (ts.isIdentifier(property.name) || ts.isStringLiteral(property.name)) && property.name.text === 'specialIds')
  if (properties.length !== 1) return false
  const call = unwrap(properties[0].initializer)
  return ts.isCallExpression(call) && ts.isIdentifier(call.expression) && call.expression.text === 'expand'
    && call.arguments.length === 1 && ts.isStringLiteral(call.arguments[0]) && call.arguments[0].text === 'special'
}

function selectedComputed(script) {
  const initializer = variableInitializer(script, 'selected')
  if (!initializer) return false
  const printer = ts.createPrinter({ removeComments: true })
  const actual = printer.printNode(ts.EmitHint.Expression, initializer, script)
  return expressionIs(actual, 'computed(() => byId.value.get(selectedId.value))')
}

function containsParticipantDeckCards(node) {
  let found = false
  function visit(current) {
    current = unwrap(current)
    if (ts.isPropertyAccessExpression(current) && ts.isIdentifier(unwrap(current.expression))
      && unwrap(current.expression).text === 'participant' && current.name.text === 'deckCards') found = true
    if (ts.isElementAccessExpression(current) && ts.isIdentifier(unwrap(current.expression))
      && unwrap(current.expression).text === 'participant') {
      const argument = unwrap(current.argumentExpression)
      if ((ts.isStringLiteral(argument) || ts.isNoSubstitutionTemplateLiteral(argument))
        && argument.text === 'deckCards') found = true
    }
    if (!found) ts.forEachChild(current, visit)
  }
  visit(node)
  return found
}

function loopsParticipantDeckCards(node) {
  const loop = directives(node, 'for', undefined)
  if (loop.length !== 1) return false
  const source = loop[0].exp?.content?.match(/^\s*(?:\([^)]*\)|[^\s]+)\s+(?:in|of)\s+([\s\S]+?)\s*$/)?.[1]
  const parsed = parsedExpression(source)
  return Boolean(parsed && containsParticipantDeckCards(parsed.node))
}

function exactComponentNode(node, bindings, events) {
  if (!exactDynamicSurface(node, Object.keys(bindings), Object.keys(events)) || hasVisualHidingOverride(node)
    || (node.props || []).some(prop => prop.type === 6 && Object.hasOwn(bindings, prop.name))) return false
  return Object.entries(bindings).every(([name, value]) => exactDirective(node, 'bind', name, value))
    && Object.entries(events).every(([name, value]) => exactDirective(node, 'on', name, value))
}

const empty = {
  archiveLauncher: false,
  noInlineArchiveCards: false,
  archiveViewerConsumer: false,
  analyticsViewerConsumer: false,
  viewerBrowserComposition: false,
  viewerCopyCodeAction: false,
  viewerExportImageAction: false,
  viewerCopyToLibraryAction: false,
  viewerSpecialExpansion: false,
  browserFilterSemantics: false,
  browserQuantityRendering: false,
  browserSelectedComputed: false,
}

export const deckSnapshotPredicateNames = Object.freeze(Object.keys(empty))

export function deckSnapshotTemplateContract(sources) {
  const matches = parseComponent(sources.adminMatches)
  const analytics = parseComponent(sources.adminMasterAnalytics)
  const viewer = parseComponent(sources.deckSnapshotViewer)
  const browser = parseComponent(sources.deckConstructionBrowser)
  if (!matches || !analytics || !viewer || !browser) return { ...empty }

  const matchHost = unique(matches.nodes, node => node.tag === 'section' && hasClass(node, 'match-admin')
    && noVisibility(node) && !hasVisualHidingOverride(node))
  const participant = matchHost && unique(descendants(matchHost), node => node.tag === 'article'
    && hasClass(node, 'participant-card') && exactVisibility(node, 'for', 'participant in detail.participants')
    && !hasVisualHidingOverride(node)
    && visibleAncestorPath(matches, node, matchHost, [ancestor => ancestor.tag === 'template'
      && exactVisibility(ancestor, 'else-if', 'detail')]))
  const launcher = participant && unique(renderedChildren(participant), node => node.tag === 'button'
    && hasClass(node, 'view-construction') && exactVisibility(node, 'if', 'participant.deckCards?.length')
    && !hasVisualHidingOverride(node))
  const archiveViewer = matchHost && unique(renderedChildren(matchHost), node => componentTag(node, 'DeckSnapshotViewer', 'deck-snapshot-viewer')
    && exactVisibility(node, 'if', 'deckViewer'))

  const analyticsHost = unique(analytics.nodes, node => node.tag === 'section' && hasClass(node, 'master-analytics')
    && noVisibility(node) && !hasVisualHidingOverride(node))
  const analyticsViewer = analyticsHost && unique(renderedChildren(analyticsHost), node => componentTag(node, 'DeckSnapshotViewer', 'deck-snapshot-viewer')
    && exactVisibility(node, 'if', 'popularDeck && selected'))

  const teleport = unique(renderedChildren(viewer.template), node => node.tag === 'Teleport' && staticAttribute(node, 'to', 'body')
    && noVisibility(node) && exactDynamicSurface(node, [], []) && !hasVisualHidingOverride(node))
  const modal = teleport && unique(descendants(teleport), node => node.tag === 'section' && hasClass(node, 'deck-viewer-modal')
    && staticAttribute(node, 'role', 'dialog') && staticAttribute(node, 'aria-modal', 'true')
    && exactDirective(node, 'bind', 'aria-label', 'title') && noVisibility(node)
    && exactDynamicSurface(node, ['aria-label'], []) && !hasVisualHidingOverride(node)
    && visibleAncestorPath(viewer, node, teleport))
  const construction = modal && unique(renderedChildren(modal), node => componentTag(node, 'DeckConstructionBrowser', 'deck-construction-browser')
    && noVisibility(node))
  const footer = modal && unique(renderedChildren(modal), node => node.tag === 'footer' && hasClass(node, 'deck-viewer-actions')
    && noVisibility(node) && !hasVisualHidingOverride(node))
  const action = (label, handler, disabled = '!deck') => footer && unique(renderedChildren(footer), node => node.tag === 'button'
    && staticText(node) === label && exactDirective(node, 'bind', 'disabled', disabled)
    && exactDirective(node, 'on', 'click', handler)
    && exactDynamicSurface(node, ['disabled'], ['click']) && noVisibility(node) && !hasVisualHidingOverride(node))

  const browserHost = unique(browser.nodes, node => node.tag === 'section' && hasClass(node, 'construction-browser')
    && staticAttribute(node, 'data-ui-contract', 'shared-deck-construction-browser') && noVisibility(node)
    && !hasVisualHidingOverride(node))
  const browserChildren = browserHost ? renderedChildren(browserHost) : []
  const filterTeleport = unique(browserChildren, node => node.tag === 'Teleport'
    && exactVisibility(node, 'if', 'filterTarget && filterTargetReady') && exactDirective(node, 'bind', 'to', 'filterTarget')
    && exactDynamicSurface(node, ['to'], []) && !hasVisualHidingOverride(node))
  const teleportedFilter = filterTeleport && unique(renderedChildren(filterTeleport), node => node.tag === 'nav'
    && hasClass(node, 'construction-filter-rail') && staticAttribute(node, 'aria-label', '构筑筛选')
    && noVisibility(node) && !hasVisualHidingOverride(node))
  const localFilter = unique(browserChildren, node => node.tag === 'nav'
    && exactVisibility(node, 'else', undefined) && staticAttribute(node, 'aria-label', '构筑筛选')
    && !hasVisualHidingOverride(node))
  const grid = unique(browserChildren, node => node.tag === 'div' && hasClass(node, 'construction-grid')
    && noVisibility(node) && !hasVisualHidingOverride(node))
  const cardButton = grid && unique(renderedChildren(grid), node => node.tag === 'button'
    && exactVisibility(node, 'for', 'entry in visible')
    && exactDirective(node, 'bind', 'key', '`${entry.section}-${entry.cardId}`')
    && exactDirective(node, 'bind', 'class', '{ selected: selectedId === entry.cardId }')
    && exactDirective(node, 'on', 'click', 'selectCard(entry.cardId)')
    && exactDynamicSurface(node, ['key', 'class'], ['click']) && !hasVisualHidingOverride(node))
  const quantity = cardButton && unique(renderedChildren(cardButton), node => node.tag === 'strong'
    && hasInterpolation(node, 'entry.quantity', '×'))
  const details = browserHost && unique(renderedChildren(browserHost), node => componentTag(node, 'CatalogCardDetails', 'catalog-card-details')
    && exactVisibility(node, 'if', 'selected && !externalDetails')
    && exactComponentNode(node, { card: 'selected', 'show-catalog-only': 'false' }, { close: "selectedId = ''" }))

  const copyCode = action('复制牌库码', 'copyCode')
  const exportImage = action('导出牌库图', 'exportImage')
  const copyToLibrary = action('复制到我的牌库', 'copyToLibrary', '!deck || copyBusy')
  return {
    archiveLauncher: Boolean(matchHost && participant && launcher
      && staticAttribute(launcher, 'data-ui-contract', 'match-snapshot-view-construction')
      && exactDirective(launcher, 'on', 'click', 'deckViewer = participant')
      && exactDynamicSurface(launcher, [], ['click']) && staticTextStartsWith(launcher, '查看构筑')),
    noInlineArchiveCards: Boolean(participant && !matches.nodes.some(loopsParticipantDeckCards)),
    archiveViewerConsumer: Boolean(archiveViewer
      && hasDefaultImport(matches.script, 'DeckSnapshotViewer', './DeckSnapshotViewer.vue')
      && exactComponentNode(archiveViewer, {
        entries: 'deckViewer.deckCards', catalog: 'cards', 'master-id': "deckViewer.masterId || ''",
        title: "`${deckViewer.displayName} · ${deckViewer.deckName || '未命名构筑'}`",
        'deck-name': "deckViewer.deckName || `${deckViewer.displayName}的对局构筑`",
      }, { close: 'deckViewer = null', notice: "emit('notice', $event)" })),
    analyticsViewerConsumer: Boolean(analyticsViewer
      && hasDefaultImport(analytics.script, 'DeckSnapshotViewer', './DeckSnapshotViewer.vue')
      && staticAttribute(analyticsViewer, 'eyebrow', '统计构筑快照')
      && exactComponentNode(analyticsViewer, {
        entries: 'popularDeck.cards', catalog: 'cards', 'master-id': 'selected.masterId',
        title: '`${masterName(selected.masterId)} · 热门构筑`',
        'deck-name': '`${masterName(selected.masterId)} 热门构筑`',
      }, { close: 'popularDeck = null', notice: "emit('notice', $event)" })),
    viewerBrowserComposition: Boolean(construction
      && hasDefaultImport(viewer.script, 'DeckConstructionBrowser', './DeckConstructionBrowser.vue')
      && exactComponentNode(construction, { entries: 'entries', catalog: 'catalog', title: 'title' }, {})),
    viewerCopyCodeAction: Boolean(copyCode && declaresAsyncCallable(viewer.script, 'copyCode')),
    viewerExportImageAction: Boolean(exportImage && declaresAsyncCallable(viewer.script, 'exportImage')),
    viewerCopyToLibraryAction: Boolean(copyToLibrary && declaresAsyncCallable(viewer.script, 'copyToLibrary')),
    viewerSpecialExpansion: specialExpansion(viewer.script),
    browserFilterSemantics: Boolean(teleportedFilter && localFilter),
    browserQuantityRendering: Boolean(cardButton && quantity),
    browserSelectedComputed: Boolean(details
      && hasDefaultImport(browser.script, 'CatalogCardDetails', '@/l12/CatalogCardDetails.vue')
      && selectedComputed(browser.script)),
  }
}

export const deckSnapshotGroups = contract => [
  ['archive launcher replaces inline cards', [contract.archiveLauncher, contract.noInlineArchiveCards]],
  ['archive and analytics mount the shared viewer', [contract.archiveViewerConsumer, contract.analyticsViewerConsumer]],
  ['viewer composes browser and guarded actions', [contract.viewerBrowserComposition, contract.viewerCopyCodeAction,
    contract.viewerExportImageAction, contract.viewerCopyToLibraryAction, contract.viewerSpecialExpansion]],
  ['browser keeps filters quantities and selected details', [contract.browserFilterSemantics,
    contract.browserQuantityRendering, contract.browserSelectedComputed]],
]
