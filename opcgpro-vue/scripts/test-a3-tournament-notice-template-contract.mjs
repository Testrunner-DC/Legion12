import assert from 'node:assert/strict'
import fs from 'node:fs'
import {
  tournamentNoticeGroups,
  tournamentNoticePredicateNames,
  tournamentNoticeTemplateContract,
} from './tournament-notice-template-contract.mjs'

const account = fs.readFileSync('src/l12/site/TournamentAccountDetail.vue', 'utf8')
const judge = fs.readFileSync('src/l12/site/TournamentJudgeDesk.vue', 'utf8')
const judgeTag = '<TournamentJudgeDesk :tournament="tournament" :can-judge="canJudge" @updated="value => tournament = value" @notice="value => notice = value" />'
const managementTag = '<TournamentManagementPanel :tournament="tournament" :account-id="accountId" :can-organize="canOrganize" @updated="value => tournament = value" @notice="value => notice = value" />'
const contract = (accountSource = account, judgeSource = judge) => tournamentNoticeTemplateContract(accountSource, judgeSource)
const replaceOnce = (source, before, after) => {
  const index = source.indexOf(before)
  assert.notEqual(index, -1, `fixture marker missing: ${before.slice(0, 60)}`)
  return source.slice(0, index) + after + source.slice(index + before.length)
}
const expectFalse = (name, accountSource = account, judgeSource = judge) => {
  assert.equal(contract(accountSource, judgeSource)[name], false, `${name} must fail closed`)
}

const baseline = contract()
assert.equal(tournamentNoticePredicateNames.length, 17, 'A3 notice slice must stay within the approved 17 semantic predicates')
assert.deepEqual(tournamentNoticeGroups(baseline).map(([, predicates]) => predicates.length), [4, 4, 3, 3, 3])
assert.deepEqual(Object.entries(baseline).filter(([, passed]) => !passed), [], 'real tournament templates must satisfy every notice contract')

const reorderedClasses = account
  .replace('class="detail-page"', 'class="extra-shell   detail-page"')
  .replace('v-if="notice" class="notice"', 'v-if=" ( notice ) " class="result-state notice"')
assert.deepEqual(Object.entries(contract(reorderedClasses, judge)).filter(([, passed]) => !passed), [],
  'class order, extra classes and expression whitespace are not semantic changes')

const transparentAccount = account
  .replace('<p v-if="notice" class="notice" role="status" aria-live="polite">{{ notice }}</p>',
    '<template>\n        <p v-if="notice" class="notice" role="status" aria-live="polite">{{ notice }}</p>\n      </template>')
assert.deepEqual(Object.entries(contract(transparentAccount, judge)).filter(([, passed]) => !passed), [],
  'unconditional template wrappers are render-transparent')

const transparentComponents = replaceOnce(replaceOnce(account, judgeTag, `<template>${judgeTag}</template>`),
  managementTag, `<template>${managementTag}</template>`)
assert.deepEqual(Object.entries(contract(transparentComponents, judge)).filter(([, passed]) => !passed), [],
  'unconditional component template wrappers are render-transparent')

const kebabComponents = replaceOnce(replaceOnce(account,
  '<TournamentJudgeDesk ', '<tournament-judge-desk '),
  '<TournamentManagementPanel ', '<tournament-management-panel ')
assert.deepEqual(Object.entries(contract(kebabComponents, judge)).filter(([, passed]) => !passed), [],
  'the two explicit Vue kebab-case component spellings are equivalent')

expectFalse('accountLoadingCondition', replaceOnce(account,
  '<p v-if="loading" class="state-panel" role="status">正在加载赛事…</p>',
  '<div v-if="loading" class="state-panel" role="status">正在加载赛事…</div>'))
expectFalse('accountFailureConditionAndOrder', account.replace(
  '</p><p v-else-if="!tournament"', '</p><span>插入节点</span><p v-else-if="!tournament"'))
