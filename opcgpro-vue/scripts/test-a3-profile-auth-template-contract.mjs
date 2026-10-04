import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { hasProfileAuthTemplate, hasProfileStatusNoticeBeforeRank } from './profile-auth-template-contract.mjs'

const guard = 'authBusy || !auth.username.trim() || !auth.password'
const fixture = '<template><section><form class="account-form auth-form" @submit.prevent="submitAuth">'
  + '<label>用户名<input v-model="auth.username" required/></label>'
  + '<label>密码<input v-model="auth.password" type="password" required/></label>'
  + '<UiButton class="primary" type="submit" :busy="authBusy" :disabled="' + guard + '">提交</UiButton>'
  + '<UiNotice class="auth-notice" v-if="authNotice" kind="error" role="alert" aria-live="assertive" aria-atomic="true">{{ authNotice }}</UiNotice>'
  + '</form></section></template>'
let checks = 0
function check(source, expected, label) {
  assert.equal(hasProfileAuthTemplate(source), expected, label)
  checks++
}
check(fixture, true, 'canonical same-form auth structure')
check(readFileSync(new URL('../src/l12/site/ProfilePage.vue',import.meta.url),'utf8'), true, 'actual unchanged ProfilePage')
check(fixture.replace('account-form auth-form','auth-form extra account-form')
  .replace('@submit.prevent="submitAuth"','@submit.stop.prevent="( submitAuth )"')
  .replaceAll('auth.username','auth . username').replaceAll('auth.password','auth . password')
  .replace(':busy="authBusy"',':busy="( authBusy )"')
  .replace('v-if="authNotice"','v-if="( authNotice )"'), true, 'equivalent class order and expression whitespace')
