import assert from 'node:assert/strict'
import fs from 'node:fs'
import { actionGateTemplateContract, publicContentSaveDisabledBinding } from './action-gate-template-contract.mjs'

const read = name => fs.readFileSync(new URL(`../src/l12/site/${name}.vue`, import.meta.url), 'utf8')
const originals = [read('FriendsPage'), read('PublicDeckContentEditor'), read('AdminTournamentWorkbench')]
let checks = 0
const pass = sources => { assert.deepEqual(Object.entries(actionGateTemplateContract(...sources)).filter(([, value]) => !value), []); checks++ }
const fail = (index, before, after, field) => {
  assert(originals[index].includes(before), `fixture marker missing: ${before}`)
  const sources = [...originals]
  sources[index] = sources[index].replace(before, after)
  assert.equal(actionGateTemplateContract(...sources)[field], false, `${field}: ${after.slice(0, 80)}`)
  checks++
}

pass(originals)
pass(originals.map(source => source.replaceAll(':disabled="actionPending(', ':disabled=" actionPending(')))
pass(originals.map(source => source.replaceAll('class="friends-page"', "class='extra friends-page'")
  .replaceAll('class="public-content-editor grand-panel"', 'class="grand-panel extra public-content-editor"')
  .replaceAll('class="tournament-page"', 'class="extra tournament-page"')))
pass(originals.map(source => source.replaceAll('@click="saveContent"', '@click.stop.prevent="(saveContent)"')
  .replaceAll('@click="join(item)"', '@click.prevent=" (join(item)) "')
  .replaceAll('@click="invite(selected)"', '@click.stop=" (invite(selected)) "')))
pass(originals.map(source => source.replace('<template>', '<template><template>').replace(/<\/template>\r?\n\r?\n<style/, '</template></template>\n\n<style')))
pass(originals.map(source => source.replace('player in incoming', '(player) of (incoming)')
  .replace('item in visibleTournaments', '(item) of (visibleTournaments)')))

for (const [index, root, field] of [[0, 'class="friends-page"', 'friendsPending'], [1, 'class="public-content-editor grand-panel"', 'publicPending'], [2, 'class="tournament-page"', 'tournamentPending']]) {
  for (const hidden of ['hidden', 'inert', 'v-show="false"', 'v-if="(false)"', 'style="display: none"', 'style="visibility: hidden!important"'])
    fail(index, root, `${root} ${hidden}`, field)
  fail(index, root, root.replace(/(friends-page|public-content-editor|tournament-page)/, 'wrong-host'), field)
}
for (const handler of ['invite(selected)', 'spectate(selected)', 'remove(selected)', 'blockPlayer(selected)', 'blockPlayer(player)', 'resolve(player, false)', 'resolve(player, true)', 'add(player)', 'unblock(player)']) {
  fail(0, `@click="${handler}"`, `@click="wrongAction(player)"`, 'friendsPending')
}
for (const [before, after] of [
  [':disabled="actionPending(actionKey(selected))"', ':disabled="actionPending(actionKey(player))"'],
  [':disabled="actionPending(actionKey(player))"', ':disabled="actionPending(actionKey(selected))"'],
  [':disabled="actionPending(actionKey(selected))"', ':disabled="false"'],
  [':disabled="actionPending(actionKey(selected))"', ':disabled="busy && actionPending(actionKey(selected))"'],
  [':disabled="actionPending(actionKey(selected))"', 'disabled'],
  ['player in incoming', 'player in results'],
  ['v-if="selected"', 'v-if="wrongPlayer"'],
  ['@click="invite(selected)"', '@click.once="invite(selected)"'],
  ['@click="invite(selected)"', '@click.right="invite(selected)"'],
  ['@click="invite(selected)"', '@click.middle="invite(selected)"'],
  ['@click="invite(selected)"', '@click="invite(selected)" v-on="handlers"'],
  ['@click="invite(selected)"', '@click="invite(selected)" v-bind="overrides"'],
  ['@click="invite(selected)"', '@click="invite(selected)" :[key]="override"'],
]) fail(0, before, after, 'friendsPending')
fail(0, ':aria-busy="actionBusy"', ':aria-busy="busy"', 'friendsBusy')
fail(0, ':aria-busy="actionBusy"', 'aria-busy="actionBusy"', 'friendsBusy')
fail(0, ':aria-busy="actionBusy"', ':aria-busy="actionBusy" v-bind="overrides"', 'friendsBusy')
fail(0, ':aria-busy="actionBusy"', '><span :aria-busy="actionBusy"></span><div', 'friendsBusy')