expectFalse('accountFailureConditionAndOrder', account.replace('v-else-if="!tournament"', 'v-else-if="!tournament || notice"'))
expectFalse('accountNoticeStatus', account.replace(
  'class="notice" role="status" aria-live="polite"', 'class="notice" role="status" :role="dynamicRole" aria-live="polite"'))
expectFalse('accountNoticeText', replaceOnce(account,
  '<p v-if="notice" class="notice" role="status" aria-live="polite">{{ notice }}</p>',
  '<p v-if="notice" class="notice" role="status" aria-live="polite">{{ notice }} 处理完成</p>'))
expectFalse('accountNoticeCondition', account.replace('v-if="notice" class="notice"', 'v-if="notice && tournament" class="notice"'))
expectFalse('accountNoticeCondition', account.replace('v-if="notice" class="notice"', 'v-if="notice" v-show="false" class="notice"'))
expectFalse('accountNoticeCondition', account.replace('<template v-else>', '<template v-else-if="tournament">'))
expectFalse('accountNoticeText', account.replace(
  'v-if="notice" class="notice"', 'v-if="notice" v-text="notice" class="notice"'))
expectFalse('scheduleEmpty', account.replace('赛程尚未生成', '赛程暂未生成'))
expectFalse('standingsEmpty', account.replace(
  'v-if="!(tournament.finalSwissStandings.length || rounds.at(-1)?.standings?.length)"',
  'v-if="!tournament.finalSwissStandings.length"'))
expectFalse('passiveEmptyStates', account.replace(
  'v-if="!rounds.length" class="empty-state"', 'v-if="!rounds.length" class="empty-state" role="status"'))
expectFalse('judgePersonalMatchCondition', account, replaceOnce(
  replaceOnce(judge, '<form v-if="myMatches.length"', '<section v-if="myMatches.length"'),
  '</button></form>', '</button></section>'))
expectFalse('judgePersonalEmptyElse', account, judge.replace('<p v-else class="empty">', '<p v-if="!myMatches.length" class="empty">'))
expectFalse('judgeVisibleCasesEmpty', account, judge.replace('v-if="!tournament.judgeCases.length"', 'v-if="!tournament.judgeCases.length || busy"'))
expectFalse('accountJudgeComponent', account.replace('<TournamentJudgeDesk', '<div'), judge)
expectFalse('accountJudgeComponent', account.replace('<TournamentJudgeDesk ', '<TournamentJudgeDesk v-if="false" '), judge)
expectFalse('accountManagementComponent', account.replace('<TournamentManagementPanel ', '<TournamentManagementPanel v-show="false" '), judge)
expectFalse('accountComponentsShareNotice', replaceOnce(account,
  '@notice="value => notice = value"', '@notice="value => notice = value + \'!\'"'), judge)
expectFalse('accountComponentsShareNotice', account.replace(
  '<TournamentManagementPanel :tournament="tournament"',
  '<TournamentManagementPanel v-on="forwardedEvents" :tournament="tournament"'), judge)

const fakeAccount = `<template><main class="detail-page"><!--
  <p v-if="loading" role="status">正在加载赛事…</p>
  <TournamentManagementPanel @notice="value => notice = value" />
--><script>const fake = '<TournamentJudgeDesk @notice="value => notice = value" />'</script></main></template>`
const fakeJudge = `<template><section class="judge-desk"><!--
  <form v-if="myMatches.length" class="call"></form><p v-else class="empty"><b>当前没有可呼叫的桌次</b></p>
--><script>const fake = '<div v-if="!tournament.judgeCases.length">暂无可见案件</div>'</script></section></template>`
assert.deepEqual(Object.entries(contract(fakeAccount, fakeJudge)).filter(([, passed]) => passed), [],
  'comments and script text cannot lend fake rendered nodes')

console.log(`A3 tournament notice template contracts passed: ${tournamentNoticePredicateNames.length} predicates, 5 groups`)
