import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse } from '@vue/compiler-dom'
import ts from 'typescript'

const classTokens = node => node.props?.find(prop => prop.type === 6 && prop.name === 'class')
  ?.value?.content?.split(/\s+/).filter(Boolean) || []
const hasClass = (node, token) => classTokens(node).includes(token)
const isComponentTag = (node, pascal, kebab) => node.tag === pascal || node.tag === kebab
const unwrap = node => ts.isParenthesizedExpression(node) ? unwrap(node.expression) : node

function parsedExpression(value) {
  if (!value) return null
  const file = ts.createSourceFile('template-binding.js', `(${value})`, ts.ScriptTarget.Latest, true, ts.ScriptKind.JS)
  if (file.parseDiagnostics.length || file.statements.length !== 1 || !ts.isExpressionStatement(file.statements[0])) return null
  return { node: unwrap(file.statements[0].expression), file }
}

function expressionKey(value) {
  const parsed = parsedExpression(value)
  return parsed && ts.createPrinter({ removeComments: true }).printNode(ts.EmitHint.Expression, parsed.node, parsed.file)
}

const expressionIs = (value, expected) => expressionKey(value) === expressionKey(expected)
const transparentTemplate = node => node?.type === 1 && node.tag === 'template' && node.props.length === 0
const ignoredContainer = node => node?.type === 1 && ['script', 'style'].includes(node.tag.toLowerCase())
const visibilityDirectives = new Set(['if', 'else-if', 'else', 'show', 'for'])

function parseTemplate(source) {
  const sfc = parseSfc(source)
  if (sfc.errors.length || !sfc.descriptor.template) return null
  let ast
  try { ast = parse(sfc.descriptor.template.content, { comments: false }) } catch { return null }
  const nodes = [], parents = new Map()
  function walk(node, ancestors = []) {
    if (node.type === 1 && !ignoredContainer(node) && !ancestors.some(ignoredContainer)) {
      nodes.push(node)
      parents.set(node, ancestors)
    }
    for (const child of node.children || []) walk(child, [...ancestors, node])
  }
  walk(ast)
  return { ast, nodes, parents }
}

function matchingDirectives(node, name, argument) {
  return (node.props || []).filter(prop => prop.type === 7 && prop.name === name
    && (argument === undefined ? !prop.arg : prop.arg?.isStatic && prop.arg.content === argument))
}

function hasUnknownOverride(node, names) {
  return (node.props || []).some(prop => prop.type === 7
    && ((prop.name === 'bind' && (!prop.arg || !prop.arg.isStatic || names.includes(prop.arg.content)))
      || (prop.name === 'on' && !prop.arg)))
}

function hasOnlyDirective(node, name, argument, expected) {
  const matches = matchingDirectives(node, name, argument)
  const visibility = (node.props || []).filter(prop => prop.type === 7 && visibilityDirectives.has(prop.name))
  return matches.length === 1 && visibility.length === 1 && expressionIs(matches[0].exp?.content, expected)
}

const hasNoVisibilityDirective = node => !(node.props || []).some(prop => prop.type === 7 && visibilityDirectives.has(prop.name))

function staticAttribute(node, name, expected) {
  if (hasUnknownOverride(node, [name])) return false
  const matches = (node.props || []).filter(prop => prop.type === 6 && prop.name === name)
  return matches.length === 1 && matches[0].value?.content === expected
}

function noAnnouncementSemantics(node) {
  const names = ['role', 'aria-live', 'aria-atomic']
  return !hasUnknownOverride(node, names)
    && !(node.props || []).some(prop => prop.type === 6 && names.includes(prop.name))
}

function renderedChildren(node) {
  return (node.children || []).flatMap(child => transparentTemplate(child)
    ? renderedChildren(child)
    : child.type === 1 && !ignoredContainer(child) ? [child] : [])
}

function descendants(node) {
  return (node.children || []).flatMap(child => child.type === 1 && !ignoredContainer(child)
    ? [child, ...descendants(child)] : [])
}

function normalizedText(value) {
  return value.replace(/\s+/g, ' ').trim()
}

function exactStaticText(node, expected) {
  let invalid = false
  function collect(current) {
    if (current.type === 2) return current.content
    if (current.type === 3) return ''
    if (current.type === 5 || ignoredContainer(current)) { invalid = true; return '' }
    if (current.type === 1 && (current.props || []).some(prop => prop.type === 7 && ['text', 'html'].includes(prop.name))) {
      invalid = true
      return ''
    }
    if (current.type === 1 && current !== node && (current.props.length || !['b', 'span', 'strong', 'small', 'em', 'i', 'template'].includes(current.tag))) {
      invalid = true
      return ''
    }
    return (current.children || []).map(collect).join('')
  }
  const text = normalizedText(collect(node))
  return !invalid && text === expected
}

