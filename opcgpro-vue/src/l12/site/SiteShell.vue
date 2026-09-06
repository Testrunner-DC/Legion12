<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { inviteFriend, l12State, resolveFriendInvitation, spectateRoom } from '@/l12/net'
import { friendApi, login, platformState, register, type PlatformPresence } from '@/l12/platform'
import SiteIcon from './SiteIcon.vue'
import L12SettingsModal from './L12SettingsModal.vue'
import MaintenanceTicker from './MaintenanceTicker.vue'

const siteBrandIcon = '/favicon.png'
const releaseVersion = String(import.meta.env.VITE_APP_VERSION || 'dev')
const updateEntries = [
  {
    date: '2026-09-07', title: '卡牌结算、对局操作与后台维护更新', version: releaseVersion,
    sections: [
      { title: '卡牌效果与构筑', items: [
        '猎杀时刻的效果文本已更新：将墓地4张卡牌自选顺序返回我方牌库底部，击杀对方1张兵力不高于6000的军团。回牌是效果而非发动费用；墓地不足4张时不回牌，仍可发动击杀。击杀目标失效不会撤回已经完成的回牌。',
        '雷神索尔赋予的冲锋正确覆盖效果登场的阿斯加德军团；黑胡子的完整登场效果被绝对防御无效后，不再继续抽牌；卡纽特大帝的声明与效果全部处理完毕后才继续翻天灾。',
        '乾坤·阳等原本兵力判断正确识别草薙剑的军团形态；步行者罗洛能识别魔戒赋予的阵营；哮天犬衍生物阵亡消失后仍会正确加入休整士气。',
        '荷鲁斯与阿尔伟达可选择支付费用后腾出的合法登场格，取消预选不会提前扣费；安德华拉诺特的构筑上限固定为1张。',
        '勇士等可代表多张的墓地卡改为逐张选择代表数量，不再堆叠大量组合；会排除无法完成所需数量的选项。没有符文时，消耗符文的阵营能力明确显示不可用。',
      ] },
      { title: '对局界面与交互', items: [
        '效果弹框与响应提示展示完整的对应效果，保留必要规则说明；太阳城相关提示使用正确来源。不发动与确认选择并列且同尺寸，长文字、窄窗口和卡牌候选均可滚动查看。',
        '双方手牌、战场、常驻回合计时及左侧详情重新分区，避免互相遮挡；双方手牌保持相同尺寸，无计时房间仅显示回合状态。士气每行3枚、最多显示12枚圆标，超出数量仍完整计数；我方向下、对方向上增加，资源组随行数相对牌库和墓地居中。',
        '选中卡牌下方保留设置、好友和对局工具三个等宽图标入口；进攻目标选择旁可取消尚未提交的选择，试炼操作与军团其他动作对齐。',
        '军团阵亡时先展示战斗伤害与退场动画；没有伤害数值的效果击杀显示“击杀”，不再用卡面兵力猜测伤害。返回手牌、位移及替代存活不会被误显示为普通阵亡。',
        '修复权威快照已经恢复却仍停留大厅的问题；主动返回后，不会被同一对局的重复快照再次拉回。排行榜主宰对阵行高与列宽统一，最强称号说明入口更清晰。',
        '全站文字统一使用黑体，通过字号和字重区分层级；下拉菜单保持深色，设置控件在窄窗口中不会互相挤压。',
      ] },
      { title: '好友与对局治理', items: [
        '对局工具提供Bug反馈、申请平局、屏蔽与举报入口，好友功能可在对局中打开；屏蔽用于阻止该玩家后续好友申请，可在好友管理中解除。',
        '真实双人休闲与排位对局可申请平局，由另一方决定是否接受；排位和局不增减双方分数，也不记为无效对局。重复响应、迟到确认和断线恢复不会重复结算。',
        '后台新增独立对局举报管理，保留申请、处理和操作记录；赛事对局继续使用裁判流程，机器人及GM沙盒不开放双人平局申请。',
      ] },
      { title: '维护与资讯后台', items: [
        '后台新增独立“维护服务器”按钮：立即停止开启新对局，已开始的对局可以继续完成和重连；管理员填写预计维护小时数，对战子页持续滚动广播。预计时长结束不会自行开放，需明确结束维护。',
        '即时维护与预约维护分开保存，普通配置保存或回退不会误清除即时维护；解除即时维护后，如预约仍在生效，会明确提示对局尚未开放。维护公告和倒计时会持续刷新，短暂请求失败保留最后有效内容。',
        '资讯后台可手动预览并导入官方摩点项目的历史及新增动态为草稿，正文图片保存到本站素材库；重复导入会识别已有内容，本地编辑冲突需要确认，不会自动发布或覆盖已发布文章。',
      ] },
    ],
  },
  {
    date: '2026-09-06', title: '最强称号规则、排位准备计时与对局体验更新', version: 'cb14e6b07d36fd8df1d1961b6a094d4f0b2577a9',
    sections: [
      { title: '称号与排行榜', items: [
        '“最强”称号固定按近30日有效排位评选：通常至少40场，冷门主宰降为20场，并要求5个参赛日与10名不同对方；少于6回合、掉线结局和无明确胜负的对局不参与评选。',
        '最强称号采用剔除本人对局后的主宰基准与贝叶斯修正胜率，降低小样本连胜影响；镜像局按整场去重。同分依次比较场次和胜场，最终只产生一名持有者。排行榜和“我的”均可打开完整规则。',
        '玩家榜按排名、昵称、阵营、段位、称号、最擅长主宰、七曜值、场次、战绩、胜率分列；搜索栏收窄，主宰头像统一为方形，主宰对阵数据行保持相同高度。',
        '后台同等级段位共用数值设置，三个派系仍可分别命名；历史荣耀仍以赛季结算结果为准，当前赛季不提前进入历史。',
      ] },
      { title: '卡牌与支付', items: [
        '信仰狂热者统一免除可复制主宰能力的全部费用，且不占用原能力正常次数；荷鲁斯的免费复活、杨戬的抽牌回牌、梅杰德的减兵力、瓦尔基里与洛基的能力、天照、须佐之男和雷神索尔均纳入回归检查，目标与登场空间限制仍然保留。',
        '神力中的太阳城、阿斯加德与高天原相关复制能力一并检查；太阳城检索和英灵回收按当前有效特征判断候选，不再遗漏因效果获得阵营特征的卡牌。',
        '普通士气、临时士气及陵墓守卫等可选资源统一参与支付选择；有多种合法组合时由玩家决定。彼界阵营效果也可以手动选择要消耗的士气。',
        '士气每行最多8枚，我方向下、对方向上增加行；黑色莲花产生的临时士气使用莲花专属标识，已选择或不足的资源不能被重复用于同一组费用。',
      ] },
      { title: '对局操作与记录', items: [
        '排位手牌调度显示先后手与1分钟倒计时，超时保留原手牌；天灾禁选每一步同样限时1分钟，超时由服务器从当前合法候选中选择。准备阶段不扣除正式对局总时限，刷新与重连不会重置选择时间。',
        '修正我方场面外框的样式传递；回合玩家与双方计时常驻，并为手牌预留独立位置。右侧玩家信息完整换行显示，无段位或最强称号时留空，不再展示阵营和缺失提示。',
        '对战左下角保留设置、在线人数和连接状态。对局记录按回合分组，显示卡牌编号、名称、公开选择及结算信息，可点击查看相关卡牌；非公开选择不泄露候选与手牌。',
        'GM沙盒正确显示横版卡牌，并提供独立卡牌详情入口。各处过小的操作文字与说明统一提高到卡牌图鉴效果文本的可读字号，并调整拥挤布局。',
      ] },
      { title: '好友、设置与运营', items: [
        '收到好友申请时弹窗并播放提示音，可直接通过、拒绝或屏蔽；取消屏蔽后允许重新申请，申请方不会获知自己被屏蔽。',
        '音乐切换使用平滑淡出、换曲与淡入；音乐和音效分别控制，音乐滑杆支持更细的低音量调整。卡牌尺寸、动画速度及声音设置继续按账号保存。',
        '后台单卡分析可按双方主宰筛选，对局档案通过“查看构筑”浏览赛后牌库；长回放按页读取，避免一次加载整场记录。',
        '维护与公告新增独立长期公告，显示在大厅牌库选择区上方；维护结束时间可不填写，并可显式启动服务器。部署或短暂连接失败期间，首页继续显示最后成功加载的已发布内容。',
      ] },
    ],
  },
  {
    date: '2026-09-06', title: '卡牌交互、排位恢复与运营功能更新', version: '37f1dcc9dd68ab958841d4415f1ec52195f30dae',
    sections: [
      {
        title: '卡牌效果',
        items: [
          '陵墓构造体同时触发离场与阵亡时，现在只会召唤3张休整的陵墓守卫，不再先全部登场后进入墓地。',
          '栖木猎鹰的登场与进攻动画会分别显示对应的抽牌时点，不再使用无法区分时点的说明。',
          '荷鲁斯的士气与军团费用改为分步支付；已横置的陵墓守卫仍可继续作为弃置费用，费用离场的军团及其空出的原位置也可参与后续复活选择。',
          '信仰狂热者复制荷鲁斯时会跳过原本的士气、费用与弃置要求，且不会占用荷鲁斯正常的“我方回合1次”。',
          '兰斯洛特登场时，即使原本没有符文，也可使用同一触发流程中“寻找圣杯之旅”刚获得的符文来取得冲锋。',
          '月读可选择另一张我方或对方的活跃/休整军团进行位移；托勒密十三世只能再次发动本回合打出的上一张主动战术。',
          '海伦的代替阵亡恢复为选发；黄金圣甲虫的弃牌减兵力效果严格限制为我方回合1次。',
          '梅林等“选择一项”效果会先公开所选项目，再询问绝对防御是否响应，响应方能够看见具体选择。',
        ],
      },
      {
        title: '对局与房间',
        items: [
          '好友房现在可以由房主选择并固定当前赛季天灾规则，未选择时仍沿用普通好友房规则。',
          '进攻选择阶段只高亮合法的被攻击对象；挑衅等关键词被无效时保留文字并显示红色叉号，避免误认为效果被移除。',
          '统一放大“不响应”按钮；临时士气改用白色十二军团标志显示，并在休整后正确消失。',
          '胜负结算页面不再自动离开，双方可查看结果并在点击返回后退出对局。',
        ],
      },
      {
        title: '排位与维护',
        items: [
          '排位对局在服务器重启后可恢复进行中状态、双方总时限、单次操作时限、掉线窗口及尚未处理的交互。',
          '对局记录与排位账本增加持久化结算对账；重复结算会保持幂等，异常旧记录会被隔离而不会污染正常战绩。',
          '后台新增计划维护与恢复服务器功能：按设定时间停止新对局、循环广播维护预告、提醒进行中的玩家，并在维护开始时废弃未结束对局。',
          '维护即将开始或维护期间，玩家点击开启对局会明确提示对局功能已关闭；已经进行的对局在正式维护前不受开局门禁影响。',
        ],
      },
      {
        title: '界面与设置',
        items: [
          '排行榜统一使用主宰头像并压缩主宰对阵行高；对局内的回合标识和双方计时常驻显示，玩家信息栏强制完整展示。',
          '加入官网与对局音乐，并提供音乐、音效、卡牌尺寸和动画速度设置；登录账号后会跨设备保存。',
          '本次起每次正式部署都会附带与线上版本对应的更新日志，明确列出已完成的卡效和功能结果。',
        ],
      },
    ],
  },
]

