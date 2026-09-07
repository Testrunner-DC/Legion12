<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { l12State } from '../net'
import { friendApi } from '../platform'
import { reportOpponent, requestMatchDraw, resolveMatchDraw } from '../matchGovernance'
import FriendsPage from '../site/FriendsPage.vue'

defineEmits<{ settings: [] }>()

const showFriends = ref(false)
const showTools = ref(false)
const responseOpen = ref(false)
const toolView = ref<'menu' | 'draw' | 'report'>('menu')
const drawReason = ref('')
const reportDescription = ref('')
const notice = ref('')
const busy = ref(false)
const pendingClientId = ref('')
let acknowledgementTimer: ReturnType<typeof window.setTimeout> | undefined

const governance = computed(() => l12State.game?.matchGovernance)
const drawRequest = computed(() => governance.value?.drawRequest ?? null)
const connection = computed(() => {
  if (l12State.connectionIssue === 'authentication') return '登录状态失效'
  if (l12State.connectionIssue === 'superseded') return '已由其他页面接管'
  if (l12State.connectionIssue === 'maintenance') return '维护中'
  if (l12State.status === 'connecting') return '连接恢复中'
  return l12State.status === 'online' ? '连接正常' : '对战连接中断'
})

watch(() => drawRequest.value?.viewerCanRespond ? drawRequest.value.id : '', id => {
  if (id) responseOpen.value = true
}, { immediate: true })

watch(() => l12State.matchGovernanceResult, result => {
  if (!result || (pendingClientId.value && result.clientRequestId !== pendingClientId.value)) return
  notice.value = result.message
  busy.value = false
  if (result.action === 'report-opponent' && result.status === 'submitted') {
    reportDescription.value = ''
    toolView.value = 'menu'
  }
  if (result.action === 'request-draw' && result.status === 'pending') {
    drawReason.value = ''
    toolView.value = 'menu'
  }
  if (result.action === 'resolve-draw' && (result.status === 'accepted' || result.status === 'rejected'))
    responseOpen.value = false
  pendingClientId.value = ''
  if (acknowledgementTimer) window.clearTimeout(acknowledgementTimer)
  acknowledgementTimer = undefined
})

watch(() => l12State.status, status => {
  if (!busy.value || !pendingClientId.value || status === 'online') return
  busy.value = false
  pendingClientId.value = ''
  if (acknowledgementTimer) window.clearTimeout(acknowledgementTimer)
  acknowledgementTimer = undefined
  notice.value = '对战连接已中断；恢复连接后请根据权威状态重试'
})

function awaitAcknowledgement(clientRequestId: string) {
  if (acknowledgementTimer) window.clearTimeout(acknowledgementTimer)
  acknowledgementTimer = window.setTimeout(() => {
    if (pendingClientId.value !== clientRequestId) return
    pendingClientId.value = ''
    busy.value = false
    notice.value = '服务器未确认此操作；请先查看权威对局状态，再安全重试'
  }, 12_000)
}

function closeOverlays() {
  showFriends.value = false
  showTools.value = false
  responseOpen.value = false
}
function keydown(event: KeyboardEvent) {
  if (event.key !== 'Escape') return
  if (responseOpen.value) responseOpen.value = false
  else closeOverlays()
}
onMounted(() => window.addEventListener('keydown', keydown))
onBeforeUnmount(() => {
  window.removeEventListener('keydown', keydown)
  if (acknowledgementTimer) window.clearTimeout(acknowledgementTimer)
})

function openBugFeedback() {
  showTools.value = false
  window.dispatchEvent(new CustomEvent('l12-open-bug-feedback'))
}

async function blockOpponent() {
  const accountId = governance.value?.opponentAccountId
  const name = governance.value?.opponentName || '对手'
  if (!accountId || !window.confirm(`屏蔽「${name}」后将不再接受其好友申请；不会影响当前对局或匹配。确定继续吗？`)) return
  busy.value = true
  notice.value = ''
  try { notice.value = (await friendApi.block(accountId)).message }
  catch (error) { notice.value = error instanceof Error ? error.message : '屏蔽失败' }
  finally { busy.value = false }
}