check(fixture.replace(guard,'(!auth.password) || (authBusy || ! (auth.username.trim()))'), true, 'equivalent OR order and parentheses')
check(fixture.replace('</form>','<UiButton>取消</UiButton></form>'), true, 'UiButton default type is not submit')
check(fixture.replace('</form>','<button :disabled="' + guard + '">额外提交</button></form>'), true, 'extra implicit native submit is fully locked')
check(fixture.replace('<form ','<div ').replace('</form>','</div>'), false, 'wrong form node')
check(fixture.replace('account-form auth-form','auth-form-old account-form'), false, 'wrong form class')
check(fixture.replace('submitAuth','otherSubmit'), false, 'wrong submit handler')
check(fixture.replace('@submit.prevent','@submit'), false, 'missing prevent modifier')
check(fixture.replace('@submit.prevent="submitAuth"','@submit.prevent="\'submitAuth\'"'), false, 'literal string is not submit handler')
check(fixture.replace('v-model="auth.username"','v-model="other.username"'), false, 'wrong username model')
check(fixture.replace('v-model="auth.password"','v-model="auth.username"'), false, 'two required inputs cannot use the same model')
check(fixture.replace('v-model="auth.password"','v-model="\'auth.password\'"'), false, 'model text literal is not auth state')
check(fixture.replace('auth.username" required','auth.username"'), false, 'username must itself be required')
check(fixture.replace('type="password" required','type="password"').replace('<label>密码','<label required>密码'), false, 'required on another node cannot protect password')
check(fixture.replace('type="password"','type="text"'), false, 'password input remains a password')
check(fixture.replace('</form>','<input required/></form>'), false, 'required count must correspond to the two credentials')
check(fixture.replace(':disabled="' + guard + '"','disabled="' + guard + '"'), false, 'static disabled text is not a binding')
check(fixture.replace(guard,"'" + guard + "'"), false, 'disabled binding literal cannot impersonate the OR lock')
check(fixture.replace(guard,'authBusy || !auth.username.trim()'), false, 'missing empty-password lock')
check(fixture.replace(guard,'authBusy || !auth.password'), false, 'missing trimmed-username lock')
check(fixture.replace(guard,'!auth.username.trim() || !auth.password'), false, 'missing busy lock')
check(fixture.replace(guard,'authBusy && !auth.username.trim() && !auth.password'), false, 'AND terms do not form the OR lock')
check(fixture.replace(':busy="authBusy"',':busy="otherBusy"'), false, 'submit busy must bind real authBusy')
check(fixture.replace('type="submit"','type="button"'), false, 'actual UiButton must submit the form')
check(fixture.replace('v-if="authNotice"','v-if="otherNotice"'), false, 'notice condition must bind authNotice')
check(fixture.replace('{{ authNotice }}',"{{ 'authNotice' }}"), false, 'notice must render real authNotice')
check(fixture.replace('aria-atomic="true"',''), false, 'atomic announcement is retained')
check(fixture.replace('aria-live="assertive"','aria-live="polite"'), false, 'error notice remains assertive')
check(fixture.replace('aria-live="assertive"','').replace('</form>','<span aria-live="assertive"/></form>'), false, 'alert and live cannot be assembled from different nodes')
check(fixture.replace('<UiNotice class="auth-notice"','<UiNotice class="other-notice"').replace('</form>','<span class="auth-notice" v-if="authNotice" role="alert" aria-live="assertive"/> </form>'), false, 'wrong notice node cannot borrow UiNotice semantics')
const form = fixture.slice(fixture.indexOf('<form '),fixture.indexOf('</form>')+7)
check('<template><section>' + form + form + '</section></template>', false, 'duplicate auth forms')
check('<template><section><!-- ' + form + ' --></section></template>', false, 'comment form is not real')
check('<script>const decoy = ' + JSON.stringify(form) + ';</script><template><section/></template>', false, 'script form is not real')
check(fixture.replace('</form>','<form><input v-model="auth.username" required/></form></form>'), false, 'nested form cannot contribute fields')
const password = '<label>密码<input v-model="auth.password" type="password" required/></label>'
check(fixture.replace(password,'').replace('</section>','<form>' + password + '</form></section>'), false, 'password from a sibling form cannot contribute')
const notice = '<UiNotice class="auth-notice" v-if="authNotice" kind="error" role="alert" aria-live="assertive" aria-atomic="true">{{ authNotice }}</UiNotice>'
check(fixture.replace(notice,'').replace('</section>','<form>' + notice + '</form></section>'), false, 'notice must belong to the same form')
check(fixture.replace('</form>','<button>未锁默认提交</button></form>'), false, 'implicit native submit must not bypass locks')
check(fixture.replace('</form>','<input type="submit"/></form>'), false, 'input submit must not bypass locks')
check(fixture.replace('</form>','<UiButton :type="buttonType">未知提交入口</UiButton></form>'), false, 'bound UiButton type is unknown')
check(fixture.replace('</form>','<button :type="buttonType">未知提交入口</button></form>'), false, 'bound native type is unknown')
check(fixture.replace('<form ','<form id="profile-auth" ').replace('</section>','<button type="submit" form="profile-auth">外部提交</button></section>'), false, 'associated external submit must not bypass locks')
check(fixture.replace('v-model="auth.username" required','v-model="auth.username" required form="another-form"'), false, 'credential must not override form ownership')
check(fixture.replace('v-model="auth.username" required','v-model="auth.username" required :required="false"'), false, 'dynamic required override cannot borrow static required')
check(fixture.replace('v-model="auth.username" required','v-model="auth.username" required v-bind="credentialProps"'), false, 'unknown credential props cannot override required or ownership')
check(fixture.replace('auth.username" required','auth?.username" required'), false, 'optional chain is not an assignable model')
check(fixture.replace('role="alert"','role="alert" :role="\'status\'"'), false, 'dynamic role cannot borrow a static alert')
check(fixture.replace('</form>','<UiButton v-bind="extraProps">未知入口</UiButton></form>'), false, 'unknown props may turn a default UiButton into a submit')
check(fixture.replace('<form ','<form v-on="handlers" '), false, 'unknown event map cannot impersonate submit ownership')
check(fixture.replace('class="auth-notice"','class="extra auth-notice legacy"'), true, 'notice class additions and order remain equivalent')
console.log('A3 Profile auth template contract: ' + checks + '/' + checks)

const statusNotice = '<UiNotice v-if="notice" class="notice" role="status" aria-live="polite" aria-atomic="true">{{ notice }}</UiNotice>'
const rankOverview = '<section v-if="ranked" class="rank-overview"/>'
const statusFixture = '<template><div class="profile-page ui-state-scope">' + statusNotice
  + '<div class="ranked-season-panel">' + rankOverview + '</div></div></template>'
let statusChecks = 0
function checkStatus(source, expected, label) {
  assert.equal(hasProfileStatusNoticeBeforeRank(source), expected, label)
  statusChecks++
}
checkStatus(statusFixture, true, 'canonical same-page notice before nested rank')
checkStatus(readFileSync(new URL('../src/l12/site/ProfilePage.vue',import.meta.url),'utf8'), true, 'actual unchanged ProfilePage status notice')
checkStatus(statusFixture.replace('profile-page ui-state-scope','ui-state-scope extra profile-page')
  .replace('class="notice"','class="extra notice"').replace('class="rank-overview"','class="extra rank-overview"')
  .replace('v-if="notice"','v-if="( notice )"').replace('{{ notice }}','{{ ( notice ) }}')
  .replace('role="status" aria-live="polite"','aria-live="polite"\n role="status"'), true, 'class order attribute order and expression whitespace')
checkStatus(statusFixture.replace(statusNotice,'<template>' + statusNotice + '</template>')
  .replace(rankOverview,'<template>' + rankOverview + '</template>'), true, 'transparent templates preserve ownership and order')
