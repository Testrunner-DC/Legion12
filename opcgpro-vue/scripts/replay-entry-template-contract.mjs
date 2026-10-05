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
  const file = ts.createSourceFile('replay-entry-binding.ts', `(${value})`, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
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
  const script = ts.createSourceFile('replay-entry.ts', sfc.descriptor.scriptSetup.content,
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
    || (noVisibility(ancestor) && !hasRenderingOverride(ancestor)))
}

function templateRootReachable(component, node) {
  return (component.parents.get(node) || []).every(ancestor => ancestor.type !== 1
    || transparentTemplate(ancestor))
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

function exactNode(node, { bindings = {}, statics = {}, booleans = [], events = {}, visibility = null } = {}) {
  if (hasRenderingOverride(node) || !exactDynamicSurface(node, Object.keys(bindings), Object.keys(events))) return false
  if (visibility ? !exactVisibility(node, visibility.name, visibility.expected) : !noVisibility(node)) return false
  if ((node.props || []).some(prop => prop.type === 6 && prop.name !== 'class'
    && Object.hasOwn(bindings, prop.name))) return false
  return Object.entries(bindings).every(([name, value]) => exactDirective(node, 'bind', name, value))
    && Object.entries(statics).every(([name, value]) => staticAttribute(node, name, value))
    && booleans.every(name => booleanAttribute(node, name))
    && Object.entries(events).every(([name, value]) => exactDirective(node, 'on', name, value))
}

function staticText(node) {
  let valid = true
  function collect(current, descendant = false) {
    if (current.type === 2) return current.content
    if (current.type === 3) return ''
    if (current.type === 5 || ignoredTag(current)) { valid = false; return '' }
    if (current.type === 1 && (hasRenderingOverride(current) || (descendant && !noVisibility(current)))) {
      valid = false
      return ''
    }
    return (current.children || []).map(child => collect(child, true)).join('')
  }
  const value = collect(node).replace(/\s+/g, ' ').trim()
  return valid ? value : null
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

function exactBoundNode(node, binding, visibility = null) {
  return !hasRenderingOverride(node)
    && (visibility ? exactVisibility(node, visibility.name, visibility.expected) : noVisibility(node))
    && !(node.props || []).some(prop => prop.type === 6 && prop.name === binding)
    && exactDirective(node, 'bind', binding, 'mobileLandscapeViewport')
}

function hasDefaultImport(script, localName, moduleName) {
  return script.statements.filter(statement => ts.isImportDeclaration(statement)
    && ts.isStringLiteral(statement.moduleSpecifier) && statement.moduleSpecifier.text === moduleName
    && statement.importClause?.name?.text === localName).length === 1
}

function hasExactNamedImports(script, moduleName, names) {
  const expected = new Set(names)
  const totalByLocal = new Map(names.map(name => [name, 0]))
  const correctByLocal = new Map(names.map(name => [name, 0]))
  for (const statement of script.statements) {
    if (!ts.isImportDeclaration(statement) || !ts.isStringLiteral(statement.moduleSpecifier)) continue
    const bindings = statement.importClause?.namedBindings
    if (!bindings || !ts.isNamedImports(bindings)) continue
    for (const element of bindings.elements) {
      const local = element.name.text
      if (!expected.has(local)) continue
      totalByLocal.set(local, totalByLocal.get(local) + 1)
      const imported = element.propertyName?.text || local
      if (statement.moduleSpecifier.text === moduleName && imported === local) {
        correctByLocal.set(local, correctByLocal.get(local) + 1)
      }
    }
  }
  return names.every(name => totalByLocal.get(name) === 1 && correctByLocal.get(name) === 1)
}

export function profileUsesAggregatedStatisticsWithoutReplayHistory(source) {
  const profile = parseComponent(source)
  if (!profile) return false
  let aggregatedStatisticsCalls = 0
  let replayHistoryRequests = 0
  function visit(node) {
    if (ts.isCallExpression(node)) {
      const callee = unwrap(node.expression)
      if (ts.isPropertyAccessExpression(callee) && ts.isIdentifier(unwrap(callee.expression))
        && unwrap(callee.expression).text === 'playerApi' && callee.name.text === 'statistics'
        && node.arguments.length === 1 && ts.isIdentifier(unwrap(node.arguments[0]))
        && unwrap(node.arguments[0]).text === 'range') aggregatedStatisticsCalls++
      if (ts.isIdentifier(callee) && callee.text === 'platformRequest') {
        const path = unwrap(node.arguments[0])
        if (path && ts.isStringLiteral(path) && path.text.startsWith('/api/matches')) replayHistoryRequests++
      }
    }
    ts.forEachChild(node, visit)
  }
  visit(profile.script)
  return aggregatedStatisticsCalls === 1 && replayHistoryRequests === 0
}

const empty = {
  recordsHeaderAndImport: false,
  recordsReplayAvailability: false,
  recordsMobileInlineBlock: false,
  replayMobileBlocker: false,
  replayBoardBinding: false,
  replayControlTeleport: false,
  boardInspectorHandle: false,
  boardMobileInspectorDetails: false,
  boardMobileLayoutConsumers: false,
}

export const replayEntryPredicateNames = Object.freeze(Object.keys(empty))

export function replayEntryTemplateContract(sources) {
  const records = parseComponent(sources.matchRecords)
  const replay = parseComponent(sources.replayPage)
  const board = parseComponent(sources.gameBoard)
  if (!records || !replay || !board) return { ...empty }

  const recordsRoot = unique(records.nodes, node => node.tag === 'section' && hasClass(node, 'match-records')
    && noVisibility(node) && !hasRenderingOverride(node) && templateRootReachable(records, node))
  const recordsHeader = recordsRoot && unique(renderedChildren(recordsRoot), node => node.tag === 'header'
    && hasClass(node, 'records-header') && noVisibility(node) && !hasRenderingOverride(node))
  const heading = recordsHeader && unique(records.nodes, node => node.tag === 'h1' && staticText(node) === '对局回放'
    && noVisibility(node) && !hasRenderingOverride(node) && visiblePath(records, node, recordsHeader))
  const retention = recordsHeader && unique(records.nodes, node => node.tag === 'small'
    && hasClass(node, 'records-retention-note')
    && staticText(node) === '仅保存7天内最近10场回放，历史回放文件可能随版本更新失效。'
    && noVisibility(node) && !hasRenderingOverride(node) && visiblePath(records, node, recordsHeader))
  const fileActions = recordsHeader && unique(renderedChildren(recordsHeader), node => node.tag === 'div'
    && hasClass(node, 'record-file-actions') && noVisibility(node) && !hasRenderingOverride(node))
  const fileInput = fileActions && unique(renderedChildren(fileActions), node => node.tag === 'input'
    && exactNode(node, { statics: { type: 'file', accept: 'application/json,.json' }, events: { change: 'importReplay' } })
    && staticAttribute(node, 'ref', 'fileInput'))
  const importButton = fileActions && unique(renderedChildren(fileActions), node => node.tag === 'button'
    && staticText(node) === '打开 JSON 回放' && exactNode(node, { events: { click: 'openReplayImport' } }))
  const exportButton = fileActions && unique(renderedChildren(fileActions), node => node.tag === 'button'
    && staticText(node) === '保存 JSON'
    && exactNode(node, { bindings: { disabled: '!canUseSelectedReplay' }, events: { click: 'exportReplay' } }))

  const selectedDetail = recordsRoot && unique(records.nodes, node => node.tag === 'main'
    && hasClass(node, 'record-detail') && exactVisibility(node, 'if', 'selected')
    && !hasRenderingOverride(node) && visiblePath(records, node, recordsRoot))
  const launch = selectedDetail && unique(renderedChildren(selectedDetail), node => node.tag === 'section'
    && hasClass(node, 'record-launch') && noVisibility(node) && !hasRenderingOverride(node))
  const cleared = launch && unique(renderedChildren(launch), node => node.tag === 'p'
    && staticText(node) === '这场对局的回放载荷已清理，摘要与结算结果仍保留。'
    && exactVisibility(node, 'if', 'selected.commandCount === 0') && !hasRenderingOverride(node))
  const mobileInline = launch && unique(renderedChildren(launch), node => node.tag === 'p'
    && hasClass(node, 'mobile-replay-inline')
    && staticText(node) === '移动端可查看完整摘要；请到电脑端播放回放。'
    && exactVisibility(node, 'else-if', 'mobileReplayBlocked') && !hasRenderingOverride(node))
  const desktopHint = launch && unique(renderedChildren(launch), node => node.tag === 'p'
    && staticText(node) === '回放将在独立的完整对战界面中打开。'
    && exactVisibility(node, 'else', undefined) && !hasRenderingOverride(node))
  const playButton = launch && unique(renderedChildren(launch), node => node.tag === 'button'
    && hasClass(node, 'primary') && staticText(node) === '播放回放'
    && exactNode(node, { bindings: { disabled: '!canUseSelectedReplay' }, events: { click: 'playSelected' } }))
  const disabledReplayControls = records.nodes.filter(node => exactDirective(node, 'bind', 'disabled', '!canUseSelectedReplay'))

  const replayRoot = unique(replay.nodes, node => node.tag === 'div' && hasClass(node, 'replay-page')
    && noVisibility(node) && !hasRenderingOverride(node) && templateRootReachable(replay, node))
  const mobileBlocker = replayRoot && unique(renderedChildren(replayRoot), node => node.tag === 'main'
    && hasClass(node, 'replay-mobile-blocked') && staticAttribute(node, 'role', 'status')
    && exactVisibility(node, 'if', 'mobileReplayBlocked') && !hasRenderingOverride(node))
  const blockerText = mobileBlocker && unique(renderedChildren(mobileBlocker), node => node.tag === 'p'
    && staticText(node) === '请到电脑端查看回放' && noVisibility(node) && !hasRenderingOverride(node))
  const blockerReturn = mobileBlocker && unique(renderedChildren(mobileBlocker), node => node.tag === 'button'
    && singleInterpolation(node, 'returnLabel') && exactNode(node, { events: { click: 'returnFromReplay' } }))
  const replayBoard = replayRoot && unique(renderedChildren(replayRoot), node => componentTag(node, 'GameBoard', 'game-board')
    && exactNode(node, {
      bindings: {
        game: 'currentGame', 'replay-focus-card': 'replayFocusCard',
        'replay-playback-speed': 'playbackSpeed', 'reveal-both-hands': 'isAdminReplay',
      },
      booleans: ['read-only'],
      events: { 'replay-presentation-change': 'replayPresentationBusy = $event' },
      visibility: { name: 'else-if', expected: 'currentGame' },
    }))
  const controlTeleport = replayRoot && unique(renderedChildren(replayRoot), node => componentTag(node, 'Teleport', 'teleport')
    && exactNode(node, {
      bindings: { to: 'landscapeTeleportTarget()' },
      visibility: { name: 'if', expected: '!mobileReplayBlocked' },
    }))
  const controls = controlTeleport && unique(renderedChildren(controlTeleport), node => node.tag === 'div'
    && hasClass(node, 'replay-controls') && staticAttribute(node, 'aria-label', '回放控制')
    && exactVisibility(node, 'if', 'currentGame') && !hasRenderingOverride(node))

  const boardRoot = unique(board.nodes, node => node.tag === 'div' && hasClass(node, 'board-viewport')
    && noVisibility(node) && !hasRenderingOverride(node) && templateRootReachable(board, node))
  const boardStage = boardRoot && unique(renderedChildren(boardRoot), node => node.tag === 'div'
    && hasClass(node, 'board-stage') && noVisibility(node)
    && !(node.props || []).some(prop => prop.type === 6 && ['hidden', 'style'].includes(prop.name))
    && !(node.props || []).some(prop => prop.type === 7 && ['html', 'text', 'show'].includes(prop.name))
    && directives(node, 'bind', 'style').length === 1)
  const handleTeleport = boardRoot && unique(renderedChildren(boardRoot), node => componentTag(node, 'Teleport', 'teleport')
    && exactNode(node, { bindings: { to: 'landscapeTeleportTarget()' } })
    && Boolean(unique(renderedChildren(node), child => child.tag === 'button'
      && hasClass(child, 'mobile-card-inspector-handle-global'))))
  const inspectorHandle = handleTeleport && unique(renderedChildren(handleTeleport), node => node.tag === 'button'
    && hasClass(node, 'mobile-card-inspector-handle') && hasClass(node, 'mobile-card-inspector-handle-global')
    && singleInterpolation(node, "mobileInspectorOpen ? '收起详情' : '展开卡牌详情'")
    && exactNode(node, {
      bindings: { class: '{ open: mobileInspectorOpen }', 'aria-expanded': 'mobileInspectorOpen' },
      statics: { type: 'button' }, events: { click: 'mobileInspectorOpen = !mobileInspectorOpen' },
      visibility: { name: 'if', expected: 'mobileLandscapeViewport' },
    }))
  const inspectorAside = boardRoot && unique(board.nodes, node => node.tag === 'aside'
    && hasClass(node, 'mobile-card-inspector') && hasClass(node, 'mobile-safe-overlay')
    && staticAttribute(node, 'role', 'dialog') && staticAttribute(node, 'aria-modal', 'false')
    && staticAttribute(node, 'aria-label', '卡牌详情')
    && exactVisibility(node, 'if', 'mobileLandscapeViewport && mobileInspectorOpen')
    && !hasRenderingOverride(node) && boardStage && visiblePath(board, node, boardStage))
  const inspectorBody = inspectorAside && unique(renderedChildren(inspectorAside), node => node.tag === 'div'
    && hasClass(node, 'mobile-card-detail-body')
    && exactVisibility(node, 'if', 'focusCard && focusDetailCard') && !hasRenderingOverride(node))
  const inspectorDetails = inspectorBody && unique(renderedChildren(inspectorBody), node => componentTag(node,
    'CardDetailContent', 'card-detail-content') && exactNode(node, {
    bindings: { card: 'focusDetailCard', 'show-catalog-only': 'false' },
  }))

  const bound = (pascal, kebab, visibility = null) => board.nodes.filter(node => componentTag(node, pascal, kebab)
    && exactBoundNode(node, 'mobile-layout', visibility) && boardStage && visiblePath(board, node, boardStage))
  const handAreas = [
    ...bound('HandArea', 'hand-area', { name: 'if', expected: 'l12State.gmEnabled || showBothHands' }),
    ...bound('HandArea', 'hand-area', { name: 'else', expected: undefined }),
  ]
  const playerMats = bound('PlayerMat', 'player-mat')
  const graveyard = bound('GraveyardOverlay', 'graveyard-overlay', { name: 'if', expected: 'graveyardPlayer !== null' })
  const master = bound('MasterOverlay', 'master-overlay', { name: 'if', expected: 'masterPlayerIndex !== null' })
  const prompt = bound('PromptOverlay', 'prompt-overlay', {
    name: 'if', expected: "!readOnly || game.phase === 'DisasterPreparation'",
  })
  return {
    recordsHeaderAndImport: Boolean(recordsRoot && recordsHeader && heading && retention && fileActions && fileInput && importButton),
    recordsReplayAvailability: Boolean(selectedDetail && launch && cleared && desktopHint && exportButton && playButton
      && disabledReplayControls.length === 2),
    recordsMobileInlineBlock: Boolean(mobileInline
      && !records.nodes.some(node => ['dialog', 'alertdialog'].includes((node.props || [])
        .find(prop => prop.type === 6 && prop.name === 'role')?.value?.content))),
    replayMobileBlocker: Boolean(replayRoot && mobileBlocker && blockerText && blockerReturn),
    replayBoardBinding: Boolean(replayBoard && hasDefaultImport(replay.script, 'GameBoard', './game/GameBoard.vue')
      && replay.nodes.filter(node => componentTag(node, 'GameBoard', 'game-board')).length === 1),
    replayControlTeleport: Boolean(controlTeleport && controls && hasExactNamedImports(replay.script,
      './mobileViewport', ['isMobileDeviceExperience', 'landscapeTeleportTarget'])),
    boardInspectorHandle: Boolean(handleTeleport && inspectorHandle),
    boardMobileInspectorDetails: Boolean(boardStage && inspectorAside && inspectorBody && inspectorDetails
      && hasDefaultImport(board.script, 'CardDetailContent', '../CardDetailContent.vue')),
    boardMobileLayoutConsumers: Boolean(boardStage) && handAreas.length === 2 && playerMats.length === 2
      && graveyard.length === 1 && master.length === 1 && prompt.length === 1,
  }
}

export const replayEntryGroups = contract => [
  ['record entry retention and availability', [contract.recordsHeaderAndImport,
    contract.recordsReplayAvailability, contract.recordsMobileInlineBlock]],
  ['replay route blocks mobile and owns controls', [contract.replayMobileBlocker,
    contract.replayBoardBinding, contract.replayControlTeleport]],
  ['board keeps isolated mobile details', [contract.boardInspectorHandle,
    contract.boardMobileInspectorDetails, contract.boardMobileLayoutConsumers]],
]