const route = useRoute()
const router = useRouter()
const mobileOpen = ref(false)
const modal = ref<'settings' | 'updates' | 'online' | null>(null)

const mainNav = [
  { to: '/', icon: 'home', label: '主页' },
  { to: '/news', icon: 'news', label: '资讯' },
  { to: '/battle', icon: 'battle', label: '对战' },
  { to: '/decks', icon: 'decks', label: '牌库' },
  { to: '/cards', icon: 'archive', label: '图鉴' },
  { to: '/rules', icon: 'rules', label: '规则' },
  { to: '/me', icon: 'profile', label: '我的' },
]
const battleNav = [
  { to: '/', icon: 'home', label: '主页' },
  { to: '/battle', icon: 'battle', label: '大厅' },
  { to: '/battle/tournaments', icon: 'tournament', label: '赛事' },
  { to: '/decks?from=%2Fbattle', icon: 'decks', label: '牌库' },
  { to: '/battle/rankings', icon: 'ranking', label: '排行' },
  { to: '/battle/friends', icon: 'friends', label: '好友' },
  { to: '/battle/records', icon: 'records', label: '对局' },
]
const nav = computed(() => route.meta.section === 'battle' ? battleNav : mainNav)
const accountGate = computed(() => route.meta.requiresAccount === true && !platformState.account)
const authMode = ref<'login' | 'register'>('login')
const auth = reactive({ username: '', password: '' })
const authBusy = ref(false)
const authNotice = ref('')
async function submitAuth() {
  authBusy.value = true
  authNotice.value = ''
  try {
    if (authMode.value === 'login') await login(auth.username, auth.password)
    else await register(auth.username, auth.password)
    auth.password = ''
  } catch (error) {
    authNotice.value = error instanceof Error ? error.message : '登录失败'
  } finally { authBusy.value = false }
}