function submitDrawRequest() {
  const reason = drawReason.value.trim()
  if (!reason) { notice.value = '请输入所出现的Bug给对手申请平局'; return }
  if (l12State.status !== 'online') { notice.value = '对战连接未就绪，请等待恢复后重试'; return }
  if (!governance.value?.canRequestDraw) {
    notice.value = governance.value?.drawUnavailableReason || '当前对局不能申请平局'
    return
  }
  busy.value = true
  notice.value = '正在等待服务器确认…'
  pendingClientId.value = requestMatchDraw(reason)
  awaitAcknowledgement(pendingClientId.value)
}

function submitPlayerReport() {
  const description = reportDescription.value.trim()
  if (!description) { notice.value = '请输入举报内容'; return }
  if (l12State.status !== 'online') { notice.value = '对战连接未就绪，请等待恢复后重试'; return }
  if (!governance.value?.canReportOpponent) {
    notice.value = governance.value?.reportUnavailableReason || '当前无法举报对手'
    return
  }
  busy.value = true
  notice.value = '正在等待服务器确认…'
  pendingClientId.value = reportOpponent(description)
  awaitAcknowledgement(pendingClientId.value)
}

function answerDraw(accept: boolean) {
  const request = drawRequest.value
  if (!request?.viewerCanRespond || busy.value) return
  if (l12State.status !== 'online') { notice.value = '对战连接未就绪，请等待恢复后重试'; return }
  busy.value = true
  notice.value = '正在等待服务器确认…'
  pendingClientId.value = request.id
  resolveMatchDraw(request.id, accept)
  awaitAcknowledgement(request.id)
}
</script>

