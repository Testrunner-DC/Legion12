<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { connect, createTutorial, l12State } from '@/l12/net'
import { platformState } from '@/l12/platform'
import CardImage from '@/l12/CardImage.vue'
import type { TutorialLessonId } from '@/l12/types'
import { loadGuidedTutorialProgress, markGuidedIntroductionRead, restartGuidedTutorial } from '@/l12/tutorial/tutorialProgress'
import { tutorialChapters, tutorialIntroduction, tutorialScenes } from '@/l12/tutorial/tutorialCourse'

const props = withDefaults(defineProps<{ embedded?: boolean }>(), { embedded: false })
const router = useRouter()
const introductionIndex = ref(0)
const questionOpen = ref(false)
const introductionOpen = ref(true)
const progress = ref(loadGuidedTutorialProgress(''))
const starting = ref<TutorialLessonId | null>(null)
const connecting = ref(false)
const unsaved = ref(false)
let identityEpoch = 0

const accountId = computed(() => platformState.account?.id || '')
const accountAligned = computed(() => Boolean(accountId.value) && l12State.accountId === accountId.value)
const available = computed(() => l12State.status === 'online' && l12State.tutorialAvailable && accountAligned.value)
const roomBusy = computed(() => Boolean(l12State.room || l12State.game || l12State.leavingRoom))
const matchmakingBusy = computed(() => l12State.matchmaking?.queued === true)
const actionBusy = computed(() => Boolean(starting.value || l12State.pendingAction || roomBusy.value || matchmakingBusy.value))
const introduction = computed(() => tutorialIntroduction[introductionIndex.value]!)
const chapterComplete = computed(() => progress.value.introductionRead && progress.value.reviewedScenes.length === tutorialScenes.length)
const nextScene = computed(() => tutorialScenes[progress.value.reviewedScenes.length] || 'deploy')
const availabilityCopy = computed(() => connecting.value || l12State.status === 'connecting' ? '正在连接教学…'
  : l12State.status === 'online' && !accountAligned.value ? '账号刚刚切换，请刷新页面后开始教学。'
  : !l12State.tutorialAvailable ? '教学暂未开放' : l12State.status !== 'online' ? '教学暂时无法连接' : '')
const lastIntroduction = computed(() => introductionIndex.value === tutorialIntroduction.length - 1)

watch(() => [platformState.account?.id || '', platformState.token] as const, () => {
  identityEpoch++
  starting.value = null
  connecting.value = false
  questionOpen.value = false
  introductionIndex.value = 0
  progress.value = loadGuidedTutorialProgress(accountId.value)
  introductionOpen.value = !progress.value.introductionRead
  unsaved.value = false
}, { immediate: true, flush: 'sync' })

watch(() => [l12State.pendingAction, l12State.room?.roomCode || '', l12State.game?.matchId || ''] as const,
  ([pending, roomCode, matchId]) => { if (!pending && !roomCode && !matchId) starting.value = null })

async function ensureConnected(epoch: number, expectedAccountId: string, expectedToken: string) {
  if (l12State.status === 'online') return l12State.accountId === expectedAccountId
  connecting.value = true
  try { await connect() } catch { return false }
  finally { if (epoch === identityEpoch) connecting.value = false }
  return epoch === identityEpoch && accountId.value === expectedAccountId && platformState.token === expectedToken
}
async function startPractice() {
  if (actionBusy.value || questionOpen.value) return
  const epoch = identityEpoch
  const expectedAccountId = accountId.value
  const expectedToken = platformState.token
  if (!expectedAccountId || !expectedToken) { void router.push('/me'); return }
  if (!progress.value.introductionRead) {
    if (!lastIntroduction.value) return
    unsaved.value = !markGuidedIntroductionRead(expectedAccountId)
    progress.value = loadGuidedTutorialProgress(expectedAccountId)
    introductionOpen.value = false
  }
  const scene = nextScene.value
  starting.value = scene
  const connected = await ensureConnected(epoch, expectedAccountId, expectedToken)
  if (!connected || epoch !== identityEpoch || accountId.value !== expectedAccountId
    || platformState.token !== expectedToken) { if (epoch === identityEpoch) starting.value = null; return }
  if (!available.value || !createTutorial(scene)) starting.value = null
}
function continueIntroduction() {
  if (questionOpen.value) return
  if (lastIntroduction.value && chapterComplete.value) { introductionOpen.value = false; return }
  if (lastIntroduction.value) { void startPractice(); return }
  introductionIndex.value++
}
function repeatChapter() {
  if (actionBusy.value || !accountId.value) return
  unsaved.value = !restartGuidedTutorial(accountId.value)
  progress.value = loadGuidedTutorialProgress(accountId.value)
  introductionIndex.value = 0
  questionOpen.value = false
  introductionOpen.value = true
}
function revisitIntroduction() {
  introductionIndex.value = 0; questionOpen.value = false; introductionOpen.value = true
}
onMounted(() => {
  const epoch = identityEpoch; const expectedAccountId = accountId.value; const expectedToken = platformState.token
  if (expectedAccountId && expectedToken && l12State.status === 'offline')
    void ensureConnected(epoch, expectedAccountId, expectedToken)
})
</script>

