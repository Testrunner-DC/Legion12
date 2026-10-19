<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { connect, createTutorial, l12State } from '@/l12/net'
import { platformState } from '@/l12/platform'
import CardImage from '@/l12/CardImage.vue'
import CatalogCardDetails from '@/l12/CatalogCardDetails.vue'
import { masterProfileUrl } from '@/l12/specialAssets'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import type { TutorialLessonId } from '@/l12/types'
import { guidedTutorialPersistenceState, loadGuidedTutorialProgress, markGuidedIntroductionRead, restartGuidedTutorial, retryGuidedTutorialProgress } from '@/l12/tutorial/tutorialProgress'
import { tutorialChapters, tutorialIntroduction, tutorialScenes } from '@/l12/tutorial/tutorialCourse'

const props = withDefaults(defineProps<{ embedded?: boolean }>(), { embedded: false })
const router = useRouter()
const introductionIndex = ref(0)
const introductionOpen = ref(true)
const progress = ref(loadGuidedTutorialProgress(''))
const starting = ref<TutorialLessonId | null>(null)
const connecting = ref(false)
const unsaved = ref(false)
const catalog = ref<DeckCard[]>([])
const detailCard = ref<DeckCard | null>(null)
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
function showCardDetails(cardId: string) {
  detailCard.value = catalog.value.find(card => card.id === cardId) ?? null
  if (!detailCard.value) return
  void nextTick().then(() => {
    document.querySelectorAll('.tutorial-intro-detail-focus').forEach(element => element.classList.remove('tutorial-intro-detail-focus'))
    const dialog = document.querySelector('.catalog-detail-dialog')
    if (!dialog) return
    const labels = new Set(['费用', '兵力', '血量'])
    dialog.querySelectorAll('dt').forEach(term => {
      if (!labels.has(term.textContent?.trim() || '')) return
      term.classList.add('tutorial-intro-detail-focus')
      term.nextElementSibling?.classList.add('tutorial-intro-detail-focus')
    })
    dialog.querySelector('.archive-effect')?.classList.add('tutorial-intro-detail-focus')
    dialog.querySelector('.archive-effect.tutorial-intro-detail-focus')?.scrollIntoView({ block: 'nearest' })
  })
}
function closeCardDetails() {
  document.querySelectorAll('.tutorial-intro-detail-focus').forEach(element => element.classList.remove('tutorial-intro-detail-focus'))
  detailCard.value = null
}
function retryProgressSave() {
  const owner = accountId.value
  if (!owner) return
  retryGuidedTutorialProgress(owner)
  unsaved.value = guidedTutorialPersistenceState(owner).unsaved
  progress.value = loadGuidedTutorialProgress(owner)
}