const onlinePlayers = ref<PlatformPresence[]>([])
const onlineCount = computed(() => onlinePlayers.value.length)
const onlineActionBusy = ref('')
const onlineNotice = ref('')
const connectionLabel = computed(() => {
  if (l12State.connectionIssue === 'authentication') return '登录状态失效'
  if (l12State.connectionIssue === 'superseded') return '已由其他页面接管'
  if (l12State.connectionIssue === 'maintenance') return '维护中 · 连接正常'
  if (l12State.status === 'connecting') return l12State.recoveryPhase === 'snapshot-received' ? '快照确认中' : '连接恢复中'
  if (l12State.status === 'online') return '连接正常'
  return l12State.connectionIssue === 'websocket' ? '对战连接中断' : '未连接'
})

watch(() => route.fullPath, () => { mobileOpen.value = false })
function enterFriendRoom() { void router.push('/battle') }
let presenceTimer = 0
async function refreshPresence() {
  if (!platformState.account || !platformState.token) {
    onlinePlayers.value = []
    return
  }
  try { onlinePlayers.value = await friendApi.presence() } catch { onlinePlayers.value = [] }
}
const activityLabel = (player: PlatformPresence) => ({ idle: '在线 · 空闲', inRoom: '在线 · 房间中', playing: '在线 · 对局中', spectating: '在线 · 观战中' }[player.activity])
async function addOnlineFriend(player: PlatformPresence) {
  onlineActionBusy.value = player.accountId
  onlineNotice.value = ''
  try {
    const result = await friendApi.request(player.accountId)
    onlineNotice.value = result.message
    await refreshPresence()
  } catch (error) { onlineNotice.value = error instanceof Error ? error.message : '好友申请发送失败' }
  finally { onlineActionBusy.value = '' }
}
async function resolveOnlineFriend(player: PlatformPresence, accept: boolean) {
  onlineActionBusy.value = player.accountId
  onlineNotice.value = ''
  try {
    const result = await friendApi.resolve(player.accountId, accept)
    onlineNotice.value = result.message
    await refreshPresence()
  } catch (error) { onlineNotice.value = error instanceof Error ? error.message : '好友申请处理失败' }
  finally { onlineActionBusy.value = '' }
}
function inviteOnlinePlayer(player: PlatformPresence) {
  onlineNotice.value = ''
  inviteFriend(player.accountId)
  onlineNotice.value = `已向 ${player.username} 发送对战邀请`
}
function watchOnlinePlayer(player: PlatformPresence) {
  if (!player.roomCode || !player.canSpectate) return
  spectateRoom(player.roomCode)
  modal.value = null
  void router.push('/battle')
}
function answerInvitation(accept: boolean) {
  if (!l12State.friendInvitation) return
  const invitationId = l12State.friendInvitation.invitationId
  l12State.friendInvitation = null
  resolveFriendInvitation(invitationId, accept)
}
watch(() => platformState.account?.id, () => void refreshPresence())
onMounted(() => {
  window.addEventListener('l12-friend-room-created', enterFriendRoom)
  void refreshPresence()
  presenceTimer = window.setInterval(() => void refreshPresence(), 15_000)
})
onBeforeUnmount(() => {
  window.removeEventListener('l12-friend-room-created', enterFriendRoom)
  window.clearInterval(presenceTimer)
})
</script>