function exactInterpolation(node, expected) {
  let interpolationCount = 0
  let valid = true
  function walk(current) {
    if (current.type === 2 && normalizedText(current.content)) valid = false
    else if (current.type === 5) {
      interpolationCount++
      if (!expressionIs(current.content.content, expected)) valid = false
    } else if (current.type === 1) {
      if ((current.props || []).some(prop => prop.type === 7 && ['text', 'html'].includes(prop.name))) valid = false
      if (current !== node && (!['span', 'strong', 'b', 'em', 'small', 'template'].includes(current.tag)
        || current.props.length && !transparentTemplate(current))) valid = false
      for (const child of current.children || []) walk(child)
    }
  }
  walk(node)
  return valid && interpolationCount === 1
}

function exactCopyPair(node, first, second) {
  const children = descendants(node)
  return children.filter(child => child.tag === 'b' && exactStaticText(child, first)).length === 1
    && children.filter(child => child.tag === 'span' && exactStaticText(child, second)).length === 1
}

function unique(nodes, predicate) {
  const matches = nodes.filter(predicate)
  return matches.length === 1 ? matches[0] : null
}

function directBranch(host, tag, directive, condition) {
  return unique(renderedChildren(host), node => node.tag === tag
    && hasOnlyDirective(node, directive, undefined, condition))
}

function under(node, ancestor, parents) {
  return parents.get(node)?.includes(ancestor)
}

function templateBranch(host, parsed, tag, directive, condition) {
  return unique(parsed.nodes, node => {
    if (node.tag !== tag || !hasOnlyDirective(node, directive, undefined, condition)) return false
    const path = parsed.parents.get(node), hostIndex = path.indexOf(host)
    if (hostIndex < 0) return false
    const wrappers = path.slice(hostIndex + 1).filter(parent => parent.type === 1)
    if (!wrappers.every(parent => parent.tag === 'template')) return false
    const branchWrappers = wrappers.filter(parent => !transparentTemplate(parent))
    return branchWrappers.length === 1
      && branchWrappers[0].props.length === 1
      && matchingDirectives(branchWrappers[0], 'else', undefined).length === 1
      && (branchWrappers[0].props || []).filter(prop => prop.type === 7 && visibilityDirectives.has(prop.name)).length === 1
  })
}

function exactNoticeHandler(node) {
  if (hasUnknownOverride(node, []) || !hasNoVisibilityDirective(node)) return false
  const handlers = matchingDirectives(node, 'on', 'notice')
  return handlers.length === 1 && expressionIs(handlers[0].exp?.content, 'value => notice = value')
}

const emptyContract = {
  accountLoadingCondition: false,
  accountLoadingStatus: false,
  accountFailureConditionAndOrder: false,
  accountFailureAlert: false,
  accountNoticeCondition: false,
  accountNoticeText: false,
  accountNoticeStatus: false,
  accountNoticeLivePoliteSameHost: false,
  scheduleEmpty: false,
  standingsEmpty: false,
  passiveEmptyStates: false,
  judgePersonalMatchCondition: false,
  judgePersonalEmptyElse: false,
  judgeVisibleCasesEmpty: false,
  accountJudgeComponent: false,
  accountManagementComponent: false,
  accountComponentsShareNotice: false,
}

export const tournamentNoticePredicateNames = Object.freeze(Object.keys(emptyContract))

