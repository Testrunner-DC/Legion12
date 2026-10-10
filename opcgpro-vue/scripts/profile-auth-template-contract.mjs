import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse } from '@vue/compiler-dom'
import ts from 'typescript'

const classes = node => node.props?.find(p => p.type === 6 && p.name === 'class')?.value?.content?.split(/\s+/) || []
const attribute = (node, name) => node.props?.find(p => p.type === 6 && p.name === name)?.value?.content
const hasAttribute = (node, name) => node.props?.some(p => p.type === 6 && p.name === name)
const directive = (node, name, argument) => node.props?.find(p => p.type === 7 && p.name === name
  && (argument === undefined || (p.arg?.isStatic && p.arg.content === argument)))
const unknownBindings = node => node.props?.some(p => p.type === 7 && ['bind','on'].includes(p.name)
  && (!p.arg || !p.arg.isStatic))
const unwrap = node => ts.isParenthesizedExpression(node) ? unwrap(node.expression) : node
function expression(value) {
  if (!value) return null
  const file = ts.createSourceFile('binding.js', '(' + value + ')', ts.ScriptTarget.Latest, true, ts.ScriptKind.JS)
  if (file.parseDiagnostics.length || file.statements.length !== 1 || !ts.isExpressionStatement(file.statements[0])) return null
  return unwrap(file.statements[0].expression)
}
const identifier = (node, name) => Boolean(node && ts.isIdentifier(unwrap(node)) && unwrap(node).text === name)
function authField(node, name) {
  if (!node) return false
  node = unwrap(node)
  return ts.isPropertyAccessExpression(node) && !node.questionDotToken && identifier(node.expression, 'auth') && node.name.text === name
}
function disabledAuthGuard(node) {
  if (!node) return false
  const terms = []
  function flatten(node) {
    node = unwrap(node)
    if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.BarBarToken) {
      flatten(node.left); flatten(node.right)
    } else terms.push(node)
  }
  flatten(node)
  const busy = terms.filter(node => identifier(node, 'authBusy'))
  const emptyPassword = terms.filter(node => ts.isPrefixUnaryExpression(node) && node.operator === ts.SyntaxKind.ExclamationToken
    && authField(node.operand, 'password'))
  const emptyUsername = terms.filter(node => {
    if (!ts.isPrefixUnaryExpression(node) || node.operator !== ts.SyntaxKind.ExclamationToken) return false
    const call = unwrap(node.operand)
    if (!ts.isCallExpression(call) || call.arguments.length) return false
    const member = unwrap(call.expression)
    return ts.isPropertyAccessExpression(member) && member.name.text === 'trim' && authField(member.expression, 'username')
  })
  return terms.length === 3 && busy.length === 1 && emptyUsername.length === 1 && emptyPassword.length === 1
}

export function hasProfileAuthTemplate(source) {
  const sfc = parseSfc(source)
  if (sfc.errors.length || !sfc.descriptor.template) return false
  let ast
  try { ast = parse(sfc.descriptor.template.content, { comments: false }) } catch { return false }
  const nodes = [], parents = new Map()
  function walk(node, ancestors = []) {
    if (node.type === 1) { nodes.push(node); parents.set(node, ancestors) }
    for (const child of node.children || []) walk(child, [...ancestors, node])
  }
  walk(ast)
  // Class tokens identify one actual form; comments, scripts and sibling forms cannot contribute.
  const candidates = nodes.filter(node => classes(node).includes('auth-form'))
  if (candidates.length !== 1) return false
  const form = candidates[0]
  if (form.tag !== 'form' || !classes(form).includes('account-form') || unknownBindings(form)
    || directive(form,'bind','id')) return false
  const submit = directive(form, 'on', 'submit')
  if (!submit || !submit.modifiers.some(modifier => (typeof modifier === 'string' ? modifier : modifier.content) === 'prevent')
    || !identifier(expression(submit.exp?.content), 'submitAuth')) return false
  const owned = nodes.filter(node => parents.get(node).includes(form))
  if (owned.some(node => node.tag === 'form')) return false
  const controls = owned.filter(node => ['input','button','UiButton'].includes(node.tag))
  const formId = attribute(form, 'id')
  if (controls.some(node => unknownBindings(node) || directive(node,'bind','form')
    || hasAttribute(node,'form') && (!formId || attribute(node,'form') !== formId))) return false
  const inputs = owned.filter(node => node.tag === 'input')
  const modelField = (node, field) => {
    const model = directive(node,'model')
    return model && !model.arg && authField(expression(model.exp?.content), field)
  }
  const usernames = inputs.filter(node => modelField(node,'username'))
  const passwords = inputs.filter(node => modelField(node,'password'))
  if (usernames.length !== 1 || passwords.length !== 1 || usernames[0] === passwords[0]
    || !hasAttribute(usernames[0],'required') || !hasAttribute(passwords[0],'required')
    || inputs.filter(node => hasAttribute(node,'required')).length !== 2
    || directive(usernames[0],'bind','required') || directive(passwords[0],'bind','required')
    || ![undefined,'text'].includes(attribute(usernames[0],'type')) || attribute(passwords[0],'type') !== 'password'
    || directive(usernames[0],'bind','type') || directive(passwords[0],'bind','type')) return false
  // Native button's missing/invalid type submits; UiButton defaults to button, not submit.
  // Bound types are unknown submission entrances and cannot silently bypass the guard.
  function submitKind(node) {
    if (directive(node,'bind','type')) return 'unknown'
    const type = attribute(node,'type')?.toLowerCase()
    if (node.tag === 'button') return ['button','reset'].includes(type) ? 'not-submit' : 'submit'
    if (node.tag === 'UiButton') return type === 'submit' ? 'submit' : type === undefined || ['button','reset'].includes(type) ? 'not-submit' : 'unknown'
    return ['submit','image'].includes(type) ? 'submit' : 'not-submit'
  }
  const associated = formId ? nodes.filter(node => !owned.includes(node) && ['input','button','UiButton'].includes(node.tag)
    && attribute(node,'form') === formId) : []
  const submitControls = [...controls,...associated].filter(node => submitKind(node) !== 'not-submit')
  if (!submitControls.some(node => node.tag === 'UiButton' && attribute(node,'type') === 'submit')) return false
  if (submitControls.some(node => unknownBindings(node) || submitKind(node) === 'unknown'
    || !disabledAuthGuard(expression(directive(node,'bind','disabled')?.exp?.content))
    || node.tag === 'UiButton' && !identifier(expression(directive(node,'bind','busy')?.exp?.content),'authBusy'))) return false
  const notices = owned.filter(node => classes(node).includes('auth-notice'))
  if (notices.length !== 1) return false
  const notice = notices[0]
  if (notice.tag !== 'UiNotice' || unknownBindings(notice)
    || ['kind','role','aria-live','aria-atomic'].some(name => directive(notice,'bind',name))
    || !identifier(expression(directive(notice,'if')?.exp?.content),'authNotice')
    || attribute(notice,'kind') !== 'error' || attribute(notice,'role') !== 'alert'
    || attribute(notice,'aria-live') !== 'assertive' || attribute(notice,'aria-atomic') !== 'true') return false
  function hasNoticeText(node) {
    return node.type === 5 && identifier(expression(node.content.content),'authNotice')
      || (node.children || []).some(hasNoticeText)
  }
  return hasNoticeText(notice)
}

