<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { l12State } from '@/l12/net'
import { canAccessAdmin, changePassword, emailApi, login, logout, mfaCapability as loadMfaCapability, platformRequest, platformState, rankedApi, register, sessionApi, type EmailStatus, type MfaCapability, type PlatformSession, type RankedOverview } from '@/l12/platform'
import { ensureOfficialPrebuiltDecks } from '@/l12/decks'

interface Match { player0: string; player1: string; winner?: number | null; endedUtc?: string | null; startedUtc: string }
const publicHistory = ref(localStorage.getItem('l12-public-history') === 'true')
const matches = ref<Match[]>([])
const notice = ref('')
const authMode = ref<'login' | 'register'>('login')
const auth = reactive({ username: '', password: '', currentPassword: '', newPassword: '' })
const emailForm = reactive({ email: '', currentPassword: '' })
const authBusy = ref(false)
const route = useRoute()
const router = useRouter()
const sessions = ref<PlatformSession[]>([])
const mfa = ref<MfaCapability | null>(null)
const emailStatus = ref<EmailStatus | null>(null)
const emailFeatureEnabled = ref(false)
const ranked = ref<RankedOverview | null>(null)
const selectedMasterTitle = ref('')

async function loadAccountData() {
  if (!platformState.account || platformState.account.mustChangePassword) return
  const [matchResult, sessionResult, rankedResult] = await Promise.allSettled([
    platformRequest<Match[]>('/api/matches?limit=200'), sessionApi.list(), rankedApi.overview(),
  ])
  if (matchResult.status === 'fulfilled') matches.value = matchResult.value
  if (sessionResult.status === 'fulfilled') sessions.value = sessionResult.value
  if (rankedResult.status === 'fulfilled') {
    ranked.value = rankedResult.value
    selectedMasterTitle.value = rankedResult.value.profile.selectedMasterTitle || ''
  }
}