<template>
  <div class="site-shell">
    <header class="site-mobile-head">
      <router-link class="mobile-brand" to="/" title="返回主页"><img :src="siteBrandIcon" alt="十二军团"/></router-link>
      <button aria-label="打开导航" @click="mobileOpen = !mobileOpen">{{ mobileOpen ? '×' : '☰' }}</button>
    </header>

    <aside class="site-sidebar" :class="{ open: mobileOpen }">
      <router-link class="site-brand" to="/" title="十二军团官方网站">
        <img :src="siteBrandIcon" alt="十二军团"/>
      </router-link>

      <nav class="site-nav" aria-label="主要导航">
        <router-link v-for="item in nav" :key="item.to" :to="item.to" :title="item.label">
          <SiteIcon :name="item.icon"/><span>{{ item.label }}</span>
        </router-link>
      </nav>

      <div class="site-utilities">
        <button title="设置" @click="modal = 'settings'"><SiteIcon name="settings"/><span>设置</span></button>
        <button title="更新日志" @click="modal = 'updates'"><SiteIcon name="updates"/><span>更新日志</span></button>
        <button title="在线人数" @click="modal = 'online'"><span class="utility-icon"><SiteIcon name="online"/><i>{{ onlineCount }}</i></span><span>在线人数</span></button>
        <button class="connection" :class="l12State.status" :title="connectionLabel"><span class="utility-icon"><SiteIcon name="connection"/><i/></span><span>{{ connectionLabel }}</span></button>
      </div>
    </aside>

    <main class="site-content"><MaintenanceTicker v-if="route.meta.section === 'battle'"/><slot /></main>

    <div v-if="modal" class="site-modal-mask" @click.self="modal = null">
      <L12SettingsModal v-if="modal === 'settings'" @close="modal = null"/>

      <section v-else-if="modal === 'updates'" class="site-modal update-modal">
        <header><div><small>CHANGELOG</small><h2>更新日志</h2></div><button @click="modal = null">×</button></header>
        <article v-for="entry in updateEntries" :key="`${entry.date}-${entry.version}`">
          <time>{{ entry.date }}</time><h3>{{ entry.title }}</h3><code>{{ entry.version }}</code>
          <section v-for="section in entry.sections" :key="section.title" class="update-section">
            <h4>{{ section.title }}</h4>
            <ul><li v-for="item in section.items" :key="item">{{ item }}</li></ul>
          </section>
        </article>
      </section>

      <section v-else class="site-modal online-modal">
        <header><div><small>ONLINE</small><h2>在线玩家</h2></div><button @click="modal = null">×</button></header>
        <p v-if="onlineNotice" class="online-notice">{{ onlineNotice }}</p>
        <div v-for="player in onlinePlayers" :key="player.accountId" class="online-entry">
          <i/><div class="online-identity"><b>{{ player.username }}</b><span>{{ player.accountId === platformState.account?.id ? '在线 · 当前账号' : activityLabel(player) }}</span></div>
          <div v-if="player.friendStatus !== 'self'" class="online-actions">
            <template v-if="player.friendStatus === 'pending' && player.friendDirection === 'incoming'">
              <button class="quiet" :disabled="onlineActionBusy === player.accountId" @click="resolveOnlineFriend(player, false)">拒绝</button>
              <button :disabled="onlineActionBusy === player.accountId" @click="resolveOnlineFriend(player, true)">{{ onlineActionBusy === player.accountId ? '处理中' : '接受' }}</button>
            </template>
            <button v-else-if="player.activity === 'playing'" :disabled="!player.canSpectate" :title="player.actionReason || '进入该玩家的对局观战'" @click="watchOnlinePlayer(player)">观战</button>
            <button v-else-if="player.friendStatus === 'accepted'" :disabled="!player.canInvite" :title="player.actionReason || '邀请好友直接建立房间'" @click="inviteOnlinePlayer(player)">邀请对战</button>
            <button v-else-if="player.friendStatus === 'pending'" disabled>已申请</button>
            <button v-else :disabled="onlineActionBusy === player.accountId" @click="addOnlineFriend(player)">{{ onlineActionBusy === player.accountId ? '发送中' : '添加好友' }}</button>
          </div>
        </div>
        <div v-if="onlinePlayers.length === 0" class="modal-empty">登录并连接服务器后可查看在线玩家。</div>
      </section>
    </div>

    <div v-if="accountGate" class="site-modal-mask account-gate">
      <section class="site-modal auth-modal">
        <header><div><small>BATTLE ACCOUNT</small><h2>登录后进入对战</h2></div><button title="返回主页" @click="router.push('/')">×</button></header>
        <p>对战、赛事、好友、排行榜和个人对局记录使用同一账号身份。</p>
        <div class="auth-tabs"><button :class="{ active: authMode === 'login' }" @click="authMode = 'login'">登录</button><button :class="{ active: authMode === 'register' }" @click="authMode = 'register'">注册</button></div>
        <label>用户名<input v-model="auth.username" maxlength="20" autocomplete="username"/></label>
        <label>密码<input v-model="auth.password" type="password" maxlength="128" :autocomplete="authMode === 'login' ? 'current-password' : 'new-password'" @keyup.enter="submitAuth"/></label>
        <p v-if="authNotice" class="auth-notice">{{ authNotice }}</p>
        <button class="auth-submit" :disabled="authBusy || !auth.username.trim() || !auth.password" @click="submitAuth">{{ authMode === 'login' ? '登录并进入' : '注册并进入' }}</button>
        <button class="auth-home" @click="router.push('/')">返回主页</button>
      </section>
    </div>

    <div v-if="l12State.friendInvitation" class="site-modal-mask invitation-gate">
      <section class="site-modal invitation-modal">
        <header><div><small>FRIEND BATTLE</small><h2>好友对战邀请</h2></div></header>
        <p><b>{{ l12State.friendInvitation.fromName }}</b> 邀请你进行友谊战。</p>
        <div class="invite-code"><span>预留房间码</span><strong>{{ l12State.friendInvitation.roomCode }}</strong></div>
        <p class="invite-note">接受后将直接创建房间：发起方成为房主，你将自动进入整备室，无需再输入房间码。</p>
        <div class="invite-actions"><button class="quiet" @click="answerInvitation(false)">拒绝</button><button @click="answerInvitation(true)">接受并进入房间</button></div>
      </section>
    </div>
  </div>
