import fs from 'node:fs'
import path from 'node:path'
import ts from 'typescript'
import { parse as parseSfc } from '@vue/compiler-sfc'
import { baseParse, parserOptions } from '@vue/compiler-dom'
import { tournamentNoticeGroups, tournamentNoticeTemplateContract } from './tournament-notice-template-contract.mjs'

const root = process.cwd()
const read = file => fs.readFileSync(path.join(root, file), 'utf8')
const detail = read('src/l12/site/TournamentAccountDetail.vue')
const publicDetail = read('src/l12/site/TournamentPublicDetail.vue')
const management = read('src/l12/site/TournamentManagementPanel.vue')
const judge = read('src/l12/site/TournamentJudgeDesk.vue')
const summary = read('src/l12/site/TournamentSummaryList.vue')
const wizard = read('src/l12/site/TournamentCreateWizard.vue')
const hub = read('src/l12/site/TournamentHubPage.vue')
const platform = read('src/l12/platform.ts')
const router = read('src/router/index.ts')
const admin = read('src/l12/site/AdminTournamentWorkbench.vue')
const time = read('src/l12/tournamentTime.ts')
const createPolicy = read('src/l12/tournamentCreatePolicy.ts')
await import('./test-tournament-create-flow.mjs')
const gamePage = read('src/l12/GamePage.vue')
const roomLoadingState = read('src/l12/tournamentRoomLoadingState.ts')
const tournamentServer = read('../服务端WebSocket/TwelveLegions/L12PlatformStore.Tournaments.cs')
const visualSources = [detail, publicDetail, management, judge, summary, wizard, hub]
function publicLoginStaysReachable(source) {
  const template = parseSfc(source).descriptor.template?.content
  if (!template) return false
  let found = false
  const visit = (node, headerActions = false) => {
    const classes = node.props?.find(prop => prop.type === 6 && prop.name === 'class')?.value?.content.split(/\s+/) ?? []
    const inHeader = headerActions || classes.includes('header-actions')
    if (node.type === 1 && node.tag === 'button' && inHeader
        && node.props.some(prop => prop.type === 7 && prop.name === 'on'
          && prop.arg?.content === 'click' && prop.exp?.content === 'login')) {
      found = !node.props.some(prop => prop.type === 6 && prop.name === 'disabled'
        || prop.type === 7 && prop.name === 'bind' && prop.arg?.content === 'disabled')
    }
    for (const child of node.children ?? []) visit(child, inHeader)
  }
  visit(baseParse(template, parserOptions))
  return found
}
const disabledPublicLogin = publicDetail.replace(/(<div class="header-actions">[\s\S]*?<button class="primary") @click="login"/,
  '$1 :disabled="!tournament.registrationOpen" @click="login"')
if (disabledPublicLogin === publicDetail || publicLoginStaysReachable(disabledPublicLogin))
  throw new Error('Closed-registration login guard must reject a disabled login path')
function hasBoundCreateFlow(source, functionName) {
  const setup = parseSfc(source).descriptor.scriptSetup
  if (!setup) return false
  const tree = ts.createSourceFile('create-flow.ts', setup.content, ts.ScriptTarget.Latest, true)
  const binding = tree.statements.filter(ts.isImportDeclaration)
    .filter(node => node.moduleSpecifier.text === '@/l12/tournamentCreateFlow')
    .flatMap(node => node.importClause?.namedBindings?.elements ?? [])
    .find(node => (node.propertyName ?? node.name).text === 'createTournamentWithCurrentContext')?.name.text
  const fn = tree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === functionName)
  if (!binding || !fn) return false
  let found = false
  function visit(node) { if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === binding) found = true; ts.forEachChild(node, visit) }
  visit(fn)
  return found
}
function hasDangerRemoveAction(source) {
  const parsed = parseSfc(source)
  if (parsed.errors.length || !parsed.descriptor.template) return false
  const ast = baseParse(parsed.descriptor.template.content, parserOptions)
  let found = false
  function visit(node) {
    if (node.type === 1 && node.tag === 'button') {
      const classes = node.props.find(prop => prop.type === 6 && prop.name === 'class')?.value?.content.split(/\s+/) ?? []
      const click = node.props.find(prop => prop.type === 7 && prop.name === 'on'
        && prop.arg?.isStatic && prop.arg.content === 'click')?.exp?.content.replace(/\s+/g, '')
      if (classes.includes('danger') && click === 'remove(person,true)') found = true
    }
    for (const child of node.children ?? []) visit(child)
  }
  visit(ast)
  return found
}
const dangerRemoveButton = detail.match(/<button\b[^>]*@click="remove\(person,true\)"[^>]*>[\s\S]*?<\/button>/)?.[0]
if (!dangerRemoveButton) throw new Error('Actual dangerous removal button is required')
for (const changedButton of [
  dangerRemoveButton.replace('class="danger"', 'class="normal"'),
  dangerRemoveButton.replace('@click="remove(person,true)"', '@click="remove(person,false)"'),
  dangerRemoveButton.replace('@click="remove(person,true)"', '@mouseover="remove(person,true)"'),
  dangerRemoveButton.replace('<button', '<div').replace('</button>', '</div>'),
]) {
  if (changedButton === dangerRemoveButton || hasDangerRemoveAction(detail.replace(dangerRemoveButton, changedButton)))
    throw new Error('Dangerous-action semantic guard must reject missing danger/button/click/ban identity')
}
if (!hasDangerRemoveAction(detail.replace(dangerRemoveButton, dangerRemoveButton.replace('class="danger"', 'class="extra danger"'))))
  throw new Error('Dangerous-action guard must allow additional visual classes')
