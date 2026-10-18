<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { connect, createTutorial, l12State } from '@/l12/net'
import { platformState } from '@/l12/platform'
import type { TutorialLessonId } from '@/l12/types'
import { loadTutorialProgress } from '@/l12/tutorial/tutorialProgress'

const router = useRouter()
const lessons: Array<{ id: TutorialLessonId; order: number; title: string; summary: string; tasks: string[] }> = [
  { id: 'deploy', order: 1, title: '部署与支付', summary: '从手牌部署 1 费《西施》，并完成费用支付。',
    tasks: ['选择手牌中的《西施》', '选择亮起的空阵地', '在支付提示中确认费用'] },
  { id: 'move', order: 2, title: '普通移动', summary: '移动后排中格的《西施》，支付费用并进入相邻空格。',
    tasks: ['选择后排中格的《西施》', '使用普通移动', '选择亮起的相邻空格'] },
  { id: 'combat', order: 3, title: '进攻与防守', summary: '完成一次进攻，再在对方回合处理真实防守选择。',
    tasks: ['用前排左格军团进攻对方主宰', '结束回合', '选择抵挡或不抵挡'] },
]
const completed = ref<TutorialLessonId[]>([])
const starting = ref<TutorialLessonId | null>(null)
const connecting = ref(false)
let identityEpoch = 0

const accountId = computed(() => platformState.account?.id || '')
const accountAligned = computed(() => Boolean(accountId.value) && l12State.accountId === accountId.value)
const available = computed(() => l12State.status === 'online' && l12State.tutorialAvailable && accountAligned.value)
const roomBusy = computed(() => Boolean(l12State.room || l12State.game || l12State.leavingRoom))
const matchmakingBusy = computed(() => l12State.matchmaking?.queued === true)
const actionBusy = computed(() => Boolean(starting.value || l12State.pendingAction || roomBusy.value || matchmakingBusy.value))
const availabilityCopy = computed(() => connecting.value || l12State.status === 'connecting'
  ? '正在准备教学…'
  : l12State.status === 'online' && !accountAligned.value ? '账号刚刚切换，请刷新页面后开始教学。'
  : !l12State.tutorialAvailable ? '教学暂未开放' : l12State.status !== 'online'
    ? '教学暂时无法连接' : '三关可任选，也可以重复练习。')

function refreshProgress() {
  completed.value = accountId.value ? loadTutorialProgress(accountId.value) : []
}

watch(() => [platformState.account?.id || '', platformState.token] as const, () => {
  identityEpoch++
  starting.value = null
  refreshProgress()
}, { immediate: true, flush: 'sync' })

watch(() => [l12State.pendingAction, l12State.room?.roomCode || '', l12State.game?.matchId || ''] as const,
  ([pending, roomCode, matchId]) => {
    if (!pending && !roomCode && !matchId) starting.value = null
  })

async function ensureConnected(epoch: number, expectedAccountId: string, expectedToken: string) {
  if (l12State.status === 'online') return l12State.accountId === expectedAccountId
  connecting.value = true
  try { await connect() }
  catch { return false }
  finally { if (epoch === identityEpoch) connecting.value = false }
  return epoch === identityEpoch && accountId.value === expectedAccountId && platformState.token === expectedToken
}

async function startLesson(lessonId: TutorialLessonId) {
  if (actionBusy.value) return
  const epoch = identityEpoch
  const expectedAccountId = accountId.value
  const expectedToken = platformState.token
  if (!expectedAccountId || !expectedToken) { void router.push('/me'); return }
  starting.value = lessonId
  const connected = await ensureConnected(epoch, expectedAccountId, expectedToken)
  if (!connected || epoch !== identityEpoch || accountId.value !== expectedAccountId
    || platformState.token !== expectedToken) { if (epoch === identityEpoch) starting.value = null; return }
  if (!available.value || !createTutorial(lessonId)) starting.value = null
}

onMounted(() => {
  const epoch = identityEpoch
  const expectedAccountId = accountId.value
  const expectedToken = platformState.token
  if (expectedAccountId && expectedToken && l12State.status === 'offline')
    void ensureConnected(epoch, expectedAccountId, expectedToken)
})
</script>