<template>
  <component :is="props.embedded ? 'section' : 'main'" class="tutorial-page" :class="{ embedded: props.embedded }" aria-label="十二军团新手教学">
    <header class="tutorial-heading"><div><small>十二军团 · 新手教学</small><h1>初识十二军团</h1></div>
      <router-link v-if="!props.embedded" to="/battle">返回对战大厅</router-link>
    </header>
    <ol class="course-path" aria-label="教学主线"><li v-for="(chapter, index) in tutorialChapters" :key="chapter" :class="{ current: index === 0 }">
      <span>{{ chapter }}</span><small v-if="index > 0">尚未开放</small>
    </li></ol>

    <section v-if="introductionOpen" class="tutorial-conversation" data-tutorial-introduction="true" aria-live="polite">
      <div class="guide-speaker"><span aria-hidden="true">✦</span><b>向导</b><small>认识十二军团</small></div>
      <div class="intro-cards" v-if="!questionOpen && introduction.cards.length">
        <figure v-for="card in introduction.cards" :key="card.id"><CardImage :card-id="card.id" :alt="card.name" intent="detail" eager native-orientation /><figcaption>{{ card.name }} · {{ card.kind }}</figcaption></figure>
      </div>
      <h2>{{ questionOpen ? introduction.question : introduction.title }}</h2>
      <p>{{ questionOpen ? introduction.answer : introduction.text }}</p>
      <p v-if="!questionOpen && introduction.flow" class="resource-flow">{{ introduction.flow }}</p>
      <div class="conversation-choices">
        <button v-if="questionOpen" class="primary" type="button" @click="questionOpen = false">明白了，回到主线</button>
        <template v-else>
          <button class="primary" type="button" :disabled="lastIntroduction && (actionBusy || connecting)" @click="continueIntroduction">
            {{ starting ? '正在准备练习场面…' : lastIntroduction ? (chapterComplete ? '回到本章复盘' : accountId ? '进入战场，继续主线' : '登录后开始实操') : '继续主线' }}
          </button>
          <button type="button" @click="questionOpen = true">我想了解：{{ introduction.question }}</button>
        </template>
      </div>
    </section>

    <section v-else-if="!chapterComplete" class="tutorial-conversation" aria-live="polite">
      <div class="guide-speaker"><span aria-hidden="true">✦</span><b>向导</b><small>继续主线</small></div>
      <h2>我们接着刚才的学习</h2><p>接下来是{{ nextScene === 'deploy' ? '打出军团与支付士气' : nextScene === 'move' ? '普通移动' : '进攻与防守' }}。我会先介绍当前场面，再陪你一步步操作。</p>
      <div class="conversation-choices"><button class="primary" type="button" :disabled="actionBusy || connecting" @click="startPractice">{{ starting ? '正在准备练习场面…' : '继续主线' }}</button>
        <button type="button" :disabled="actionBusy" @click="revisitIntroduction">再认识一下卡牌</button></div>
    </section>
    <section v-else class="tutorial-conversation chapter-review" aria-live="polite">
      <div class="guide-speaker"><span aria-hidden="true">✦</span><b>向导</b><small>第一章复盘</small></div>
      <h2>现在，把刚才的行动串起来</h2>
      <p>你已经认识了获胜目标和各种卡牌，并实际打出军团、支付士气、移动、进攻和防守。做决定前，先看当前可用士气、军团位置与状态，再读卡牌效果。</p>
      <p>下一章会继续认识卡牌效果与响应，目前还在准备。你可以留在这里复习，也可以重新走一遍本章。</p>
      <div class="conversation-choices"><button type="button" class="primary" :disabled="actionBusy" @click="repeatChapter">重新学习本章</button><button type="button" @click="revisitIntroduction">再看卡牌介绍</button></div>
    </section>
    <p v-if="unsaved" class="tutorial-notice" role="status">本机暂时无法保存进度，当前学习仍可继续；刷新页面后可能需要重新学习。</p>
    <p v-if="accountId && availabilityCopy" class="tutorial-status" role="status">{{ availabilityCopy }}</p>
    <p v-if="l12State.notice" class="tutorial-notice" role="alert">{{ l12State.notice }}</p>
    <p v-if="matchmakingBusy" class="room-wait" role="status">请先取消当前匹配，再开始教学。</p>
    <p v-if="roomBusy" class="room-wait" role="status">请先结束并退出当前对局，再开始教学。</p>
    <button v-if="l12State.room && l12State.game" class="return-game" type="button" @click="router.push('/game')">返回当前对局</button>
  </component>