const standingComparator = tournamentServer.slice(tournamentServer.indexOf('private static int CompareStanding('))
const appearsInOrder = (source, markers) => {
  let position = 0
  return markers.every(marker => {
    const found = source.indexOf(marker, position)
    if (found < 0) return false
    position = found + marker.length
    return true
  })
}

const loadTypeScriptModule = async source => {
  const transpiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  }).outputText
  return import(`data:text/javascript;base64,${Buffer.from(transpiled).toString('base64')}`)
}
const createPolicyModule = await loadTypeScriptModule(createPolicy)
const roomLoadingModule = await loadTypeScriptModule(roomLoadingState)
const noticeTemplateContract = tournamentNoticeTemplateContract(detail, judge)
const noticeTemplateChecks = tournamentNoticeGroups(noticeTemplateContract)
  .map(([name, predicates]) => [`tournament notice: ${name}`, predicates.every(Boolean)])
const throwsWith = (work, expected) => {
  try { work(); return false } catch (error) { return String(error?.message || error).includes(expected) }
}
const policyCards = [...Array.from({ length: 8 }, (_, index) => `S01-DS0${index + 1}`), 'S01-DS10']
const formatBase = { format: 'swiss-cut', maxPlayers: 16, swissRounds: 4, cutSize: 8 }
const roomBase = {
  status: 'online', recoveryPhase: 'snapshot-acknowledged', connectionIssue: 'none', notice: '',
  room: { tournamentId: 't-1', tournamentMatchId: 'm-1', started: false, yourPlayerIndex: 0,
    players: [{ name: '甲', playerIndex: 0, connected: true }, { name: '乙', playerIndex: 1, connected: false }] },
}

function ownedMetadataCheckIn(source) {
  const script = source.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)?.[1]
  if (!script) return false
  const file = ts.createSourceFile('owned-checkin.ts', script, ts.ScriptTarget.Latest, true)
  if (file.parseDiagnostics.length) return false
  const fn = file.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'preCheckIn')
  if (!fn?.body) return false
  const declaration = fn.body.statements.filter(ts.isVariableStatement).flatMap(node => node.declarationList.declarations)
    .find(node => node.name.getText(file) === 'deck')
  if (declaration?.initializer?.getText(file) !== 'selectedDeck.value') return false
  const guard = fn.body.statements.findIndex(node => ts.isIfStatement(node)
    && node.expression.getText(file) === '!deck?.id' && node.thenStatement.getText(file).includes('return'))
  const calls = []
  function visit(node) {
    if (ts.isCallExpression(node) && node.expression.getText(file) === 'tournamentApi.preCheckIn') calls.push(node)
    ts.forEachChild(node, visit)
  }
  visit(fn.body)
  return guard >= 0 && calls.length === 1 && calls[0].arguments.length === 5
    && calls[0].arguments.map(node => node.getText(file)).join('|') === "item.id|item.version|deck.name|''|deck.id"
    && calls[0].pos > fn.body.statements[guard].end
    && source.includes("import SavedDeckSelector from '@/l12/SavedDeckSelector.vue'")
}
for (const [before, after] of [["deck.name, '', deck.id", "deck.name, '', deck.name"],
  ['if (!deck?.id)', 'if (false)'], ['const deck = selectedDeck.value', 'const deck = savedDecks.value[0]']]) {
  const changed = detail.replace(before, after)
  if (changed === detail || ownedMetadataCheckIn(changed)) throw new Error(`Owned check-in guard must reject ${after}`)
}

