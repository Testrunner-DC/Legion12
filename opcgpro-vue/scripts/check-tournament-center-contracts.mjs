import fs from 'node:fs'
import path from 'node:path'
import ts from 'typescript'

const root = process.cwd()
const read = file => fs.readFileSync(path.join(root, file), 'utf8')
const detail = read('src/l12/site/TournamentDetailPage.vue')
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
const gamePage = read('src/l12/GamePage.vue')
const roomLoadingState = read('src/l12/tournamentRoomLoadingState.ts')
const tournamentServer = read('../服务端WebSocket/TwelveLegions/L12PlatformStore.Tournaments.cs')

const loadTypeScriptModule = async source => {
  const transpiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  }).outputText
  return import(`data:text/javascript;base64,${Buffer.from(transpiled).toString('base64')}`)
}
const createPolicyModule = await loadTypeScriptModule(createPolicy)
const roomLoadingModule = await loadTypeScriptModule(roomLoadingState)
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

const checks = [
  ['detail separates organizer and judge capabilities', detail.includes('const canOrganize') && detail.includes('const canJudge') && !detail.includes('const canManage')],
  ['participant moderation is organizer-only', detail.includes('v-if="canOrganize && person.accountId !== tournament.organizerAccountId"')],
  ['management is an extracted component', detail.includes('<TournamentManagementPanel') && management.includes('赛事生命周期')],
  ['referee cannot open organizer management', detail.includes('const canViewManage = computed(() => canOrganize.value')],
  ['judge calls only list the current player matches', judge.includes('const myMatches = computed') && judge.includes('match.playerAAccountId === accountId.value')],
  ['appeal is limited to table players', judge.includes('const canAppeal =') && judge.includes('v-if="canAppeal(item)"')],
  ['conflicted staff are excluded from assignment choices', judge.includes("person.accountId !== match?.playerAAccountId")],
  ['player-facing enums use shared Chinese labels', detail.includes("@/l12/tournamentLabels") && judge.includes("@/l12/tournamentLabels") && summary.includes('tournamentViewerRoleText')],
  ['check-in range matches server authority', wizard.includes('v-model.number="form.checkInMinutes" type="number" min="1" max="60"')],
  ['admin tournament workbench has an explicit admin-only name', router.includes("import('@/l12/site/AdminTournamentWorkbench.vue')") && !router.includes('TournamentCenterPage')],
  ['player route uses hub and stable detail routes', router.includes("component: () => import('@/l12/site/TournamentHubPage.vue')") && router.includes("path: '/battle/tournaments/:code'")],
  ['pre-check-in selects an account deck', detail.includes('syncSavedDecksFromAccount') && detail.includes('选择账号牌组') && detail.includes("preCheckIn(item.id, item.version, deckName.value, '')")],
  ['critical tournament actions use inline reasons', !detail.includes('prompt(') && !judge.includes('prompt(') && !management.includes('confirm(') && detail.includes('participantReasons[person.accountId]') && judge.includes('appealReasons[item.id]')],
  ['organizer transfer uses eligible named candidates', management.includes('transferCandidates') && management.includes('选择本场裁判或主办者好友')],
  ['career history is visible and server-paged', hub.includes('个人赛事履历') && hub.includes('career.totalPages') && platform.includes("params.set('pageSize', String(query.pageSize))")],
  ['tournament visible timing uses minutes while authority stays in seconds', [detail, wizard, admin].every(source => !source.includes('（秒）') && !source.includes('操作秒数') && !source.includes('重连秒数') && !source.includes('选择秒数') && !source.includes('调度秒数') && !source.includes(' }} 秒')) && wizard.includes('timeControlMinutes') && admin.includes('timeControlMinutes') && time.includes('tournamentTimeControlSeconds') && time.includes('tournamentMinutesLabel')],
  ['format switches remove mutually exclusive cut fields', createPolicy.includes("input.format === 'single'") && createPolicy.includes('swissRounds: 0, cutSize: undefined') && createPolicy.includes("input.format === 'swiss-cut'") && createPolicy.includes('cutSize: undefined')],
  ['single elimination hides the mutually exclusive swiss-round input', wizard.includes(`<label v-if="form.format !== 'single'">瑞士轮数`) && admin.includes(`<label v-if="form.format!=='single'">瑞士轮数`)],
  ['templates manual changes and restored drafts share format normalization', wizard.includes('normalizeTournamentFormatInPlace(form)') && wizard.includes('watch(() => form.format') && wizard.indexOf('Object.assign(form, saved)') < wizard.lastIndexOf('normalizeTournamentFormatInPlace(form)')],
  ['dry-run and create share one frozen normalized input', wizard.includes('const input = buildTournamentCreateInput') && wizard.includes('tournamentApi.create(input, props.platformVersion, true)') && wizard.includes('tournamentApi.create(input, props.platformVersion)')],
  ['disaster modes freeze authoritative policy and none stays empty', createPolicy.includes("if (mode === 'none') return []") && createPolicy.includes('policy.disasterCardIds') && createPolicy.includes('cardIds.length < 9 || cardIds.length > 64') && createPolicy.includes('TOURNAMENT_ANNIHILATION_CARD_ID') && wizard.includes('getEffectiveOperationsPolicy()')],
  ['empty or invalid operations disaster policy fails readably', createPolicy.includes('当前运营策略未提供可用天灾池') && createPolicy.includes('当前运营策略天灾池必须为 9–64 张') && wizard.includes('policyError')],
  ['server keeps strict disaster pool count and annihilation-last authority', tournamentServer.includes('normalizedDisasters.Length is < 9 or > 64') && tournamentServer.includes('normalizedDisasters[^1], L12ActiveDisasterRules.AnnihilationCardId') && tournamentServer.includes('天灾池须为 9–64 张（含堙灭），且堙灭固定在最后一张')],
  ['format normalization behavior removes stale cut values', createPolicyModule.normalizeTournamentFormatFields({ ...formatBase, format: 'single' }).cutSize === undefined && createPolicyModule.normalizeTournamentFormatFields({ ...formatBase, format: 'single' }).swissRounds === 0 && createPolicyModule.normalizeTournamentFormatFields({ ...formatBase, format: 'swiss' }).cutSize === undefined],
  ['disaster snapshot behavior validates policy and clones authority', createPolicyModule.tournamentDisasterSnapshot('none', { disasterCardIds: [] }).length === 0 && createPolicyModule.tournamentDisasterSnapshot('season', { disasterCardIds: policyCards }).join(',') === policyCards.join(',') && createPolicyModule.tournamentDisasterSnapshot('season', { disasterCardIds: policyCards }) !== policyCards],
  ['empty short duplicate and misplaced-annihilation policies fail closed', throwsWith(() => createPolicyModule.tournamentDisasterSnapshot('season', { disasterCardIds: [] }), '未提供可用天灾池') && throwsWith(() => createPolicyModule.tournamentDisasterSnapshot('all', { disasterCardIds: ['S01-DS10'] }), '9–64') && throwsWith(() => createPolicyModule.tournamentDisasterSnapshot('random', { disasterCardIds: [...policyCards.slice(0, -1), policyCards[0], 'S01-DS10'] }), '重复卡牌') && throwsWith(() => createPolicyModule.tournamentDisasterSnapshot('season', { disasterCardIds: ['S01-DS10', ...policyCards.slice(0, -1)] }), '最后一张')],
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
