import fs from 'node:fs'
import path from 'node:path'

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
]

const failed = checks.filter(([, passed]) => !passed)
if (failed.length) {
  for (const [name] of failed) console.error(`FAIL ${name}`)
  process.exit(1)
}
console.log(`Tournament center contracts passed: ${checks.length}/${checks.length}`)