</template>

<style scoped>
.tutorial-page{box-sizing:border-box;width:min(1000px,calc(100% - 32px));margin:0 auto;padding:28px 0 64px;color:#e7ecea}.tutorial-page.embedded{width:100%;padding:6px 0 20px}.tutorial-heading{display:flex;justify-content:space-between;align-items:center;gap:18px}.tutorial-heading small{color:#67c9ce;font-size:13px;font-weight:800}.tutorial-heading h1{margin:6px 0 0;font-size:28px}.tutorial-heading a{color:#d6c27b;text-decoration:none;font-weight:800;min-height:44px;display:flex;align-items:center}.course-path{display:flex;gap:12px;list-style:none;margin:20px 0;padding:0}.course-path li{flex:1;border-bottom:2px solid #34434c;padding:10px 0;color:#86959c;display:flex;gap:8px;flex-wrap:wrap;align-items:center}.course-path .current{color:#e6cc80;border-color:#cfb970}.course-path small{font-size:12px}.tutorial-conversation{box-sizing:border-box;position:relative;padding:24px 28px;border:1px solid #697270;background:linear-gradient(130deg,#142129,#0b141b);box-shadow:0 16px 36px #0003}.guide-speaker{display:flex;align-items:center;gap:10px;color:#f0d58b}.guide-speaker>span{font-size:26px}.guide-speaker small{margin-left:auto;color:#91afb6;font-size:13px}.tutorial-conversation h2{font-size:23px;line-height:1.4;margin:16px 0 12px}.tutorial-conversation p{font-size:16px;line-height:1.9;margin:0 0 18px;max-width:760px;color:#d8e1df}.intro-cards{display:flex;justify-content:center;align-items:center;gap:18px;margin:18px 0}.intro-cards figure{display:flex;flex-direction:column;align-items:center;gap:8px;margin:0;width:min(220px,44%)}.intro-cards figure>:first-child{width:100%;height:240px;object-fit:contain}.intro-cards figcaption{font-size:14px;color:#dfc979;font-weight:800}.conversation-choices{display:flex;align-items:stretch;gap:10px;flex-wrap:wrap;margin-top:22px}.conversation-choices button,.return-game{min-height:46px;padding:11px 17px;border:1px solid #526875;background:#182831;color:#dae5e3;font-family:inherit;font-size:14px;font-weight:800;line-height:1.6;text-align:left}.conversation-choices .primary,.return-game{background:#dfc67a;color:#101619;border-color:#dfc67a}.conversation-choices button:disabled{opacity:.5;cursor:not-allowed}.tutorial-status,.tutorial-notice,.room-wait{padding:12px 14px;margin:16px 0 0;border-left:3px solid #bc9d59;background:#211c12;color:#dccb97;line-height:1.7;font-size:14px}.tutorial-notice[role=alert]{border-color:#bc6877;background:#27171c;color:#edb5c0}.return-game{margin-top:14px}.chapter-review p+p{color:#9eafb4}
@media(max-width:700px){.tutorial-page{width:calc(100% - 24px);padding-top:18px}.tutorial-conversation{padding:19px 17px}.tutorial-heading h1{font-size:24px}.course-path{gap:10px}.course-path li{font-size:13px}.tutorial-conversation h2{font-size:20px}.tutorial-conversation p{font-size:15px;line-height:1.8}.conversation-choices{flex-direction:column}.conversation-choices button{width:100%;text-align:center}.intro-cards figure>:first-child{height:200px}}
@media(max-height:480px) and (min-width:650px){.intro-cards{float:right;width:40%;margin:14px 0 14px 20px}.intro-cards figure{width:100%}.intro-cards figure>:first-child{height:170px}.tutorial-conversation{display:flow-root}.tutorial-conversation p{font-size:15px}.conversation-choices{clear:both}.course-path{margin:12px 0}}
.resource-flow{padding:10px 14px;border-left:3px solid #6dc3c8;background:#183039;color:#abdee0!important;font-weight:800}
</style>
