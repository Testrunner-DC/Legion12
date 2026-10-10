import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse } from '@vue/compiler-dom'

// Class order/extra state classes are presentation details, not a contract.
export function hasTemplateClass(source, required, forbidden = [], options = {}) {
  const template = parseSfc(source).descriptor.template?.content
  if (!template) return false
  const ast = parse(template, { comments: false })
  const tokens = node => node.props?.find(p => p.type === 6 && p.name === 'class')?.value?.content?.split(/\s+/) || []
  const includes = (node, classes) => classes.every(token => tokens(node).includes(token))
  function walk(node, ancestors = []) {
    const classes = node.props?.find(p => p.type === 6 && p.name === 'class')?.value?.content?.split(/\s+/) || []
    if (required.every(token => classes.includes(token)) && forbidden.every(token => !classes.includes(token))
      && (!options.tag || node.tag === options.tag)
      && (!options.within || ancestors.some(parent => includes(parent, options.within)))
      && !(options.withoutAncestors || []).some(token => ancestors.some(parent => tokens(parent).includes(token)))) return true
    return (node.children || []).some(child => walk(child, [...ancestors, node]))
  }
  return walk(ast)
}

export function hasInvitationTemplate(source) {
  const template = parseSfc(source).descriptor.template?.content
  if (!template) return false
  const ast = parse(template, { comments: false })
  const tokens = node => node.props?.find(p => p.type === 6 && p.name === 'class')?.value?.content?.split(/\s+/) || []
  const stacks = []
  function collect(node, ancestors = []) {
    if (tokens(node).includes('invitation-stack')) stacks.push({ node, ancestors })
    for (const child of node.children || []) collect(child, [...ancestors, node])
  }
  collect(ast)
  // Both directions belong to the same actual stack, not unrelated lookalikes.
  if (stacks.length !== 1) return false
  const { node, ancestors } = stacks[0]
  if (node.tag !== 'div' || [...ancestors, node].some(parent => tokens(parent).includes('site-modal-mask'))) return false
  const directions = ['invitation-gate', 'outgoing-invitation-gate'].map(gate => {
    const matches = (node.children || []).filter(child => tokens(child).includes(gate))
    return matches.length === 1 && matches[0].tag === 'div' && !tokens(matches[0]).includes('site-modal-mask') ? matches[0] : null
  })
  return directions.every(Boolean) && directions[0] !== directions[1]
    && !hasTemplateClass(source, ['invitation-gate', 'site-modal-mask'])
}