</template>

<style scoped>
.site-shell{--nav-w:92px;width:100vw;height:100vh;background:radial-gradient(circle at 80% 10%,rgba(18,101,108,.12),transparent 34%),radial-gradient(circle at 12% 84%,rgba(121,22,32,.13),transparent 35%),#060a0d;color:#f2f0e9;font-family:'Microsoft YaHei','微软雅黑',system-ui,sans-serif}.site-sidebar{position:fixed;z-index:40;inset:0 auto 0 0;width:var(--nav-w);display:flex;flex-direction:column;border-right:1px solid rgba(232,227,213,.16);background:#0d1318}.site-brand{display:flex;height:96px;flex-direction:column;align-items:center;justify-content:center;gap:5px;color:#f3eee1;text-decoration:none}.site-brand img{width:44px;height:44px;border:0;border-radius:0;object-fit:contain;filter:brightness(0) invert(1)}.site-nav{display:flex;flex:1;min-height:0;flex-direction:column;overflow-y:auto}.site-nav a,.site-utilities button{position:relative;display:flex;min-height:58px;flex-direction:column;align-items:center;justify-content:center;gap:5px;border:0;background:transparent;color:#7d8991;text-decoration:none}.site-nav a:hover,.site-nav a.router-link-active{background:linear-gradient(90deg,rgba(48,181,190,.2),transparent);color:#f4f1e9}.site-nav a.router-link-active::before{content:'';position:absolute;left:0;top:12px;bottom:12px;width:3px;background:#51c5cc;box-shadow:0 0 12px #51c5cc}.site-nav b,.site-utilities b{font-size:15px}.site-nav span,.site-utilities span{font-size:14px;font-weight:900}.site-utilities{display:flex;flex-direction:column;gap:8px;padding:8px 0;border-top:1px solid rgba(232,227,213,.12)}.site-utilities button{width:100%;min-height:48px}.site-utilities .connection i{width:8px;height:8px;border-radius:50%;background:#6b7272}.site-utilities .connection.online i{background:#55c99a;box-shadow:0 0 8px #55c99a}.site-utilities .connection.connecting i{background:#d7b15f}.site-content{position:absolute;inset:0 0 0 var(--nav-w);overflow:auto}.site-mobile-head{display:none}.site-modal-mask{position:fixed;z-index:100;inset:0;display:grid;place-items:center;padding:20px;background:rgba(1,4,7,.75);backdrop-filter:blur(10px)}.site-modal{width:min(560px,94vw);max-height:min(720px,90vh);overflow:auto;border:1px solid rgba(235,230,216,.28);background:#111923;box-shadow:0 28px 90px #000;padding:24px}.site-modal>header{display:flex;align-items:center;justify-content:space-between;padding-bottom:15px;border-bottom:1px solid rgba(235,230,216,.14)}.site-modal header small{color:#51c5cc;font:900 14px monospace;letter-spacing:.18em}.site-modal h2{margin:4px 0 0;font-size:24px}.site-modal header button{width:34px;height:34px;border:1px solid #48545c;background:#0a1016;color:#fff}.setting-row{display:flex;align-items:center;justify-content:space-between;gap:18px;padding:18px 0;border-bottom:1px solid rgba(235,230,216,.1)}.setting-row b,.setting-row span{display:block}.setting-row span{margin-top:5px;color:#7f8b93;font-size:14px}.setting-row select,.toggle{min-width:118px;padding:10px;border:1px solid #52606a;background:#081018;color:#fff;font-weight:900}.toggle.on{border-color:#54b48f;color:#7ee2b9}.setting-note{color:#7e898f;font-size:14px;line-height:1.7}.update-modal article{padding:18px 0;border-bottom:1px solid rgba(235,230,216,.1)}.update-modal time{color:#d6ad59;font-size:14px;font-weight:900}.update-modal h3{margin:6px 0;font-size:15px}.update-modal code{display:inline-block;padding:3px 6px;border:1px solid #6f602e;color:#e5c866;font-size:14px}.update-modal li{margin:7px 0;color:#a8b0b3;font-size:14px;line-height:1.6}.online-entry{display:flex;align-items:center;gap:12px;margin-top:12px;padding:14px;background:#0a1118}.online-entry>i{flex:0 0 auto;width:9px;height:9px;border-radius:50%;background:#55c99a;box-shadow:0 0 8px #55c99a}.online-identity{min-width:0;flex:1}.online-entry b,.online-entry span{display:block}.online-entry span{margin-top:3px;color:#718088;font-size:14px}.online-actions{display:flex;flex:0 0 auto;gap:7px}.online-actions button{min-width:82px;padding:8px 10px;border:1px solid #d2b35f;background:#29220f;color:#f0d478;font-size:14px;font-weight:900}.online-actions button:disabled{border-color:#3f484e;background:#121920;color:#68747a;cursor:not-allowed}.online-notice{margin:12px 0 0;padding:9px 11px;border-left:3px solid #51c5cc;background:#0a151b;color:#9fd5d8;font-size:14px}.modal-empty{margin-top:18px;padding:38px 20px;border:1px dashed #39444b;color:#738089;text-align:center;font-size:14px;line-height:1.7}
@media(max-width:760px){.site-shell{--nav-w:0px}.site-mobile-head{position:fixed;z-index:60;top:0;left:0;right:0;height:58px;display:flex;align-items:center;justify-content:space-between;padding:0 14px;border-bottom:1px solid rgba(232,227,213,.16);background:#0d1318}.mobile-brand{display:flex;align-items:center;gap:9px;color:#fff;text-decoration:none}.mobile-brand b{display:grid;width:30px;height:30px;place-items:center;border:1px solid #d8b362;font:900 14px Georgia}.mobile-brand span{font-weight:900}.site-mobile-head button{width:38px;height:38px;border:1px solid #46525a;background:#111a22;color:#fff;font-size:20px}.site-sidebar{top:58px;width:min(310px,84vw);transform:translateX(-105%);transition:transform .2s}.site-sidebar.open{transform:none;box-shadow:18px 0 50px #000}.site-brand{display:none}.site-nav a,.site-utilities button{min-height:52px;flex-direction:row;justify-content:flex-start;padding:0 24px;gap:15px}.site-nav span,.site-utilities span{font-size:14px}.site-utilities{display:grid;grid-template-columns:1fr 1fr;column-gap:0;row-gap:8px}.site-content{top:58px}.site-modal{padding:18px}.setting-row{align-items:flex-start;flex-direction:column}.setting-row select,.toggle{width:100%}}
.utility-icon{position:relative;display:grid;place-items:center}.utility-icon>i{position:absolute;top:-7px;right:-9px;display:grid!important;min-width:15px!important;width:auto!important;height:15px!important;place-items:center;padding:0 3px;border-radius:8px!important;background:#71303a;color:#fff;font:900 14px monospace!important;font-style:normal}.site-utilities .connection .utility-icon>i{top:auto;right:-5px;bottom:-3px;width:7px!important;min-width:7px!important;height:7px!important;padding:0;border-radius:50%!important;background:#6b7272}.site-utilities .connection.online .utility-icon>i{background:#55c99a!important;box-shadow:0 0 8px #55c99a}.site-utilities .connection.connecting .utility-icon>i{background:#d7b15f!important}
.audio-setting{display:flex;align-items:center;gap:12px}.audio-setting input{width:150px}
@media(max-width:760px){.mobile-brand img{width:30px;height:30px;border:0;border-radius:0;object-fit:contain;filter:brightness(0) invert(1)}.mobile-brand b{display:none}}
.auth-modal>p{color:#87939a;font-size:14px;line-height:1.7}.auth-tabs{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin:18px 0}.auth-tabs button,.auth-home{padding:11px;border:1px solid #46535b;background:#080e13;color:#9aa3a7;font-weight:900}.auth-tabs button.active{border-color:#e1c16c;background:#2a2414;color:#f2d985}.auth-modal label{display:block;margin:13px 0;color:#abb3b6;font-size:14px;font-weight:900}.auth-modal input{display:block;width:100%;margin-top:7px;padding:12px;border:1px solid #4b5860;background:#080e13;color:#fff}.auth-submit{width:100%;margin-top:16px;padding:12px;border:1px solid #e1c16c;background:#e1c16c;color:#080b0d;font-weight:900}.auth-submit:disabled{opacity:.45}.auth-home{width:100%;margin-top:8px}.auth-notice{padding:9px!important;border-left:3px solid #a72e39;background:#291016;color:#e5aab0!important}.account-gate{z-index:140}
.invitation-gate{z-index:160}.invitation-modal>p{color:#aeb6ba;line-height:1.7}.invite-code{display:flex;align-items:center;justify-content:space-between;margin:18px 0;padding:14px;border:1px solid #4e5b63;background:#080e13}.invite-code span{color:#79868d;font-size:14px}.invite-code strong{color:#f0d478;font:900 22px monospace;letter-spacing:.18em}.invite-note{font-size:14px}.invite-actions{display:grid;grid-template-columns:1fr 1.7fr;gap:10px;margin-top:20px}.invite-actions button{padding:12px;border:1px solid #e1c16c;background:#e1c16c;color:#080b0d;font-weight:900}.invite-actions button.quiet{border-color:#4a565e;background:#0a1117;color:#929da2}
.online-actions button.quiet{border-color:#4b565c;background:#0b1217;color:#9ba5aa}
.update-modal{width:min(680px,94vw)}
.update-modal h3{font-size:17px}
.update-section{margin-top:18px}
.update-section h4{margin:0 0 8px;padding-left:9px;border-left:3px solid #d6ad59;color:#f0ede5;font-size:14px}
.update-modal ul{margin:0;padding-left:20px}
.update-modal li{margin:8px 0;color:#b2b9bc;line-height:1.75}
</style>
