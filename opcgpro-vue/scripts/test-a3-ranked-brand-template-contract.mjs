import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { hasMasterTitleBrandImage } from './ranked-brand-template-contract.mjs'

const source = readFileSync(new URL('../src/l12/RankedIdentityBadge.vue', import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const image = '    <img v-if="variant === \'master-title\'" class="identity-brand-logo" :src="siteBrandIcon" alt="" aria-hidden="true" />'
const fallback = '    <i v-else-if="badgeIcon" aria-hidden="true">{{ badgeIcon }}</i>'
const replaceOnce = (value, before, after) => {
  const index = value.indexOf(before)
  assert.notEqual(index, -1, `fixture marker missing: ${before}`)
  return value.slice(0, index) + after + value.slice(index + before.length)
}
let cases = 0
const accepts = (name, value) => { assert.equal(hasMasterTitleBrandImage(value), true, name); cases++ }
const rejects = (name, value) => { assert.equal(hasMasterTitleBrandImage(value), false, `${name} must fail closed`); cases++ }

accepts('real RankedIdentityBadge template must pass', source)

const reordered = replaceOnce(source,
  'class="identity-brand-logo" :src="siteBrandIcon" alt="" aria-hidden="true"',
  'aria-hidden="true" class="selected identity-brand-logo glowing" alt="" :src="siteBrandIcon"')
accepts('static class order, extra state classes and attribute order are presentation details', reordered)

const spaced = replaceOnce(replaceOnce(source,
  'v-if="variant === \'master-title\'"', 'v-if=" ( variant === \'master-title\' ) "'),
  ':src="siteBrandIcon"', ':src=" ( siteBrandIcon ) "')
accepts('binding whitespace and transparent parentheses preserve semantics', spaced)

const transparent = replaceOnce(source, `${image}\n${fallback}`,
  `    <template>\n${image}\n${fallback}\n    </template>`)
accepts('an unconditional template around the complete branch is render-transparent', transparent)

rejects('wrong master-title branch', replaceOnce(source,
  'v-if="variant === \'master-title\'"', 'v-if="variant === \'faction-title\'"'))
rejects('concatenated branch condition', replaceOnce(source,
  'v-if="variant === \'master-title\'"', 'v-if="variant === \'master-title\' || compact"'))
rejects('hidden image directive', replaceOnce(source,
  'v-if="variant === \'master-title\'"', 'v-if="variant === \'master-title\'" v-show="false"'))
rejects('hidden badge ancestor', replaceOnce(source,
  'class="ranked-identity-badge"', 'v-show="false" class="ranked-identity-badge"'))
rejects('native-hidden badge ancestor', replaceOnce(source,
  'class="ranked-identity-badge"', 'hidden class="ranked-identity-badge"'))
rejects('hidden branch ancestor', replaceOnce(source, `${image}\n${fallback}`,
  `    <template v-if="false">\n${image}\n${fallback}\n    </template>`))
rejects('same class on non-image', replaceOnce(source, '<img v-if=', '<div v-if='))
rejects('wrong image class', replaceOnce(source, 'class="identity-brand-logo"', 'class="identity-brand-mark"'))
rejects('native-hidden image', replaceOnce(source,
  'class="identity-brand-logo"', 'hidden class="identity-brand-logo"'))
rejects('dynamic class override', replaceOnce(source,
  'class="identity-brand-logo"', 'class="identity-brand-logo" :class="dynamicClass"'))
rejects('object bind override', replaceOnce(source,
  ':src="siteBrandIcon"', 'v-bind="imageAttributes" :src="siteBrandIcon"'))
rejects('unknown bound attribute', replaceOnce(source,
  ':src="siteBrandIcon"', ':data-state="state" :src="siteBrandIcon"'))
rejects('wrong image source', replaceOnce(source, ':src="siteBrandIcon"', ':src="otherIcon"'))
rejects('concatenated image source', replaceOnce(source, ':src="siteBrandIcon"', ':src="siteBrandIcon + suffix"'))
rejects('non-empty alternative text', replaceOnce(source, 'alt=""', 'alt="十二军团"'))
rejects('exposed decorative image', replaceOnce(source, 'aria-hidden="true"', 'aria-hidden="false"'))
rejects('duplicate master image', replaceOnce(source, image, `${image}\n${image}`))
rejects('HTML overrides badge contents', replaceOnce(source,
  'class="ranked-identity-badge"', 'v-html="replacement" class="ranked-identity-badge"'))
rejects('text overrides badge contents', replaceOnce(source,
  'class="ranked-identity-badge"', 'v-text="replacement" class="ranked-identity-badge"'))

const fake = `<script setup>const fake = '<img v-if="variant === master-title" class="identity-brand-logo">'</script>
<template><span class="ranked-identity-badge"><!--
  <img v-if="variant === 'master-title'" class="identity-brand-logo" :src="siteBrandIcon" alt="" aria-hidden="true" />
--><script>const hidden = 'identity-brand-logo'</script></span></template>`
rejects('comment and script lookalikes', fake)

console.log(`A3 ranked brand template contract passed: 1 migrated predicate with ${cases} focused cases`)