export function hasProfileStatusNoticeBeforeRank(source) {
  const sfc = parseSfc(source)
  if (sfc.errors.length || !sfc.descriptor.template) return false
  let ast
  try { ast = parse(sfc.descriptor.template.content, { comments: false }) } catch { return false }
  const nodes = [], parents = new Map()
  function walk(node, ancestors = []) {
    if (node.type === 1) { nodes.push(node); parents.set(node, ancestors) }
    for (const child of node.children || []) walk(child, [...ancestors, node])
  }
  walk(ast)
  const hosts = nodes.filter(node => classes(node).includes('profile-page'))
  const notices = nodes.filter(node => classes(node).includes('notice'))
  const ranks = nodes.filter(node => classes(node).includes('rank-overview'))
  if (hosts.length !== 1 || notices.length !== 1 || ranks.length !== 1) return false
  const [host] = hosts, [notice] = notices, [rank] = ranks
  if (host.tag !== 'div' || notice.tag !== 'UiNotice' || rank.tag !== 'section' || notice === rank
    || [host,notice,rank].some(unknownBindings)
    || [host,notice,rank].some(node => directive(node,'bind','class'))) return false
  // The page is an actual template root (unconditional transparent templates are equivalent).
  const transparent = node => node.tag === 'template' && node.props.length === 0
  if (parents.get(host).some(node => node.type === 1 && !transparent(node))) return false
  function pathFromHost(node) {
    const path = parents.get(node), index = path.indexOf(host)
    return index < 0 ? null : path.slice(index + 1).filter(parent => parent.type === 1)
  }
  const noticePath = pathFromHost(notice), rankPath = pathFromHost(rank)
  if (!noticePath || !rankPath || !noticePath.every(transparent)) return false
  // Rank may retain its native seasonal wrapper, but forms, slots, components and Teleports cannot lend nodes.
  if (rankPath.some(node => !['div','section','article','main','aside','template'].includes(node.tag)
    || node.tag === 'template' && !transparent(node) || unknownBindings(node))) return false
  if (['role','aria-live','aria-atomic'].some(name => directive(notice,'bind',name))
    || ['else','else-if','for','show','text','html','slot'].some(name => directive(notice,name))
    || !identifier(expression(directive(notice,'if')?.exp?.content),'notice')
    || attribute(notice,'role') !== 'status' || attribute(notice,'aria-live') !== 'polite'
    || attribute(notice,'aria-atomic') !== 'true') return false
  // Read real rendered text, not strings inside scripts, comments, conditional or slot children.
  function hasNoticeText(node) {
    if (node.type === 5) return identifier(expression(node.content.content),'notice')
    if (node.type === 1 && (!['span','strong','b','em','small','template'].includes(node.tag)
      || node.tag === 'template' && !transparent(node) || unknownBindings(node)
      || node.props.some(prop => prop.type === 7))) return false
    return (node.children || []).some(hasNoticeText)
  }
  return notice.children.some(hasNoticeText) && nodes.indexOf(notice) < nodes.indexOf(rank)
}