for (const [index, handler, lock, field] of [[1, 'saveContent', publicContentSaveDisabledBinding, 'publicPending'], [2, 'join(item)', 'actionPending(tournamentActionKey(item))', 'tournamentPending']]) {
  fail(index, `@click="${handler}"`, '@click="wrongHandler"', field)
  fail(index, `:disabled="${lock}"`, ':disabled="false"', field)
  fail(index, `@click="${handler}"`, `@click="${handler}" v-bind="overrides"`, field)
  fail(index, `@click="${handler}"`, `@click="${handler}" v-on="handlers"`, field)
  fail(index, `@click="${handler}"`, `@click.once="${handler}"`, field)
  fail(index, `@click="${handler}"`, `@click="${handler}" hidden`, field)
  fail(index, `:disabled="${lock}"`, `:disabled="${lock.replace('actionKey', 'otherKey').replace('tournamentActionKey', 'otherKey')}"`, field)
}
fail(2, 'item in visibleTournaments', 'item in archivedTournaments', 'tournamentPending')
fail(1, '@click="saveContent"', '@click="saveContent" :onClick="otherHandler"', 'publicPending')
fail(1, '@click="saveContent"', '@click="saveContent" onclick="otherHandler()"', 'publicPending')
fail(1, '<footer>', '<footer hidden>', 'publicPending')
fail(0, '<main v-if="selected">', '<main v-if="selected" style="display:none">', 'friendsPending')
fail(2, '<section v-if="tab !== \'create\'" class="registry">', '<section v-if="false" class="registry">', 'tournamentPending')
// Matching source text outside the rendered template cannot rescue a lock.
for (const decoy of ['<!-- <button :disabled="isPending(actionKey)" @click="saveContent">fake</button> -->',
  '<script>const fake=\'<button :disabled="isPending(actionKey)" @click="saveContent">fake</button>\'</script>',
  '<style>/* <button :disabled="isPending(actionKey)" @click="saveContent">fake</button> */</style>']) {
  const sources = [...originals]
  sources[1] = sources[1].replace(`:disabled="${publicContentSaveDisabledBinding}"`, ':disabled="false"') + decoy
  assert.equal(actionGateTemplateContract(...sources).publicPending, false); checks++
}
// Hidden duplicate controls and extra visible copies cannot satisfy uniqueness.
fail(1, '@click="saveContent"', '@click="wrongHandler"', 'publicPending')
const duplicated = [...originals]
duplicated[1] = duplicated[1].replace('<footer>', `<footer><button :disabled="${publicContentSaveDisabledBinding}" @click="saveContent">duplicate</button>`)
assert.equal(actionGateTemplateContract(...duplicated).publicPending, false); checks++
for (const [index, rootClass, field] of [[0, 'friends-page', 'friendsPending'], [1, 'public-content-editor', 'publicPending'], [2, 'tournament-page', 'tournamentPending']]) {
  const sources = [...originals]
  sources[index] = sources[index].replace('<template>', `<template><${index === 1 ? 'section' : 'div'} class="${rootClass}"></${index === 1 ? 'section' : 'div'}>`)
  assert.equal(actionGateTemplateContract(...sources)[field], false); checks++
}
const hiddenDecoy = [...originals]
hiddenDecoy[1] = hiddenDecoy[1].replace('@click="saveContent"', '@click="wrongHandler"')
  .replace('<footer>', '<footer><template v-if="false"><button :disabled="isPending(actionKey)" @click="saveContent">fake</button></template>')
assert.equal(actionGateTemplateContract(...hiddenDecoy).publicPending, false); checks++
const externalDecoy = [...originals]
externalDecoy[1] = externalDecoy[1].replace('@click="saveContent"', '@click="wrongHandler"')
  .replace('<template>', '<template><button :disabled="isPending(actionKey)" @click="saveContent">outside</button>')
assert.equal(actionGateTemplateContract(...externalDecoy).publicPending, false); checks++
console.log(`A3 action pending template contract: ${checks}/${checks} cases passed`)

const reorderedPublic = [...originals]
reorderedPublic[1] = reorderedPublic[1].replace(publicContentSaveDisabledBinding,
  '(isPending(actionKey)) || (refreshRequired) || (!readGeneration) || (loading)')
pass(reorderedPublic)
for (const missing of ['loading', '!readGeneration', 'refreshRequired', 'isPending(actionKey)']) {
  fail(1, `:disabled="${publicContentSaveDisabledBinding}"`,
    `:disabled="${publicContentSaveDisabledBinding.split(' || ').filter(term => term !== missing).join(' || ')}"`, 'publicPending')
}
fail(1, `:disabled="${publicContentSaveDisabledBinding}"`, ':disabled="true"', 'publicPending')
fail(1, `:disabled="${publicContentSaveDisabledBinding}"`, ':disabled="loading && isPending(actionKey)"', 'publicPending')
console.log('Public content save availability: 7 reordered/missing-term/inert-button cases passed; original cases retained')