const checks = [
  ['player tournament pages use the shared site visual tokens', visualSources.every(source => source.includes('--l12-ui-')) && visualSources.every(source => !/var\(--(?:border|panel|accent|muted|danger)\)/.test(source))],
  ['player tournament pages share the 700px compact boundary', visualSources.every(source => source.includes('@media(max-width:700px)')) && visualSources.every(source => !/@media\(max-width:(?:650|680|720|760|850)px\)/.test(source))],
  ['mobile tournament actions retain 44px targets', [detail, management, judge, wizard, hub].every(source => source.includes('min-height:44px'))],
  ['tournament states and roles retain text labels', summary.includes('tournamentStatusText(item.status)') && summary.includes('tournamentViewerRoleText(item.viewerRole)') && detail.includes('tournamentPhaseText(tournament.phase)') && detail.includes('tournamentRoundStatusText(round.status)')],
  ['dangerous organizer actions are visually separated', management.includes('class="danger-zone"') && management.includes('class="danger" @click="cancel"') && hasDangerRemoveAction(detail)],
  ['responsive standings become labeled cards', detail.includes('class="standings-table"') && detail.includes('data-label="胜-负-平"') && detail.includes('.standings-table .thead{display:none}')],
  ['Swiss ranking explanation matches the authoritative comparator without exposing account IDs',
    detail.match(/class="ranking-method"/g)?.length === 2
      && detail.match(/v-if="hasSwissStandings"/g)?.length === 2
      && detail.includes("tournament.value?.format === 'swiss' || tournament.value?.format === 'swiss-cut'")
      && detail.includes('轮空计 1 胜')
      && detail.includes('轮空不产生对手')
      && detail.includes("name: '对手胜场和', explanation: '实际交手过的每位对手，其胜场相加；分数较高者在前。轮空不产生对手。'")
      && detail.includes("name: '对手的对手胜场和', explanation: '把每位实际交手对手的“对手胜场和”相加；分数较高者在前。'")
      && detail.includes("name: '最终同分顺序', explanation: '以上条件仍相同时，系统按固定顺序确定先后；相同赛果不会随机换位。'")
      && !detail.includes('账号 ID')
      && tournamentServer.includes('if (match.Result == "bye") { wins[match.PlayerAAccountId]++; byes[match.PlayerAAccountId]++; }')
      && appearsInOrder(detail, ["name: '胜场'", "name: '对手胜场和'", "name: '对手的对手胜场和'", "name: '最近直接交锋'", "name: '种子'", "name: '最终同分顺序'"])
      && appearsInOrder(standingComparator, ['second.Wins.CompareTo(first.Wins)', 'second.OpponentScore.CompareTo(first.OpponentScore)', 'second.OpponentsOpponentScore.CompareTo(first.OpponentsOpponentScore)', 'matches.LastOrDefault', 'first.Seed.CompareTo(second.Seed)', 'string.CompareOrdinal(first.AccountId, second.AccountId)'])],
  ['creation progress and tournament timing stay visibly structured', wizard.includes('class="stepper"') && wizard.includes('TOURNAMENT TIMING') && wizard.includes('轮次与对局计时') && wizard.includes('报名时无需提交，赛前签到时锁定')],
  ['public detail exposes event facts and an explicit registration path before sign-in', publicDetail.includes('class="panel registration-callout"') && publicDetail.includes('登录并报名') && publicDetail.includes('登录后可直接报名') && publicDetail.includes('赛前签到时选择并锁定本人牌库')],
  ['closed registration keeps sign-in available for already registered participants', publicLoginStaysReachable(publicDetail) && publicDetail.includes("'登录查看我的赛事'")],
  ['automatic tournament creation is admin-only with separate 3/5 minute defaults', wizard.includes("hasPermission('tournaments.manage')") && wizard.includes('v-if="canCreateAutomated"') && wizard.includes("operationMode: 'managed', intermissionMinutes: 3") && wizard.includes('lateGraceMinutes: 5') && wizard.includes('轮间准备、每轮迟到和对局内断线分别计时，互不替代') && createPolicy.includes("operationMode: normalized.operationMode === 'automated' ? 'automated' : 'managed'") && createPolicy.includes('boundedInteger(normalized.intermissionMinutes, 3, 0, 60)') && createPolicy.includes('boundedInteger(normalized.lateGraceMinutes, 5, 0, 60)')],
  ['detail separates organizer and judge capabilities', detail.includes('const canOrganize') && detail.includes('const canJudge') && !detail.includes('const canManage')],
  ['participant moderation is organizer-only', detail.includes('v-if="canOrganize && person.accountId !== tournament.organizerAccountId"')],
  ['management is an extracted component', noticeTemplateContract.accountManagementComponent && management.includes('赛事生命周期')],
  ...noticeTemplateChecks,
  ['referee cannot open organizer management', detail.includes('const canViewManage = computed(() => canOrganize.value')],
  ['judge calls only list the current player matches', judge.includes('const myMatches = computed') && judge.includes('match.playerAAccountId === accountId.value')],
  ['appeal is limited to table players', judge.includes('const canAppeal =') && judge.includes('v-if="canAppeal(item)"')],
  ['conflicted staff are excluded from assignment choices', judge.includes("person.accountId !== match?.playerAAccountId")],
  ['player-facing enums use shared Chinese labels', detail.includes("@/l12/tournamentLabels") && judge.includes("@/l12/tournamentLabels") && summary.includes('tournamentViewerRoleText')],
  ['check-in range matches server authority', wizard.includes('v-model.number="form.checkInMinutes" type="number" min="1" max="60"')],
  ['admin tournament workbench has an explicit admin-only name', router.includes("import('@/l12/site/AdminTournamentWorkbench.vue')") && !router.includes('TournamentCenterPage')],
  ['player route uses hub and stable detail routes', router.includes("component: () => import('@/l12/site/TournamentHubPage.vue')") && router.includes("path: '/battle/tournaments/:code'")],
  ['pre-check-in reuses the saved-deck selector and binds an owned deck ID rather than a reusable name', ownedMetadataCheckIn(detail) && detail.includes('<SavedDeckSelector') && detail.includes('mode="tournament"') && detail.includes('confirmDeckSelection') && !detail.includes('<select v-model="deckId"') && admin.includes('v-model="deckDrafts[detail.id].id"') && admin.includes("preCheckIn(item.id, item.version, selected.name, '', selected.id)") && platform.includes('expectedVersion, deckName, deckCode, deckId')],
  ['critical tournament actions use inline reasons', !detail.includes('prompt(') && !judge.includes('prompt(') && !management.includes('confirm(') && detail.includes('participantReasons[person.accountId]') && judge.includes('appealReasons[item.id]')],
  ['organizer transfer uses eligible named candidates', management.includes('transferCandidates') && management.includes('选择本场裁判或主办者好友')],
  ['career history is visible and server-paged', hub.includes('个人赛事履历') && hub.includes('career.totalPages') && platform.includes("params.set('pageSize', String(query.pageSize))")],
  ['tournament visible timing uses minutes while authority stays in seconds', [detail, wizard, admin].every(source => !source.includes('（秒）') && !source.includes('操作秒数') && !source.includes('重连秒数') && !source.includes('选择秒数') && !source.includes('调度秒数') && !source.includes(' }} 秒')) && wizard.includes('timeControlMinutes') && admin.includes('timeControlMinutes') && time.includes('tournamentTimeControlSeconds') && time.includes('tournamentMinutesLabel')],
  ['format switches remove mutually exclusive cut fields', createPolicy.includes("input.format === 'single'") && createPolicy.includes('swissRounds: 0, cutSize: undefined') && createPolicy.includes("input.format === 'swiss-cut'") && createPolicy.includes('cutSize: undefined')],
  ['single elimination hides the mutually exclusive swiss-round input', wizard.includes(`<label v-if="form.format !== 'single'">瑞士轮数`) && admin.includes(`<label v-if="form.format!=='single'">瑞士轮数`)],
  ['templates manual changes and restored drafts share format normalization', wizard.includes('normalizeTournamentFormatInPlace(form)') && wizard.includes('watch(() => form.format') && wizard.indexOf('Object.assign(form, saved)') < wizard.lastIndexOf('normalizeTournamentFormatInPlace(form)')],
  ['player and admin creation bind the current-context flow; real script setup locks frozen input and preview version', hasBoundCreateFlow(wizard, 'create') && hasBoundCreateFlow(admin, 'createTournament')],
  ['player creation reuses shared disaster and construction-rule components', wizard.includes("import DisasterPoolPicker from './DisasterPoolPicker.vue'") && wizard.includes("import ConstructionRuleEditor from './ConstructionRuleEditor.vue'") && wizard.includes('<DisasterPoolPicker v-model="form.disasterCardIds"') && wizard.includes('<ConstructionRuleEditor v-model="form.cardRestrictions"')],
  ['disaster modes freeze the organizer selection and none stays empty', createPolicy.includes("if (mode === 'none') return []") && createPolicy.includes('source.disasterCardIds') && createPolicy.includes('tournamentDisasterSnapshot(normalized.disasterMode, normalized)') && createPolicy.includes('cardIds.length < 9 || cardIds.length > 64') && createPolicy.includes('TOURNAMENT_ANNIHILATION_CARD_ID') && wizard.includes('getEffectiveOperationsPolicy()')],
  ['empty or invalid tournament disaster selection fails readably', createPolicy.includes('本场尚未选择天灾卡') && createPolicy.includes('本场天灾池必须为 9–64 张') && wizard.includes('policyError')],
  ['server keeps strict disaster pool count and annihilation-last authority', tournamentServer.includes('normalizedDisasters.Length is < 9 or > 64') && tournamentServer.includes('normalizedDisasters[^1], L12ActiveDisasterRules.AnnihilationCardId') && tournamentServer.includes('天灾池须为 9–64 张（含湮灭），且湮灭固定在最后一张')],
  ['format normalization behavior removes stale cut values', createPolicyModule.normalizeTournamentFormatFields({ ...formatBase, format: 'single' }).cutSize === undefined && createPolicyModule.normalizeTournamentFormatFields({ ...formatBase, format: 'single' }).swissRounds === 0 && createPolicyModule.normalizeTournamentFormatFields({ ...formatBase, format: 'swiss' }).cutSize === undefined],
  ['disaster snapshot behavior validates selection and clones authority', createPolicyModule.tournamentDisasterSnapshot('none', { disasterCardIds: [] }).length === 0 && createPolicyModule.tournamentDisasterSnapshot('season', { disasterCardIds: policyCards }).join(',') === policyCards.join(',') && createPolicyModule.tournamentDisasterSnapshot('season', { disasterCardIds: policyCards }) !== policyCards],
  ['empty short duplicate and misplaced-annihilation selections fail closed', throwsWith(() => createPolicyModule.tournamentDisasterSnapshot('season', { disasterCardIds: [] }), '尚未选择天灾卡') && throwsWith(() => createPolicyModule.tournamentDisasterSnapshot('all', { disasterCardIds: ['S01-DS10'] }), '9–64') && throwsWith(() => createPolicyModule.tournamentDisasterSnapshot('random', { disasterCardIds: [...policyCards.slice(0, -1), policyCards[0], 'S01-DS10'] }), '重复卡牌') && throwsWith(() => createPolicyModule.tournamentDisasterSnapshot('season', { disasterCardIds: ['S01-DS10', ...policyCards.slice(0, -1)] }), '最后一张')],
  ['tournament room distinguishes waiting loading and true failure', roomLoadingState.includes("kind: 'waiting'") && roomLoadingState.includes("kind: 'loading'") && roomLoadingState.includes("kind: 'failed'") && roomLoadingState.includes('等待对手') && !gamePage.includes('对局状态尚未加载')],
  ['tournament room waiting behavior covers first entrant', roomLoadingModule.tournamentRoomLoadingState(roomBase).kind === 'waiting'],
  ['tournament room loading covers opponent entry', roomLoadingModule.tournamentRoomLoadingState({ ...roomBase, room: { ...roomBase.room, started: true, players: roomBase.room.players.map(player => ({ ...player, connected: true })) } }).kind === 'loading'],
  ['tournament room loading covers refresh and disconnect recovery', roomLoadingModule.tournamentRoomLoadingState({ ...roomBase, status: 'connecting', recoveryPhase: 'authenticating', room: null }).kind === 'loading' && roomLoadingModule.tournamentRoomLoadingState({ ...roomBase, status: 'offline', recoveryPhase: 'disconnected' }).title.includes('连接中断') && gamePage.includes("send({ type: 'syncState' })")],
  ['tournament room exposes only genuine missing state as failure', roomLoadingModule.tournamentRoomLoadingState({ ...roomBase, room: null }).kind === 'failed' && roomLoadingModule.tournamentRoomLoadingState({ ...roomBase, connectionIssue: 'authentication' }).kind === 'failed'],
]

const failed = checks.filter(([, passed]) => !passed)
if (failed.length) {
  for (const [name] of failed) console.error(`FAIL ${name}`)
  process.exit(1)
}
console.log(`Tournament center contracts passed: ${checks.length}/${checks.length}`)
await import('./test-a3-tournament-notice-template-contract.mjs')
await import('./test-public-browsing.mjs')