async function saveRankedTitle() {
  authBusy.value = true; notice.value = ''
  try {
    const profile = await rankedApi.selectTitle(selectedMasterTitle.value || undefined)
    if (ranked.value) ranked.value = { ...ranked.value, profile }
    selectedMasterTitle.value = profile.selectedMasterTitle || ''
    notice.value = '对战称号已更新'
  } catch (error) { notice.value = error instanceof Error ? error.message : '称号更新失败' }
  finally { authBusy.value = false }
}
async function loadEmailStatus() {
  if (!emailFeatureEnabled.value || !platformState.account || platformState.account.mustChangePassword) { emailStatus.value = null; return }
  try { emailStatus.value = await emailApi.status() } catch { emailStatus.value = null }
}
async function loadEmailCapability() {
  try {
    const capability = await emailApi.capability()
    emailFeatureEnabled.value = capability.enabled
    await loadEmailStatus()
  } catch { emailFeatureEnabled.value = false; emailStatus.value = null }
}
onMounted(() => { loadAccountData(); loadEmailCapability(); loadMfaCapability().then(value => { mfa.value = value }).catch(() => {}) })
watch(publicHistory, value => localStorage.setItem('l12-public-history', String(value)))
async function submitAuth() {
  authBusy.value = true; notice.value = ''
  try {
    if (authMode.value === 'login') await login(auth.username, auth.password)
    else await register(auth.username, auth.password)
    if (platformState.account?.mustChangePassword) {
      notice.value = '当前使用管理员临时密码登录，必须先在下方修改密码'
      auth.password = ''
      return
    }
    await ensureOfficialPrebuiltDecks()
    const redirect = typeof route.query.redirect === 'string' && route.query.redirect.startsWith('/')
      && !route.query.redirect.startsWith('//')
      ? route.query.redirect : ''
    if (redirect) {
      await router.replace(redirect)
      return
    }
    await loadAccountData()
    await loadEmailCapability()
    notice.value = authMode.value === 'login' ? '登录成功' : '账号建立成功，六阵营预组会自动加入牌库'
    auth.password = ''
  } catch (error) { notice.value = error instanceof Error ? error.message : '操作失败' }
  finally { authBusy.value = false }
}
async function submitPassword() {
  authBusy.value = true; notice.value = ''
  try { const result = await changePassword(auth.currentPassword, auth.newPassword); notice.value = result.message; auth.currentPassword = ''; auth.newPassword = ''; await loadAccountData(); await loadEmailStatus() }
  catch (error) { notice.value = error instanceof Error ? error.message : '修改失败' }
  finally { authBusy.value = false }
}
async function signOut() {
  authBusy.value = true; notice.value = ''
  try { await logout(); notice.value = '当前设备已退出，服务器会话已撤销' }
  catch (error) { notice.value = `本机已退出；服务器会话撤销失败：${error instanceof Error ? error.message : '未知错误'}` }
  finally { sessions.value = []; authBusy.value = false }
}
async function submitEmailBinding() {
  authBusy.value = true; notice.value = ''
  try {
    const result = await emailApi.bind(emailForm.email, emailForm.currentPassword)
    notice.value = result.message; emailForm.currentPassword = ''; await loadEmailStatus()
  } catch (error) { notice.value = error instanceof Error ? error.message : '邮箱验证请求失败' }
  finally { authBusy.value = false }
}
async function unbindEmail() {
  authBusy.value = true; notice.value = ''
  try {
    const result = await emailApi.unbind(emailForm.currentPassword)
    notice.value = result.message; emailForm.email = ''; emailForm.currentPassword = ''; await loadEmailStatus()
  } catch (error) { notice.value = error instanceof Error ? error.message : '邮箱解绑失败' }
  finally { authBusy.value = false }
}
async function revokeSession(session: PlatformSession) {
  authBusy.value = true; notice.value = ''
  try {
    await sessionApi.revoke(session.id)
    if (session.current) {
      await logout({ revokeServer: false })
      sessions.value = []
      notice.value = '当前设备已退出，服务器会话已撤销'
      return
    }
    sessions.value = sessions.value.filter(item => item.id !== session.id)
    notice.value = '指定设备会话已撤销'
  } catch (error) { notice.value = error instanceof Error ? error.message : '撤销失败' }
  finally { authBusy.value = false }
}
async function revokeOtherSessions() {
  authBusy.value = true; notice.value = ''
  try {
    for (const session of sessions.value.filter(item => !item.current)) await sessionApi.revoke(session.id)
    sessions.value = sessions.value.filter(item => item.current)
    notice.value = '其他设备会话已全部撤销'
  } catch (error) { notice.value = error instanceof Error ? error.message : '撤销失败' }
  finally { authBusy.value = false }
}
async function revokeAllSessions() {
  authBusy.value = true; notice.value = ''
  try { await sessionApi.revokeAll(); notice.value = '全部设备会话已撤销' }
  catch (error) { notice.value = error instanceof Error ? error.message : '撤销失败' }
  finally { await logout({ revokeServer: false }); sessions.value = []; authBusy.value = false }
}
const myMatches = computed(() => matches.value)
const wins = computed(() => myMatches.value.filter(match => match.winner === (match.player0 === l12State.nickname ? 0 : 1)).length)
const losses = computed(() => myMatches.value.filter(match => match.endedUtc && match.winner !== null && match.winner !== undefined && match.winner !== (match.player0 === l12State.nickname ? 0 : 1)).length)
const rankedWinRate = computed(() => {
  const profile = ranked.value?.profile
  return profile && profile.wins + profile.losses ? `${(profile.wins * 100 / (profile.wins + profile.losses)).toFixed(1)}%` : '0.0%'
})
function openBugFeedback() { (document.querySelector('.bug-feedback-trigger') as HTMLButtonElement | null)?.click() }
</script>