watch(() => [platformState.account?.id || '', platformState.token] as const, () => {
  identityEpoch++
  starting.value = null
  connecting.value = false
  introductionIndex.value = 0
  progress.value = loadGuidedTutorialProgress(accountId.value)
  introductionOpen.value = !progress.value.introductionRead
  unsaved.value = guidedTutorialPersistenceState(accountId.value).unsaved
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
function startPractice() { return startPracticeScene(nextScene.value) }
function startResponsePractice() {
  if (!chapterComplete.value) return
  return startPracticeScene('response')
}
async function startPracticeScene(scene: TutorialLessonId) {
  if (actionBusy.value) return
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
  starting.value = scene
  const connected = await ensureConnected(epoch, expectedAccountId, expectedToken)
  if (!connected || epoch !== identityEpoch || accountId.value !== expectedAccountId
    || platformState.token !== expectedToken) { if (epoch === identityEpoch) starting.value = null; return }
  if (!available.value || !createTutorial(scene)) starting.value = null
}
function continueIntroduction() {
  if (lastIntroduction.value && chapterComplete.value) { introductionOpen.value = false; return }
  if (lastIntroduction.value) { void startPractice(); return }
  introductionIndex.value++
}
function repeatChapter() {
  if (actionBusy.value || !accountId.value) return
  unsaved.value = !restartGuidedTutorial(accountId.value)
  progress.value = loadGuidedTutorialProgress(accountId.value)
  introductionIndex.value = 0
  introductionOpen.value = true
}
onMounted(() => {
  void loadDeckCatalog().then(cards => { catalog.value = cards }).catch(() => { catalog.value = [] })
  const epoch = identityEpoch; const expectedAccountId = accountId.value; const expectedToken = platformState.token
  if (expectedAccountId && expectedToken && l12State.status === 'offline')
    void ensureConnected(epoch, expectedAccountId, expectedToken)
})
onBeforeUnmount(closeCardDetails)
</script>

<template>
  <component :is="props.embedded ? 'section' : 'main'" class="tutorial-page" :class="{ embedded: props.embedded }" aria-label="十二军团新手教学">
    <header v-if="!props.embedded" class="tutorial-heading"><div><small>十二军团 · 新手教学</small><h1>初识十二军团</h1></div>
      <router-link v-if="!props.embedded" to="/battle">返回对战大厅</router-link>
    </header>
    <ol class="course-path" :class="{ compact: props.embedded }" aria-label="教学主线"><li v-for="(chapter, index) in tutorialChapters" :key="chapter" :aria-label="chapter" :class="{ current: index === (chapterComplete ? 1 : 0) }">
      <span>{{ props.embedded ? ['基础','响应','对局'][index] : chapter }}</span><small v-if="!props.embedded && index === 1">{{ progress.responseReviewed ? '已复盘' : chapterComplete ? '下一步' : '完成第一章后继续' }}</small><small v-if="!props.embedded && index > 1">尚未开放</small>
    </li></ol>

    <section v-if="introductionOpen" class="tutorial-conversation" :class="{ 'has-intro-cards': introduction.cards.length }" data-tutorial-introduction="true" aria-live="polite">
      <div class="guide-speaker"><span aria-hidden="true">✦</span><h2>{{ introduction.title }}</h2><small>{{ introductionIndex + 1 }} / {{ tutorialIntroduction.length }}</small></div>
      <div class="intro-cards" v-if="introduction.cards.length">
        <button v-for="card in introduction.cards" :key="card.id" class="intro-card" type="button"
          :disabled="!catalog.some(item => item.id === card.id)" :aria-label="`查看${card.name}卡牌详情`" @click="showCardDetails(card.id)">
          <img v-if="card.kind.includes('主宰')" class="intro-card-native-image" :src="masterProfileUrl(card.id)" :alt="card.name" />
          <CardImage v-else :card-id="card.id" :alt="card.name" intent="detail" eager native-orientation />
          <span>{{ card.name }}<small>{{ card.kind }}</small></span>
        </button>
      </div>
      <p>{{ introduction.text }}</p>
      <p v-if="introduction.flow" class="resource-flow">{{ introduction.flow }}</p>
      <div class="conversation-choices">
        <button class="primary" type="button" :disabled="lastIntroduction && (actionBusy || connecting)" @click="continueIntroduction">
          {{ starting ? '正在准备练习场面…' : lastIntroduction ? (chapterComplete ? '回到本章复盘' : accountId ? '进入战场' : '登录后开始实操') : '下一步' }}
        </button>
      </div>
    </section>

    <section v-else-if="!chapterComplete" class="tutorial-conversation" aria-live="polite">
      <div class="guide-speaker"><span aria-hidden="true">✦</span><b>向导</b><small>下一步</small></div>
      <h2>继续下一段实操</h2><p>接下来练习{{ nextScene === 'deploy' ? '打出军团与支付士气' : nextScene === 'move' ? '普通移动' : '进攻与防守' }}。</p>
      <div class="conversation-choices"><button class="primary" type="button" :disabled="actionBusy || connecting" @click="startPractice">{{ starting ? '正在准备练习场面…' : '下一步' }}</button></div>
    </section>
    <section v-else class="tutorial-conversation chapter-review" aria-live="polite">
      <div class="guide-speaker"><span aria-hidden="true">✦</span><b>向导</b><small>第一章复盘</small></div>
      <h2>现在，把刚才的行动串起来</h2>
      <p>你已完成打出军团、支付士气、移动、进攻和防守。继续按当前资源、位置和卡牌效果行动。</p>
      <p>{{ progress.responseReviewed ? '你也完成了反击响应。第三章仍在准备，可重学前两章。' : '下一章练习盖伏反击、支付费用并确认实际结果。' }}</p>
      <div class="conversation-choices"><button type="button" class="primary" :disabled="actionBusy || connecting" @click="startResponsePractice">{{ starting ? '正在准备练习场面…' : progress.responseReviewed ? '重学卡牌效果与响应' : '下一步：卡牌效果与响应' }}</button><button type="button" :disabled="actionBusy" @click="repeatChapter">重新学习第一章</button></div>
    </section>
    <p v-if="unsaved" class="tutorial-notice" role="status">进度暂未保存，当前学习可继续；刷新页面可能丢失本次进度。<button type="button" @click="retryProgressSave">重试保存</button></p>
    <p v-if="accountId && availabilityCopy" class="tutorial-status" role="status">{{ availabilityCopy }}</p>
    <p v-if="l12State.notice" class="tutorial-notice" role="alert">{{ l12State.notice }}</p>
    <p v-if="matchmakingBusy" class="room-wait" role="status">请先取消当前匹配，再开始教学。</p>
    <p v-if="roomBusy" class="room-wait" role="status">请先结束并退出当前对局，再开始教学。</p>
    <button v-if="l12State.room && l12State.game" class="return-game" type="button" @click="router.push('/game')">返回当前对局</button>
    <CatalogCardDetails v-if="detailCard" :card="detailCard" within-picker @close="closeCardDetails"/>
  </component>
</template>

<style scoped>
.tutorial-page{box-sizing:border-box;width:min(1000px,calc(100% - 32px));margin:0 auto;padding:28px 0 64px;color:#e7ecea}.tutorial-page.embedded{width:100%;padding:6px 0 20px}.tutorial-heading{display:flex;align-items:flex-start;justify-content:space-between;gap:18px}.tutorial-heading small{color:#67c9ce;font-size:13px;font-weight:800}.tutorial-heading h1{margin:4px 0 0;font-size:26px}.tutorial-heading a{color:#d6c27b;text-decoration:none;font-weight:800;min-height:44px;display:flex;align-items:center}.course-path{display:flex;gap:12px;list-style:none;margin:14px 0;padding:0}.course-path li{flex:1;border-bottom:2px solid #34434c;padding:8px 0;color:#86959c;display:flex;gap:8px;flex-wrap:wrap;align-items:center}.course-path .current{color:#e6cc80;border-color:#cfb970}.course-path small{font-size:12px}.tutorial-conversation{box-sizing:border-box;position:relative;padding:22px 26px;border:1px solid #52646b;background:linear-gradient(145deg,#142129,#0a1319);box-shadow:0 18px 44px #0006}.guide-speaker{display:flex;align-items:center;gap:10px;color:#efd487}.guide-speaker>span{font-size:22px}.guide-speaker small{margin-left:auto;color:#83a8af;font-size:12px;font-weight:800;letter-spacing:.08em}.tutorial-conversation h2{font-size:22px;line-height:1.4;margin:13px 0 8px}.tutorial-conversation p{font-size:15px;line-height:1.65;margin:0 0 13px;max-width:760px;color:#d8e1df}.intro-cards{display:flex;justify-content:center;align-items:stretch;gap:14px;margin:14px 0}.intro-card{display:flex;flex-direction:column;align-items:center;gap:7px;width:min(190px,44%);padding:9px;border:1px solid #3d535d;background:#0e1a21;color:#dfc979;font:800 14px inherit;box-shadow:0 8px 22px #0005}.intro-card:hover,.intro-card:focus-visible{border-color:#d9bd66;box-shadow:0 0 0 2px #d9bd6633,0 10px 26px #0007}.intro-card:disabled{opacity:.72}.intro-card>:first-child{width:100%;height:182px;object-fit:contain}.intro-card-native-image{display:block;background:#080d11}.intro-card span{display:grid;gap:2px}.intro-card small{color:#8fb4ba;font-size:12px}.conversation-choices{display:flex;align-items:stretch;gap:10px;flex-wrap:wrap;margin-top:14px}.conversation-choices button,.return-game{min-height:44px;padding:9px 16px;border:1px solid #526875;background:#182831;color:#dae5e3;font-family:inherit;font-size:14px;font-weight:800;line-height:1.6;text-align:left}.conversation-choices .primary,.return-game{background:#dfc67a;color:#101619;border-color:#dfc67a}.conversation-choices button:disabled{opacity:.5;cursor:not-allowed}.tutorial-status,.tutorial-notice,.room-wait{padding:12px 14px;margin:16px 0 0;border-left:3px solid #bc9d59;background:#211c12;color:#dccb97;line-height:1.7;font-size:14px}.tutorial-notice[role=alert]{border-color:#bc6877;background:#27171c;color:#edb5c0}.return-game{margin-top:14px}.chapter-review p+p{color:#9eafb4}
@media(max-width:700px){.tutorial-page{width:calc(100% - 24px);padding-top:18px}.tutorial-conversation{padding:20px 17px}.tutorial-heading h1{font-size:24px}.course-path{gap:10px}.course-path li{font-size:13px}.tutorial-conversation h2{font-size:20px}.tutorial-conversation p{font-size:15px;line-height:1.7}.conversation-choices{flex-direction:column}.conversation-choices button{width:100%;text-align:center}.intro-card{width:min(180px,48%);padding:8px}.intro-card>:first-child{height:190px}}
@media(max-height:480px) and (min-width:650px){.tutorial-heading{float:left}.course-path{margin:0 0 8px 230px}.course-path li{padding:4px 0;font-size:12px}.intro-cards{float:right;width:40%;margin:4px 0 8px 18px}.intro-card{width:100%;padding:6px}.intro-card>:first-child{height:126px}.tutorial-conversation{display:flow-root;padding:13px 18px}.tutorial-conversation h2{margin:8px 0 5px;font-size:18px}.tutorial-conversation p{font-size:13px;line-height:1.5;margin-bottom:8px}.conversation-choices{clear:both;margin-top:8px}}
.resource-flow{padding:10px 14px;border-left:3px solid #6dc3c8;background:#183039;color:#abdee0!important;font-weight:800}
.tutorial-notice button{min-height:44px;margin-left:10px;padding:7px 12px;border:1px solid #71808a;background:#14232c;color:#e9e2c7;font:inherit;cursor:pointer}
:global(.catalog-detail-dialog .tutorial-intro-detail-focus){position:relative;border-radius:4px;background:#3a3117!important;box-shadow:0 0 0 2px #e2c568,0 0 16px #e2c5684d;color:#fff3bd!important}:global(.catalog-detail-dialog .archive-effect.tutorial-intro-detail-focus){padding:10px}
:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus)){scroll-padding-top:58px}:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus)>header){position:sticky;z-index:3;top:0;padding:6px 0 10px;background:#101821;box-shadow:0 1px #33434b}:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus)>header h2){margin:0;font-size:21px}
.course-path.compact{max-width:330px;gap:6px;margin:2px 0 10px}.course-path.compact li{min-width:0;justify-content:center;padding:5px 4px;font-size:12px}.course-path.compact li:not(.current){color:#6e7c82}.tutorial-conversation.has-intro-cards{display:grid;grid-template-columns:minmax(0,1fr) minmax(300px,390px);grid-template-areas:'speaker cards' 'copy cards' 'flow cards' 'action cards';align-items:start;column-gap:26px}.tutorial-conversation.has-intro-cards .guide-speaker{grid-area:speaker}.tutorial-conversation.has-intro-cards>p:not(.resource-flow){grid-area:copy;align-self:start;margin-top:10px}.tutorial-conversation.has-intro-cards>.resource-flow{grid-area:flow}.tutorial-conversation.has-intro-cards .intro-cards{grid-area:cards;margin:0}.tutorial-conversation.has-intro-cards .conversation-choices{grid-area:action;align-self:end}.guide-speaker h2{margin:0;font-size:19px;line-height:1.4}.tutorial-conversation p{font-weight:500}.tutorial-conversation.has-intro-cards .intro-card{width:min(170px,46%)}.tutorial-conversation.has-intro-cards .intro-card>:first-child{height:156px}
@media(max-width:700px){.course-path.compact{max-width:none}.tutorial-conversation.has-intro-cards{display:grid;grid-template-columns:1fr;grid-template-areas:'speaker' 'copy' 'flow' 'cards' 'action';gap:0}.tutorial-conversation.has-intro-cards>p:not(.resource-flow){margin-top:10px}.tutorial-conversation.has-intro-cards .intro-cards{margin:8px 0}.tutorial-conversation.has-intro-cards .intro-card>:first-child{height:148px}}
@media(max-width:700px){:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus) .archive-modal-image){height:min(360px,44dvh)}:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus) .archive-modal-image .l12-card-image__img){object-fit:contain!important}}
@media(max-height:480px) and (min-width:650px){.course-path.compact{float:none;max-width:280px;margin:0 0 6px}.tutorial-conversation.has-intro-cards{display:grid;grid-template-columns:minmax(0,1fr) 270px;grid-template-areas:'speaker cards' 'copy cards' 'flow cards' 'action cards';column-gap:18px;padding:12px 16px}.tutorial-conversation.has-intro-cards .intro-cards{float:none;width:auto;margin:0}.tutorial-conversation.has-intro-cards .intro-card{width:46%;padding:5px}.tutorial-conversation.has-intro-cards .intro-card>:first-child{height:105px}.tutorial-conversation.has-intro-cards .conversation-choices{clear:none;margin-top:5px}.tutorial-conversation.has-intro-cards>p:not(.resource-flow){margin-top:6px}.guide-speaker h2{font-size:17px}:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus) .archive-modal-image){height:250px}:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus) .archive-modal-image .l12-card-image__img){object-fit:contain!important}:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus) .archive-modal-detail){padding:0}:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus) .archive-number){display:none}:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus) .archive-modal-detail h2){margin:0 0 4px;font-size:20px}:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus) .archive-tags span){padding:2px 5px;font-size:11px}:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus) .archive-modal-detail dl){margin:5px 0}:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus) .archive-modal-detail dl>*){padding:4px 8px}:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus) .archive-modal-detail .archive-effect){padding:6px 8px}:global(.catalog-detail-dialog:has(.tutorial-intro-detail-focus) .archive-modal-detail .archive-effect p){margin-top:3px;font-size:11px;line-height:1.35}}
</style>