<template>
  <aside class="battle-utility-dock" aria-label="对局通用功能">
    <button type="button" title="设置" aria-label="打开对局设置" @click="$emit('settings')">
      <svg aria-hidden="true" viewBox="0 0 24 24"><path d="M12 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7Zm8.2 4.9v-2.8l-2-.7a7 7 0 0 0-.7-1.6l.9-1.9-2-2-1.9.9a7 7 0 0 0-1.6-.7l-.7-2H9.4l-.7 2a7 7 0 0 0-1.6.7l-1.9-.9-2 2 .9 1.9a7 7 0 0 0-.7 1.6l-2 .7v2.8l2 .7c.2.6.4 1.1.7 1.6l-.9 1.9 2 2 1.9-.9c.5.3 1 .5 1.6.7l.7 2h2.8l.7-2c.6-.2 1.1-.4 1.6-.7l1.9.9 2-2-.9-1.9c.3-.5.5-1 .7-1.6l2-.7Z"/></svg>
    </button>
    <button type="button" title="好友" aria-label="打开好友功能" @click="showFriends = true">
      <svg aria-hidden="true" viewBox="0 0 24 24"><path d="M8.2 11.2a4.1 4.1 0 1 0 0-8.2 4.1 4.1 0 0 0 0 8.2Zm7.7-.9a3.2 3.2 0 1 0 0-6.4 3.2 3.2 0 0 0 0 6.4ZM1.8 20.8h12.8v-2.1c0-3.5-2.9-6.3-6.4-6.3s-6.4 2.8-6.4 6.3v2.1Zm13.8 0h6.6v-1.7c0-3.1-2.2-5.7-5.2-6.2a7.8 7.8 0 0 1-1.4 7.9Z"/></svg>
    </button>
    <button type="button" title="对局工具" aria-label="打开对局工具" @click="showTools = true; toolView = 'menu'">
      <svg aria-hidden="true" viewBox="0 0 24 24"><circle cx="5" cy="12" r="2.1"/><circle cx="12" cy="12" r="2.1"/><circle cx="19" cy="12" r="2.1"/></svg>
    </button>
  </aside>

  <Teleport to="body">
    <div v-if="showFriends" class="battle-modal-mask" @click.self="showFriends = false">
      <section class="battle-dialog friends-shell" role="dialog" aria-modal="true" aria-labelledby="battle-friends-title">
        <header><h2 id="battle-friends-title">好友</h2><button type="button" aria-label="关闭好友功能" @click="showFriends = false">×</button></header>
        <FriendsPage />
      </section>
    </div>

    <div v-if="showTools" class="battle-modal-mask" @click.self="showTools = false">
      <section class="battle-dialog tools-dialog" role="dialog" aria-modal="true" aria-labelledby="battle-tools-title">
        <header><div><small>MATCH TOOLS</small><h2 id="battle-tools-title">对局工具</h2></div><button type="button" aria-label="关闭对局工具" @click="showTools = false">×</button></header>
        <p class="connection" role="status"><i :class="l12State.status"/>{{ connection }}</p>

        <div v-if="toolView === 'menu'" class="tool-menu">
          <button type="button" @click="openBugFeedback">Bug反馈<span>打开普通反馈入口</span></button>
          <button type="button" :disabled="!governance?.canRequestDraw || busy" @click="toolView = 'draw'; notice = ''">申请平局<span>{{ governance?.drawUnavailableReason || '本局双方合计仅可申请一次' }}</span></button>
          <button type="button" :disabled="!governance?.opponentAccountId || busy" @click="blockOpponent">屏蔽对手<span>仅屏蔽好友申请，不影响本局或匹配</span></button>
          <button type="button" :disabled="!governance?.canReportOpponent || busy" @click="toolView = 'report'; notice = ''">举报对手<span>{{ governance?.reportUnavailableReason || '独立提交至对局治理' }}</span></button>
          <button v-if="drawRequest?.viewerCanRespond" type="button" class="attention" @click="responseOpen = true">处理平局申请<span>{{ drawRequest.requesterName }} 正在等待答复</span></button>
          <p v-else-if="drawRequest?.status === 'pending'">平局申请等待 {{ drawRequest.responderName }} 处理。</p>
        </div>

        <form v-else-if="toolView === 'draw'" @submit.prevent="submitDrawRequest">
          <label>申请原因<textarea v-model="drawReason" maxlength="1000" rows="5" placeholder="输入所出现的Bug给对手申请平局"/></label>
          <p>真实双人进行中对局可申请，排位同样允许；每场对局双方合计仅可发起一次，无论接受或拒绝都不能再次申请。对方仍可响应已经收到的申请。</p>
          <footer><button type="button" @click="toolView = 'menu'">返回</button><button class="primary" :disabled="busy || !drawReason.trim()" type="submit">{{ busy ? '提交中…' : '发送申请' }}</button></footer>
        </form>

        <form v-else @submit.prevent="submitPlayerReport">
          <label>举报内容<textarea v-model="reportDescription" maxlength="5000" rows="7" placeholder="请描述对手行为、发生时间与可核查细节" required/></label>
          <p>举报将独立进入“对局治理”，不会混入普通Bug反馈。</p>
          <footer><button type="button" @click="toolView = 'menu'">返回</button><button class="primary" :disabled="busy || !reportDescription.trim()" type="submit">{{ busy ? '提交中…' : '提交举报' }}</button></footer>
        </form>
        <p v-if="notice" class="notice" role="status">{{ notice }}</p>
      </section>
    </div>

    <div v-if="responseOpen && drawRequest?.viewerCanRespond" class="battle-modal-mask response-mask">
      <section class="battle-dialog response-dialog" role="alertdialog" aria-modal="true" aria-labelledby="draw-response-title" aria-describedby="draw-response-reason">
        <header><div><small>DRAW REQUEST</small><h2 id="draw-response-title">{{ drawRequest.requesterName }} 申请平局</h2></div><button type="button" aria-label="暂时关闭平局申请" @click="responseOpen = false">×</button></header>
        <p id="draw-response-reason" class="draw-reason">{{ drawRequest.reason }}</p>
        <p>接受后服务器将权威结束本局为平局；拒绝则继续对局。</p>
        <footer><button type="button" :disabled="busy" @click="answerDraw(false)">拒绝并继续</button><button type="button" class="primary" :disabled="busy" @click="answerDraw(true)">接受平局</button></footer>
        <p v-if="notice" class="notice" role="status">{{ notice }}</p>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.battle-utility-dock{position:relative;z-index:1;display:grid;box-sizing:border-box;width:100%;height:60px;grid-template-columns:repeat(3,1fr);border:1px solid #53616a;background:#080d11ed;box-shadow:0 8px 24px #000;font-size:max(14px,var(--l12-board-readable,14px))}.battle-utility-dock button{display:grid;min-width:0;height:58px;place-items:center;border:0;border-left:1px solid #53616a;background:#101b23;color:#e9d285}.battle-utility-dock button:first-child{border-left:0}.battle-utility-dock button:hover,.battle-utility-dock button:focus-visible{background:#1c303c;color:#fff;outline:1px solid #e9d285;outline-offset:-2px}.battle-utility-dock svg{width:24px;height:24px;fill:currentColor}.battle-modal-mask{position:fixed;z-index:4700;inset:0;display:grid;place-items:center;padding:18px;background:#010407cf;backdrop-filter:blur(8px);font-family:'Microsoft YaHei','微软雅黑',sans-serif;font-size:14px}.battle-dialog{box-sizing:border-box;width:min(600px,96vw);max-height:90vh;overflow:auto;border:1px solid #647179;background:#101820;color:#f3f0e8;box-shadow:0 30px 90px #000}.battle-dialog>header{position:sticky;z-index:2;top:0;display:flex;align-items:center;justify-content:space-between;gap:18px;padding:16px 18px;border-bottom:1px solid #38464e;background:#101820}.battle-dialog h2{margin:2px 0;font-size:23px}.battle-dialog small{color:#d5b85e;font:900 14px monospace;letter-spacing:.14em}.battle-dialog>header>button{flex:none;width:35px;height:35px;border:1px solid #52616a;background:#080d11;color:#fff;font-size:22px}.friends-shell{width:min(1100px,96vw)}.friends-shell :deep(.friends-page){box-sizing:border-box;width:100%;min-height:0;padding:18px}.tools-dialog{padding-bottom:18px}.connection{display:flex;align-items:center;gap:8px;margin:14px 18px;color:#aeb9bd}.connection i{width:8px;height:8px;border-radius:50%;background:#b66572}.connection i.online{background:#60d3a4}.connection i.connecting{background:#e5c66e}.tool-menu{display:grid;gap:8px;padding:0 18px}.tool-menu>button{display:flex;align-items:center;justify-content:space-between;gap:18px;min-height:56px;padding:10px 13px;border:1px solid #485760;background:#111f28;color:#fff;font:900 15px 'Microsoft YaHei','微软雅黑';text-align:left}.tool-menu>button span{color:#89979d;font-size:14px;font-weight:400;text-align:right}.tool-menu>button:disabled{opacity:.45}.tool-menu>button.attention{border-color:#d1b35c;background:#29220f;color:#f2d77f}.tool-menu>p{margin:4px 0;padding:10px;border-left:3px solid #d1b35c;background:#211c0f;color:#dac882;font-size:14px}.tools-dialog form{padding:0 18px}.tools-dialog label{display:block;color:#d8dcda;font-size:14px;font-weight:900}.tools-dialog textarea{box-sizing:border-box;width:100%;margin-top:8px;padding:12px;border:1px solid #4a5961;background:#070d12;color:#fff;font:700 14px 'Microsoft YaHei','微软雅黑';resize:vertical}.tools-dialog form>p,.response-dialog>p{color:#8f9ca1;font-size:14px;line-height:1.7}.battle-dialog footer{display:flex;justify-content:flex-end;gap:9px;margin-top:16px}.battle-dialog footer button{min-width:120px;padding:10px 12px;border:1px solid #56646b;background:#111b22;color:#fff;font:900 14px 'Microsoft YaHei','微软雅黑'}.battle-dialog footer button.primary{border-color:#d4b85f;background:#d4b85f;color:#111}.battle-dialog footer button:disabled{opacity:.5}.notice{margin:14px 18px 0;padding:9px;border-left:3px solid #d3b75f;background:#221c0e;color:#ead080;font-size:14px}.response-mask{z-index:4900}.response-dialog{padding-bottom:18px}.response-dialog>p{margin:14px 18px}.response-dialog .draw-reason{padding:13px;border:1px solid #52616a;background:#081016;color:#fff;white-space:pre-wrap}.response-dialog footer{padding:0 18px}
@media(max-width:700px){.friends-shell :deep(.friends-page){padding:12px}.battle-modal-mask{padding:8px}.tool-menu>button{align-items:flex-start;flex-direction:column;gap:4px}.tool-menu>button span{text-align:left}}
.friends-shell :deep(.friends-page>header){display:none}
.tool-menu{grid-template-columns:repeat(2,minmax(0,1fr))}
.tool-menu>button{min-width:0;min-height:86px;align-items:flex-start;justify-content:center;flex-direction:column;gap:5px}
.tool-menu>button span{display:block;line-height:1.45;text-align:left;overflow-wrap:anywhere}
.tool-menu>button.attention,.tool-menu>p{grid-column:1/-1}
@media(max-width:700px){.tool-menu{grid-template-columns:1fr}.tool-menu>button.attention,.tool-menu>p{grid-column:auto}}
</style>