<template>
  <main class="tutorial-page">
    <header>
      <div><small>NEW PLAYER TUTORIAL</small><h1>新手教学</h1><p>跟着提示，练习部署、移动和攻防。</p></div>
      <router-link to="/battle">← 返回对战大厅</router-link>
    </header>
    <section class="tutorial-status" :class="{ available }" role="status"><i/><span>{{ availabilityCopy }}</span></section>
    <section class="lesson-grid" aria-label="新手教学关卡">
      <article v-for="lesson in lessons" :key="lesson.id" :class="{ completed: completed.includes(lesson.id) }">
        <header><span>第 {{ lesson.order }} 关</span><b v-if="completed.includes(lesson.id)">已完成</b><b v-else>可练习</b></header>
        <h2>{{ lesson.title }}</h2><p>{{ lesson.summary }}</p>
        <ul><li v-for="task in lesson.tasks" :key="task">{{ task }}</li></ul>
        <button type="button" :disabled="!available || actionBusy" @click="startLesson(lesson.id)">
          {{ starting === lesson.id ? '正在建立教学…' : completed.includes(lesson.id) ? '重新练习' : '开始本关' }}
        </button>
      </article>
    </section>
    <section class="future-lessons" aria-disabled="true"><small>后续课程</small><h2>其余课程正在准备</h2><p>完成后会在这里开放。</p></section>
    <p v-if="l12State.notice" class="tutorial-notice" role="alert">{{ l12State.notice }}</p>
    <p v-if="matchmakingBusy" class="room-wait" role="status">请先取消当前匹配，再开始教学。</p>
    <p v-if="roomBusy" class="room-wait" role="status">请先结束并退出当前对局，再开始教学。</p>
    <button v-if="l12State.room && l12State.game" class="return-game" type="button" @click="router.push('/game')">返回当前对局</button>
  </main>
</template>

<style scoped>
.tutorial-page{box-sizing:border-box;width:min(1120px,calc(100% - 36px));min-height:100%;margin:0 auto;padding:34px 0 70px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.tutorial-page>header{display:flex;align-items:flex-start;justify-content:space-between;gap:24px}.tutorial-page small{color:#54c5cc;font-size:13px;font-weight:900;letter-spacing:.14em}.tutorial-page h1{margin:6px 0;font-size:32px}.tutorial-page>header p{max-width:720px;margin:0;color:#89969c;line-height:1.7}.tutorial-page a{color:#e2c66f;font-size:14px;font-weight:900;text-decoration:none}.tutorial-status{display:flex;align-items:center;gap:10px;margin:22px 0;padding:12px 14px;border:1px solid #6b4048;background:#211117;color:#e5a7ae;font-weight:900}.tutorial-status.available{border-color:#356d5a;background:#0d211a;color:#80d4ad}.tutorial-status i{width:9px;height:9px;border-radius:50%;background:currentColor;box-shadow:0 0 9px currentColor}.lesson-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px}.lesson-grid article,.future-lessons{display:flex;min-width:0;flex-direction:column;padding:22px;border:1px solid #35434c;background:#101821;box-shadow:0 16px 38px #0003}.lesson-grid article.completed{border-color:#7b6a35}.lesson-grid article>header{display:flex;align-items:center;justify-content:space-between;gap:10px}.lesson-grid article>header span{color:#65cbd1;font-size:14px;font-weight:900}.lesson-grid article>header b{color:#9ca8ad;font-size:13px}.lesson-grid article.completed>header b{color:#e3ca78}.lesson-grid h2,.future-lessons h2{margin:12px 0 8px}.lesson-grid p,.future-lessons p{margin:0;color:#89969c;font-size:14px;line-height:1.65}.lesson-grid ul{flex:1;margin:18px 0;padding-left:20px;color:#c6cecf;font-size:14px;line-height:1.8}.lesson-grid button,.return-game{min-height:44px;padding:10px 16px;border:1px solid #e0c36d;background:#e0c36d;color:#0a0e10;font-family:inherit;font-weight:900}.lesson-grid button:disabled{border-color:#44515a;background:#222c33;color:#77838a;cursor:not-allowed}.future-lessons{margin-top:16px;opacity:.78}.tutorial-notice,.room-wait{margin:16px 0 0;padding:12px;background:#211117;color:#e5a7ae;font-size:14px}.tutorial-notice{border-left:3px solid #b96772}.room-wait{border-left:3px solid #d1b55f;background:#211b0d;color:#d9c577}.return-game{margin-top:14px}
@media(max-width:880px){.lesson-grid{grid-template-columns:1fr}.tutorial-page{width:min(720px,calc(100% - 24px));padding-top:22px}.tutorial-page>header{align-items:stretch;flex-direction:column}.tutorial-page h1{font-size:27px}}
</style>