checkStatus(statusFixture.replace('{{ notice }}','<span>{{ notice }}</span>'), true, 'real unconditional text wrapper')
checkStatus(statusFixture.replace(statusNotice,'').replace(rankOverview,rankOverview + statusNotice), false, 'notice after rank')
checkStatus(statusFixture.replace(statusNotice,'').replace(rankOverview,'<section class="rank-overview">' + statusNotice + '</section>'), false, 'notice hidden inside rank')
checkStatus(statusFixture.replace(statusNotice,'<form>' + statusNotice + '</form>'), false, 'notice cannot be borrowed from another form')
checkStatus(statusFixture.replace(rankOverview,'<form>' + rankOverview + '</form>'), false, 'rank cannot be borrowed from a form')
checkStatus(statusFixture.replace(statusNotice,''), false, 'missing notice')
checkStatus(statusFixture.replace(rankOverview,''), false, 'missing rank')
checkStatus(statusFixture.replace(statusNotice,statusNotice + statusNotice), false, 'duplicate notice')
checkStatus(statusFixture.replace(rankOverview,rankOverview + rankOverview), false, 'duplicate rank')
checkStatus(statusFixture.replace('class="profile-page ui-state-scope"','class="other-page"'), false, 'missing actual page host')
checkStatus('<template><div class="profile-page">' + statusNotice + '</div><div>' + rankOverview + '</div></template>', false, 'different host cannot lend rank')
checkStatus('<template><div class="profile-page">' + statusNotice + '</div><div class="profile-page">' + rankOverview + '</div></template>', false, 'duplicate page hosts')
checkStatus('<template><!-- ' + statusFixture + ' --></template>', false, 'comment structure is not rendered')
checkStatus('<script>const decoy = ' + JSON.stringify(statusFixture) + ';</script><template><div/></template>', false, 'script structure is not rendered')
checkStatus('<style>/* ' + statusFixture + ' */</style><template><div/></template>', false, 'style structure is not rendered')
checkStatus(statusFixture.replace('<UiNotice ','<span ').replace('</UiNotice>','</span>'), false, 'wrong notice node')
checkStatus(statusFixture.replace('class="notice"','class="notice rank-overview"').replace(rankOverview,''), false, 'same node cannot lend notice and rank')
checkStatus(statusFixture.replace('v-if="notice"',''), false, 'missing notice condition')
checkStatus(statusFixture.replace('v-if="notice"','v-if="otherNotice"'), false, 'wrong notice condition')
checkStatus(statusFixture.replace('v-if="notice"','v-if="\'notice\'"'), false, 'literal condition is not notice state')
checkStatus(statusFixture.replace('{{ notice }}',"{{ 'notice' }}"), false, 'literal text is not notice state')
checkStatus(statusFixture.replace('{{ notice }}','').replace(rankOverview,'<section class="rank-overview">{{ notice }}</section>'), false, 'different node cannot lend notice text')
checkStatus(statusFixture.replace('role="status"','role="alert"'), false, 'wrong status role')
checkStatus(statusFixture.replace('aria-live="polite"','aria-live="assertive"'), false, 'wrong live politeness')
checkStatus(statusFixture.replace('aria-atomic="true"',''), false, 'atomic announcement retained')
for (const name of ['role','aria-live','aria-atomic']) {
  checkStatus(statusFixture.replace('<UiNotice ','<UiNotice :' + name + '="override" '), false, 'dynamic ' + name + ' cannot override static status semantics')
}
for (const token of ['<UiNotice ','<section ','<div ']) {
  checkStatus(statusFixture.replace(token,token + 'v-bind="unknownProps" '), false, 'unknown props on contracted node')
}
checkStatus(statusFixture.replace('<div class="profile-page ui-state-scope">','<section class="profile-page ui-state-scope">').replace('</div></template>','</section></template>'), false, 'wrong page host node')
checkStatus(statusFixture.replace(rankOverview,'<div class="rank-overview"/>'), false, 'wrong rank node')
checkStatus(statusFixture.replace('class="notice"','class="notice" :class="unknownClass"'), false, 'unknown contracted class binding')
checkStatus(statusFixture.replace(statusNotice,'<template v-if="ranked">' + statusNotice + '</template>'), false, 'conditional template cannot hide global status')
checkStatus(statusFixture.replace(statusNotice,'<div>' + statusNotice + '</div>'), false, 'nontransparent notice ownership')
checkStatus(statusFixture.replace(rankOverview,'<Teleport to="body">' + rankOverview + '</Teleport>'), false, 'teleported rank cannot lend page order')
checkStatus(statusFixture.replace('{{ notice }}','<template #action>{{ notice }}</template>'), false, 'slot cannot lend visible notice text')
checkStatus(statusFixture.replace('v-if="notice"','v-if="notice" v-show="false"'), false, 'show override cannot hide global status')
checkStatus(statusFixture.replace('role="status"','role="status" role="alert"'), false, 'duplicate role cannot borrow first attribute')
console.log('A3 Profile status notice contract: ' + statusChecks + '/' + statusChecks)