<template>
  <div class="profile-page">
    <header><small>PROFILE</small><h1>我的</h1><p>管理账号身份、牌库、战绩公开范围与数据文件。</p></header>
    <section class="identity"><div class="avatar">{{ (platformState.account?.username || '游').slice(0,1) }}</div><div><small>当前玩家</small><h2>{{ platformState.account?.username || '游客' }}</h2><span>{{ platformState.account ? `${platformState.account.role === 'admin' ? '管理员' : '玩家'} · ${l12State.status === 'online' ? '服务器在线' : '尚未连接对战服务'}` : '登录后同步牌库并进入对战' }}</span></div><div class="record-chip"><b>{{ ranked?.profile.placed ? ranked.profile.tier : myMatches.length }}</b><span>{{ ranked?.profile.placed ? `七曜值 ${ranked.profile.sevenValue.toLocaleString()}` : '已记录对局' }}</span></div><router-link v-if="canAccessAdmin" class="admin-button" to="/admin">⚙ 管理后台</router-link></section>
    <button class="feedback-banner" type="button" @click="openBugFeedback"><span><b>反馈 Bug 和建议</b><small>将当前页面与对局环境一并提交，方便准确复现问题。</small></span><i>进入反馈 →</i></button>
    <section v-if="ranked" class="rank-overview"><header><div><small>RANKED PROFILE</small><h2>本赛季排位</h2></div><router-link to="/battle/rankings">查看排行榜 →</router-link></header><div class="rank-body"><article><span>派系</span><b>{{ ranked.profile.faction || '尚未选择' }}</b></article><article><span>段位</span><b>{{ ranked.profile.rankLabel }}</b></article><article><span>七曜值</span><b>{{ ranked.profile.sevenValue.toLocaleString() }}</b></article><article><span>排位胜率</span><b>{{ rankedWinRate }}</b></article></div><div v-if="ranked.profile.titles.length" class="profile-titles"><span v-for="title in ranked.profile.titles" :key="title">✦ {{ title }}</span></div><p v-else>达到称号条件后会在这里展示派系与最强主宰称号。</p><section class="title-manager"><div><b>对战称号管理</b><span>对战中默认显示段位和1个最强主宰称号；若位列本派系第1至5名，则派系排名称号代替段位。</span></div><template v-if="ranked.profile.masterTitles.length"><select v-model="selectedMasterTitle"><option v-for="title in ranked.profile.masterTitles" :key="title" :value="title">{{ title }}</option></select><button :disabled="authBusy || selectedMasterTitle === (ranked.profile.selectedMasterTitle || '')" @click="saveRankedTitle">保存称号</button></template><em v-else>当前赛季尚未获得最强主宰称号</em></section></section>
    <section class="stats"><article><span>总场次</span><b>{{ myMatches.length }}</b></article><article><span>胜场</span><b>{{ wins }}</b></article><article><span>负场</span><b>{{ losses }}</b></article><article><span>胜率</span><b>{{ myMatches.length ? `${(wins / myMatches.length * 100).toFixed(1)}%` : '0.0%' }}</b></article></section>
    <section class="panel account-panel">
      <header><h2>账号与安全</h2><span>{{ platformState.account ? `${platformState.account.username} · ${platformState.account.role}` : '用户名与密码' }}</span></header>
      <template v-if="!platformState.account">
        <div class="auth-tabs"><button :class="{ active: authMode === 'login' }" @click="authMode = 'login'">登录</button><button :class="{ active: authMode === 'register' }" @click="authMode = 'register'">注册</button></div>
        <div class="account-form"><label>用户名<input v-model="auth.username" maxlength="20" autocomplete="username"/></label><label>密码<input v-model="auth.password" type="password" maxlength="128" :autocomplete="authMode === 'login' ? 'current-password' : 'new-password'"/></label><button class="primary" :disabled="authBusy" @click="submitAuth">{{ authMode === 'login' ? '登录' : '建立账号' }}</button></div>
        <router-link v-if="authMode === 'login' && emailFeatureEnabled" class="recovery-link" to="/auth/recovery">忘记密码？使用已验证邮箱找回</router-link>
      </template>
      <template v-else>
        <p v-if="platformState.account.mustChangePassword" class="password-required">管理员已重置此账号密码，必须修改密码。完成下方操作前，请勿继续使用临时密码。</p>
        <div class="account-form"><label>当前密码<input v-model="auth.currentPassword" type="password" autocomplete="current-password"/></label><label>新密码<input v-model="auth.newPassword" type="password" minlength="8" maxlength="128" autocomplete="new-password"/></label><button class="primary" :disabled="authBusy" @click="submitPassword">修改密码</button><button class="logout" :disabled="authBusy" @click="signOut">退出当前设备</button></div>
        <section v-if="emailFeatureEnabled" class="email-manager">
          <header><div><h3>邮箱与账号恢复</h3><p v-if="emailStatus?.verified">已验证：{{ emailStatus.maskedEmail }}</p><p v-else>尚未绑定已验证邮箱，忘记密码时无法找回。</p><small v-if="emailStatus?.pendingMaskedEmail">待验证：{{ emailStatus.pendingMaskedEmail }} · {{ new Date(emailStatus.pendingExpiresAt || '').toLocaleString() }} 前有效</small></div></header>
          <div class="email-form"><label>新邮箱 / 换绑邮箱<input v-model="emailForm.email" type="email" maxlength="254" autocomplete="email"/></label><label>当前密码<input v-model="emailForm.currentPassword" type="password" autocomplete="current-password"/></label><button :disabled="authBusy || !emailStatus?.mailConfigured" @click="submitEmailBinding">发送验证邮件</button><button v-if="emailStatus?.verified" class="danger" :disabled="authBusy" @click="unbindEmail">解绑邮箱</button></div>
          <small v-if="emailStatus && !emailStatus.mailConfigured" class="mail-unavailable">邮件服务尚未配置，绑定和换绑暂不可用。</small>
        </section>
        <section v-if="mfa?.enrollmentEnabled" class="mfa-boundary"><b>MFA 已启用</b><span>可使用已登记的验证器保护账号。</span></section>
        <section class="session-manager">
          <header><div><h3>登录设备与会话</h3><p>撤销后对应设备的令牌立即失效。</p></div><span class="session-actions"><button :disabled="authBusy || sessions.length <= 1" @click="revokeOtherSessions">退出其他设备</button><button class="danger" :disabled="authBusy || !sessions.length" @click="revokeAllSessions">退出全部设备</button></span></header>
          <article v-for="session in sessions" :key="session.id" class="session-row">
            <div><b>{{ session.current ? '当前设备' : '其他设备' }}</b><code>{{ session.id }}</code></div>
            <span>登录：{{ new Date(session.createdAt).toLocaleString() }}<small>到期：{{ new Date(session.expiresAt).toLocaleString() }} · {{ session.authStrength }}</small></span>
            <button :disabled="authBusy" @click="revokeSession(session)">{{ session.current ? '退出' : '撤销' }}</button>
          </article>
          <p v-if="!sessions.length" class="session-empty">暂无可显示的活动会话</p>
        </section>
      </template>
    </section>
    <div class="profile-grid"><section class="panel"><header><h2>公开设置</h2><span>账号偏好</span></header><div class="switch-row"><div><b>公开我的战绩</b><span>关闭后，其他玩家的个人页和公开榜单不展示你的个人对局列表。</span></div><button :class="{ on: publicHistory }" @click="publicHistory = !publicHistory">{{ publicHistory ? '已公开' : '不公开' }}</button></div></section><section class="panel links"><header><h2>数据与工具</h2></header><router-link to="/battle/records"><b>对局记录与 JSON 回放</b><span>导出、导入并在实战棋盘查看 →</span></router-link><router-link to="/decks"><b>我的牌库</b><span>账号牌库、牌库码与牌库图分享 →</span></router-link><router-link to="/battle/rankings"><b>排行榜</b><span>玩家榜、主宰榜与对阵矩阵 →</span></router-link></section></div>
    <p v-if="notice" class="notice">{{ notice }}</p>
  </div>