export function tournamentNoticeTemplateContract(accountSource, judgeSource) {
  const account = parseTemplate(accountSource)
  const judge = parseTemplate(judgeSource)
  if (!account || !judge) return { ...emptyContract }

  const accountHost = unique(account.nodes, node => node.tag === 'main' && hasClass(node, 'detail-page'))
  const judgeHost = unique(judge.nodes, node => node.tag === 'section' && hasClass(node, 'judge-desk'))
  if (!accountHost || !judgeHost) return { ...emptyContract }

  const accountChildren = renderedChildren(accountHost)
  const loading = directBranch(accountHost, 'p', 'if', 'loading')
  const failure = directBranch(accountHost, 'p', 'else-if', '!tournament')
  const notice = templateBranch(accountHost, account, 'p', 'if', 'notice')
  const loadingIndex = accountChildren.indexOf(loading)
  const failureIndex = accountChildren.indexOf(failure)

  const schedule = templateBranch(accountHost, account, 'section', 'else-if', "tab === 'schedule'")
  const scheduleEmpty = schedule && unique(descendants(schedule), node => node.tag === 'div' && hasClass(node, 'empty-state')
    && hasOnlyDirective(node, 'if', undefined, '!rounds.length'))
  const standings = templateBranch(accountHost, account, 'section', 'else-if', "tab === 'standings'")
  const standingsEmpty = standings && unique(descendants(standings), node => node.tag === 'div' && hasClass(node, 'empty-state')
    && hasOnlyDirective(node, 'if', undefined, '!(tournament.finalSwissStandings.length || rounds.at(-1)?.standings?.length)'))

  const judgeSection = templateBranch(accountHost, account, 'section', 'else-if', "tab === 'judge'")
  const managementSection = templateBranch(accountHost, account, 'section', 'else-if', "tab === 'manage'")
  const judgeComponent = judgeSection && unique(renderedChildren(judgeSection), node => isComponentTag(node, 'TournamentJudgeDesk', 'tournament-judge-desk')
    && hasNoVisibilityDirective(node))
  const managementComponent = managementSection && unique(renderedChildren(managementSection), node => isComponentTag(node, 'TournamentManagementPanel', 'tournament-management-panel')
    && hasNoVisibilityDirective(node))

  const judgeChildren = renderedChildren(judgeHost)
  const call = unique(judgeChildren, node => node.tag === 'form' && hasClass(node, 'call')
    && hasOnlyDirective(node, 'if', undefined, 'myMatches.length'))
  const personalEmpty = unique(judgeChildren, node => node.tag === 'p' && hasClass(node, 'empty')
    && matchingDirectives(node, 'else', undefined).length === 1
    && (node.props || []).filter(prop => prop.type === 7 && visibilityDirectives.has(prop.name)).length === 1)
  const visibleCasesEmpty = unique(judgeChildren, node => node.tag === 'div' && hasClass(node, 'empty')
    && hasOnlyDirective(node, 'if', undefined, '!tournament.judgeCases.length'))

  return {
    accountLoadingCondition: Boolean(loading),
    accountLoadingStatus: Boolean(loading && staticAttribute(loading, 'role', 'status')
      && exactStaticText(loading, '正在加载赛事…')),
    accountFailureConditionAndOrder: Boolean(failure && loadingIndex >= 0 && failureIndex === loadingIndex + 1),
    accountFailureAlert: Boolean(failure && staticAttribute(failure, 'role', 'alert')
      && exactInterpolation(failure, 'notice')),
    accountNoticeCondition: Boolean(notice),
    accountNoticeText: Boolean(notice && exactInterpolation(notice, 'notice')),
    accountNoticeStatus: Boolean(notice && staticAttribute(notice, 'role', 'status')),
    accountNoticeLivePoliteSameHost: Boolean(notice && under(notice, accountHost, account.parents)
      && staticAttribute(notice, 'aria-live', 'polite')),
    scheduleEmpty: Boolean(scheduleEmpty && exactCopyPair(scheduleEmpty, '赛程尚未生成', '主办者完成开赛检查后，这里会显示轮次与桌次。')),
    standingsEmpty: Boolean(standingsEmpty && exactCopyPair(standingsEmpty, '排名尚未产生', '完成轮次后会在这里显示权威排名。')),
    passiveEmptyStates: Boolean(scheduleEmpty && standingsEmpty
      && noAnnouncementSemantics(scheduleEmpty) && noAnnouncementSemantics(standingsEmpty)),
    judgePersonalMatchCondition: Boolean(call),
    judgePersonalEmptyElse: Boolean(call && personalEmpty
      && judgeChildren.indexOf(personalEmpty) === judgeChildren.indexOf(call) + 1
      && exactCopyPair(personalEmpty, '当前没有可呼叫的桌次', '只有本人参与的赛事桌次可以发起裁判请求。')),
    judgeVisibleCasesEmpty: Boolean(visibleCasesEmpty
      && exactCopyPair(visibleCasesEmpty, '暂无可见案件', '新的裁判请求会按权限显示在这里。')),
    accountJudgeComponent: Boolean(judgeComponent),
    accountManagementComponent: Boolean(managementComponent),
    accountComponentsShareNotice: Boolean(judgeComponent && managementComponent
      && exactNoticeHandler(judgeComponent) && exactNoticeHandler(managementComponent)),
  }
}

export const tournamentNoticeGroups = contract => [
  ['account loading and failure branch', [
    contract.accountLoadingCondition,
    contract.accountLoadingStatus,
    contract.accountFailureConditionAndOrder,
    contract.accountFailureAlert,
  ]],
  ['account result notice', [
    contract.accountNoticeCondition,
    contract.accountNoticeText,
    contract.accountNoticeStatus,
    contract.accountNoticeLivePoliteSameHost,
  ]],
  ['schedule and standings passive empty states', [
    contract.scheduleEmpty,
    contract.standingsEmpty,
    contract.passiveEmptyStates,
  ]],
  ['judge personal and visible-case empty states', [
    contract.judgePersonalMatchCondition,
    contract.judgePersonalEmptyElse,
    contract.judgeVisibleCasesEmpty,
  ]],
  ['account rendered tournament components share notices', [
    contract.accountJudgeComponent,
    contract.accountManagementComponent,
    contract.accountComponentsShareNotice,
  ]],
]