</template>

<style scoped>
.profile-page{min-height:100%;padding:30px clamp(18px,3vw,46px) 56px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.profile-page>header small{color:#52c3ca;font:900 9px monospace;letter-spacing:.18em}.profile-page>header h1{margin:5px 0;font-size:30px}.profile-page>header p{margin:0;color:#77858b;font-size:11px}.identity{display:grid;grid-template-columns:72px 1fr 160px auto;align-items:center;gap:18px;margin-top:22px;padding:22px;border:1px solid rgba(226,191,105,.35);background:linear-gradient(120deg,#111a24,#251318)}.avatar{display:grid;width:64px;height:64px;place-items:center;border:1px solid #e1c16c;border-radius:50%;background:#172831;color:#e4c674;font-size:25px;font-weight:900}.identity small,.identity h2,.identity span{display:block}.identity small{color:#78858b;font-size:9px}.identity h2{margin:5px 0;font-size:22px}.identity span{color:#8a969b;font-size:10px}.record-chip{padding:14px;border-left:1px solid #48545b}.record-chip b,.record-chip span{display:block}.record-chip b{font-size:21px;color:#e2c372}.admin-button{padding:11px 16px;border:1px solid #e1c16c;background:#e1c16c;color:#101214;font-size:11px;font-weight:900;text-decoration:none;white-space:nowrap}.feedback-banner{display:flex;width:100%;align-items:center;justify-content:space-between;margin-top:12px;padding:16px 20px;border:1px solid #8e2543;background:linear-gradient(90deg,#2a0e1a,#15121d);color:#fff;text-align:left}.feedback-banner span,.feedback-banner b,.feedback-banner small{display:block}.feedback-banner b{color:#ff87a8;font-size:14px}.feedback-banner small{margin-top:4px;color:#987987}.feedback-banner i{color:#ef9fb6;font-size:10px;font-style:normal;font-weight:900}.rank-overview{margin-top:12px;padding:20px;border:1px solid #6330a0;background:linear-gradient(135deg,#151027,#20113a)}.rank-overview>header{display:flex;align-items:flex-end;justify-content:space-between;border-bottom:1px solid #503078;padding-bottom:12px}.rank-overview h2{margin:4px 0 0}.rank-overview header small{color:#9b6ce0;font:900 9px monospace;letter-spacing:.16em}.rank-overview a{color:#cba6ff;font-size:10px;font-weight:900;text-decoration:none}.rank-body{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin-top:12px}.rank-body article{padding:13px;border:1px solid #4d3470;background:#100c1c}.rank-body span,.rank-body b{display:block}.rank-body span{color:#8f80a2;font-size:9px}.rank-body b{margin-top:5px;color:#eadbff;font-size:16px}.profile-titles{display:flex;flex-wrap:wrap;gap:8px;margin-top:12px}.profile-titles span{padding:6px 10px;border:1px solid #e4ad3f;border-radius:4px;background:linear-gradient(135deg,#a56b13,#3e2204);color:#fff1a8;font-size:10px;font-weight:900;box-shadow:0 0 12px #d98d2f55}.rank-overview>p{margin:12px 0 0;color:#8c7d9d;font-size:9px}.stats{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin:12px 0}.stats article{padding:16px;border:1px solid #35424a;background:#101821}.stats span,.stats b{display:block}.stats span{color:#748188;font-size:9px}.stats b{margin-top:5px;font-size:21px}.stats article:last-child{border-color:#7d2530;background:#241318}.stats article:last-child b{color:#e4c06d}.profile-grid{display:grid;grid-template-columns:1.2fr 1fr;gap:12px}.panel{padding:20px;border:1px solid #35424a;background:#101821}.panel>header{display:flex;align-items:center;justify-content:space-between;padding-bottom:13px;border-bottom:1px solid #35424a}.panel h2{margin:0;font-size:18px}.panel header span{color:#69767d;font-size:9px}.panel label{display:block;margin:18px 0;color:#aab2b5;font-size:10px;font-weight:900}.panel input{display:block;width:100%;margin-top:8px;padding:11px;border:1px solid #4b5860;background:#080e13;color:#fff}.switch-row{display:flex;align-items:center;justify-content:space-between;gap:20px;padding:14px 0;border-top:1px solid rgba(235,230,216,.08);border-bottom:1px solid rgba(235,230,216,.08)}.switch-row b,.switch-row span{display:block}.switch-row span{max-width:430px;margin-top:4px;color:#748087;font-size:9px;line-height:1.6}.switch-row button{min-width:100px;padding:9px;border:1px solid #536068;background:#0b1218;color:#90999e;font-weight:900}.switch-row button.on{border-color:#58c398;color:#7ae0b5}.primary{display:block;margin:16px 0 0 auto;padding:10px 18px;border:1px solid #e1c16c;background:#e1c16c;color:#080b0d;font-weight:900}.links a{display:block;padding:15px 2px;border-bottom:1px solid rgba(235,230,216,.09);color:#eeeae1;text-decoration:none}.links b,.links span{display:block}.links span{margin-top:5px;color:#758289;font-size:9px}.links a:hover span{color:#58c5cc}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584;font-size:10px}
@media(max-width:760px){.profile-page{padding:20px 12px 48px}.identity{grid-template-columns:60px 1fr}.record-chip{grid-column:1/-1;border-left:0;border-top:1px solid #48545b}.admin-button{grid-column:1/-1;text-align:center}.rank-body,.stats{grid-template-columns:1fr 1fr}.profile-grid{grid-template-columns:1fr}.switch-row{align-items:flex-start;flex-direction:column}.switch-row button{width:100%}.feedback-banner{align-items:flex-start;flex-direction:column;gap:9px}}
.account-panel{margin-bottom:12px}.auth-tabs{display:grid;grid-template-columns:1fr 1fr;gap:7px;margin-top:16px}.auth-tabs button{padding:10px;border:1px solid #46535b;background:#080e13;color:#879197;font-weight:900}.auth-tabs button.active{border-color:#e1c16c;background:#2a2414;color:#f2d985}.account-form{display:grid;grid-template-columns:1fr 1fr auto auto;align-items:end;gap:10px}.account-form label{margin:14px 0 0}.account-form .primary{margin:0}.logout{padding:10px 16px;border:1px solid #7e3c45;background:#2b1116;color:#eab5bb;font-weight:900}.admin-link{align-self:center;color:#e1c16c;font-size:11px;font-weight:900;text-decoration:none}@media(max-width:900px){.account-form{grid-template-columns:1fr 1fr}.account-form .primary,.account-form .logout,.account-form .admin-link{width:100%}}
.session-manager{grid-column:1/-1;margin-top:18px;border-top:1px solid #35424a;padding-top:16px}.session-manager>header{display:flex;align-items:center;justify-content:space-between;gap:12px}.session-manager h3{margin:0;font-size:14px}.session-manager p{margin:4px 0 0;color:#748188;font-size:9px}.session-actions{display:flex;gap:7px}.session-manager button{padding:8px 11px;border:1px solid #4b5960;background:#0a1117;color:#d8deda;font-weight:900}.session-manager button.danger,.session-row>button{border-color:#7e3c45;background:#2b1116;color:#eab5bb}.session-manager button:disabled{cursor:not-allowed;opacity:.42}.session-row{display:grid;grid-template-columns:minmax(180px,.8fr) 1fr auto;align-items:center;gap:12px;margin-top:8px;padding:10px;border:1px solid #303d44;background:#0a1117}.session-row b,.session-row code,.session-row small{display:block}.session-row code{margin-top:3px;color:#8e9ba0;font-size:8px;overflow-wrap:anywhere}.session-row span{color:#c0c8c7;font-size:9px}.session-row small{margin-top:3px;color:#718087}.session-empty{text-align:center}@media(max-width:760px){.session-manager>header{align-items:flex-start;flex-direction:column}.session-actions{width:100%}.session-actions button{flex:1}.session-row{grid-template-columns:1fr auto}.session-row>span{grid-column:1/-1;grid-row:2}}
.mfa-boundary{grid-column:1/-1;display:flex;flex-direction:column;gap:5px;margin-top:14px;padding:11px;border-left:3px solid #8a6b32;background:#20190d}.mfa-boundary span,.mfa-boundary small{color:#859197;font-size:9px}.mfa-boundary small{line-height:1.6}.session-manager{grid-column:1/-1;margin-top:18px;border-top:1px solid #35424a;padding-top:16px}.session-manager>header{display:flex;align-items:center;justify-content:space-between;gap:12px}.session-manager h3{margin:0;font-size:14px}.session-manager p{margin:4px 0 0;color:#748188;font-size:9px}.session-actions{display:flex;gap:7px}.session-manager button{padding:8px 11px;border:1px solid #4b5960;background:#0a1117;color:#d8deda;font-weight:900}.session-manager button.danger,.session-row>button{border-color:#7e3c45;background:#2b1116;color:#eab5bb}.session-manager button:disabled{cursor:not-allowed;opacity:.42}.session-row{display:grid;grid-template-columns:minmax(180px,.8fr) 1fr auto;align-items:center;gap:12px;margin-top:8px;padding:10px;border:1px solid #303d44;background:#0a1117}.session-row b,.session-row code,.session-row small{display:block}.session-row code{margin-top:3px;color:#8e9ba0;font-size:8px;overflow-wrap:anywhere}.session-row span{color:#c0c8c7;font-size:9px}.session-row small{margin-top:3px;color:#718087}.session-empty{text-align:center}@media(max-width:760px){.session-manager>header{align-items:flex-start;flex-direction:column}.session-actions{width:100%}.session-actions button{flex:1}.session-row{grid-template-columns:1fr auto}.session-row>span{grid-column:1/-1;grid-row:2}}
.recovery-link{display:inline-block;margin-top:12px;color:#70cbd2;font-size:10px;text-decoration:none}.password-required{padding:10px;border-left:3px solid #d96b72;background:#281217;color:#f0a4aa!important}.email-manager{grid-column:1/-1;margin-top:18px;border-top:1px solid #35424a;padding-top:16px}.email-manager h3{margin:0;font-size:14px}.email-manager p,.email-manager small{margin:4px 0;color:#7f8c91;font-size:9px}.email-form{display:grid;grid-template-columns:1fr 1fr auto auto;align-items:end;gap:8px}.email-form label{margin:12px 0 0}.email-form button{padding:10px;border:1px solid #4b5960;background:#0a1117;color:#d8deda;font-weight:900}.email-form button.danger{border-color:#7e3c45;background:#2b1116;color:#eab5bb}.email-form button:disabled{opacity:.45}.mail-unavailable{display:block;margin-top:8px!important;color:#d9a46d!important}@media(max-width:900px){.email-form{grid-template-columns:1fr 1fr}.email-form button{width:100%}}
.title-manager{display:grid;grid-template-columns:minmax(0,1fr) minmax(180px,260px) auto;align-items:center;gap:10px;margin-top:14px;padding:14px;border:1px solid #6a4a91;background:#100b1c}.title-manager b,.title-manager span{display:block}.title-manager span{margin-top:4px;color:#9a8aaa;font-size:9px;line-height:1.6}.title-manager select,.title-manager button{padding:10px;border:1px solid #8062a8;background:#090611;color:#eee2ff;font-weight:900}.title-manager button{border-color:#d5af55;background:#33260c;color:#f1d67d}.title-manager button:disabled{opacity:.45}.title-manager em{color:#8c7d9d;font-size:10px;font-style:normal}@media(max-width:760px){.title-manager{grid-template-columns:1fr}}
</style>
