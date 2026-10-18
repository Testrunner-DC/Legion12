<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import BattleDockPortal from '../game/BattleDockPortal.vue'
import BattleOverlayPortal from '../game/BattleOverlayPortal.vue'
import { viewportRect, visibleViewport } from '../mobileViewport'
import type { ActiveTutorialState, GameState } from '../types'
import {
  cssAttributeValue,
  tutorialActionSelector,
  tutorialDialoguePages,
  tutorialFacts,
  tutorialHelpCopy,
  tutorialPlayerSelector,
  tutorialSnapshot,
  type TutorialAnchor,
  type TutorialHelpTopic,
  type TutorialSceneSnapshot,
} from './tutorialGuidance'

const props = withDefaults(defineProps<{ tutorial: ActiveTutorialState; game: GameState; busy?: boolean }>(), { busy: false })
const emit = defineEmits<{ continueMainline: []; restartScene: []; exitTutorial: [] }>()

const minimized = ref(false)
const dialogueIndex = ref(0)
const helpTopic = ref<TutorialHelpTopic | null>(null)
const panelElement = ref<HTMLElement | null>(null)
const initialFacts = tutorialFacts(props.tutorial, props.game)
const practiceInstanceId = ref<string | null>(initialFacts.practiceInstanceId)
const facts = computed(() => tutorialFacts(props.tutorial, props.game, practiceInstanceId.value || undefined))
const baseline = ref<TutorialSceneSnapshot>(tutorialSnapshot(facts.value))
const stateAligned = computed(() => props.tutorial.matchId === props.game.matchId && props.tutorial.version === 1)
const pages = computed(() => tutorialDialoguePages(props.tutorial, facts.value, baseline.value))
const dialoguePage = computed(() => pages.value[Math.min(dialogueIndex.value, pages.value.length - 1)] ?? null)
const dialogueDone = computed(() => dialogueIndex.value >= pages.value.length)
const lessonTitle = computed(() => ({ deploy: '打出军团', move: '普通移动', combat: '进攻与防守' })[props.tutorial.lessonId])

type DomState = {
  selectedPractice: boolean
  playButton: boolean
  moveButton: boolean
  attackButton: boolean
  availableSlots: boolean
  targetMaster: boolean
  paymentPanel: boolean
  paymentChoices: boolean
  selectedPayment: boolean
  confirmPayment: boolean
  restorePayment: boolean
  endTurn: boolean
  defenseActions: boolean
  responsePassSelected: boolean
}

const emptyDomState: DomState = {
  selectedPractice: false,
  playButton: false,
  moveButton: false,
  attackButton: false,
  availableSlots: false,
  targetMaster: false,
  paymentPanel: false,
  paymentChoices: false,
  selectedPayment: false,
  confirmPayment: false,
  restorePayment: false,
  endTurn: false,
  defenseActions: false,
  responsePassSelected: false,
}
const domState = ref<DomState>({ ...emptyDomState })

function queryExists(selector: string) {
  return typeof document !== 'undefined' && Boolean(document.querySelector(selector))
}

function practiceSlotSelector() {
  const instanceId = facts.value.practiceInstanceId
  if (!instanceId) return ''
  return `${tutorialPlayerSelector(facts.value.self.playerIndex)} [data-tutorial-anchor="field-slot"]`
    + `[data-tutorial-card-instance="${cssAttributeValue(instanceId)}"]`
}

function practiceHandSelector() {
  const instanceId = facts.value.practiceInstanceId
  return instanceId ? `.hand-card-wrap[data-tutorial-card-instance="${cssAttributeValue(instanceId)}"]` : ''
}

function scanDomState(): DomState {
  const instanceId = facts.value.practiceInstanceId
  const exactAction = (action: string) => instanceId ? tutorialActionSelector(action, instanceId) : '[data-tutorial-never]'
  const selfSlots = `${tutorialPlayerSelector(facts.value.self.playerIndex)} [data-tutorial-anchor="field-slot"]`
  const enemyMaster = `${tutorialPlayerSelector(facts.value.enemy.playerIndex)} [data-tutorial-anchor="master"]`
  const practiceSelected = [practiceHandSelector() && `${practiceHandSelector()}.selected`,
    practiceSlotSelector() && `${practiceSlotSelector()}.source`].filter(Boolean).some(queryExists)
  return {
    selectedPractice: practiceSelected,
    playButton: queryExists(exactAction('play-card')),
    moveButton: queryExists(exactAction('move')),
    attackButton: queryExists(exactAction('attack')),
    availableSlots: queryExists(`${selfSlots}.available`),
    targetMaster: queryExists(`${enemyMaster}.targetable`),
    paymentPanel: queryExists('[data-tutorial-anchor="payment-panel"]'),
    paymentChoices: queryExists('[data-tutorial-payment-resource]'),
    selectedPayment: queryExists('[data-tutorial-payment-resource][aria-pressed="true"]'),
    confirmPayment: queryExists(tutorialActionSelector('confirm-payment')),
    restorePayment: queryExists(tutorialActionSelector('restore-payment')),
    endTurn: queryExists(tutorialActionSelector('end-turn')),
    defenseActions: queryExists('[data-tutorial-anchor="defense-actions"]'),
    responsePassSelected: queryExists('[data-tutorial-choice="pass"].selected'),
  }
}

function sameDomState(left: DomState, right: DomState) {
  return (Object.keys(left) as Array<keyof DomState>).every(key => left[key] === right[key])
}

type GuideView = { eyebrow: string; title: string; body: string; reason: string; outcome: string; anchor: TutorialAnchor | null; waiting?: boolean }

function paymentGuide(): GuideView {
  const prompt = facts.value.resourcePrompt
  if (domState.value.restorePayment) return {
    eyebrow: '支付费用', title: '恢复支付面板', body: '支付任务已最小化。先恢复面板，再从亮起的士气中选择。',
    reason: '必须回到支付面板，才能继续选择本次费用来源。', outcome: '恢复面板本身不提交费用，也不会推进教学。',
    anchor: { selector: tutorialActionSelector('restore-payment'), label: '恢复支付面板', unavailable: '支付恢复按钮当前不可见。' },
  }
  if (!domState.value.paymentPanel || !domState.value.paymentChoices) return {
    eyebrow: '支付费用', title: '打开士气选择',
    body: `本次需要选择 ${prompt?.minChoose ?? 1} 个支付资源。点击士气区，查看亮起的可选士气。`,
    reason: '打出军团或移动都要先付清费用。', outcome: '打开面板只显示合法选择；尚未消耗士气。',
    anchor: { selector: `${tutorialPlayerSelector(facts.value.self.playerIndex)} [data-tutorial-anchor="morale"]`,
      label: '我方士气', unavailable: '士气区当前不在可见范围，请先展开我方战场。' },
  }
  if (!domState.value.selectedPayment) return {
    eyebrow: '支付费用', title: '选择亮起的士气',
    body: `本次需选择 ${prompt?.minChoose ?? 1}${prompt?.maxChoose !== prompt?.minChoose ? ` 至 ${prompt?.maxChoose}` : ''} 个资源。只选择当前面板允许的士气。`,
    reason: '选择支付来源，决定哪些活跃士气在本次操作后转为休整。', outcome: '选择尚未提交；可先核对，再确认支付。',
    anchor: { selector: '[data-tutorial-payment-resource]', label: '可支付士气',
      unavailable: '当前支付面板没有可见的可选士气。', multiple: true },
  }
  return {
    eyebrow: '支付费用', title: '确认本次支付', body: '已选择支付资源。确认后等待场面更新。',
    reason: '确认后才会真正支付本次选择的士气。', outcome: '军团或移动结算后，所付士气会转为休整。',
    anchor: { selector: tutorialActionSelector('confirm-payment'), label: '确认支付', unavailable: '确认支付按钮当前不可见。' },
  }
}

function deployOrMoveGuide(kind: 'deploy' | 'move'): GuideView {
  const instanceId = facts.value.practiceInstanceId
  const exactCard = kind === 'deploy' ? practiceHandSelector() : practiceSlotSelector()
  if (!instanceId || !exactCard) return {
    eyebrow: '等待场面', title: '练习军团尚未出现', body: '正在准备本段的练习场面。', anchor: null, waiting: true,
    reason: '先看到本段要使用的军团，才能继续操作。', outcome: '军团出现后，提示会指向正确的卡牌。',
  }
  if (facts.value.resourcePrompt) return paymentGuide()
  if (!domState.value.selectedPractice && !domState.value.availableSlots) return {
    eyebrow: '实际操作', title: `选择${facts.value.practiceCard?.name || '练习军团'}`,
    body: kind === 'deploy' ? '点击手牌中高亮的西施。' : '点击后排中格的西施，打开她当前可执行的操作。',
    reason: kind === 'deploy' ? '先选中西施，才能把她从手牌打到战场。' : '先选中西施，才能查看她此刻允许的移动。',
    outcome: '选择只打开原操作入口，不等于已经打出、移动或支付。',
    anchor: { selector: exactCard, label: facts.value.practiceCard?.name || '练习军团',
      unavailable: kind === 'deploy' ? '练习军团不在可见手牌范围，请滑动或展开我方手牌。' : '后排中格的练习军团不在可见战场内。' },
  }
  if (kind === 'deploy' && domState.value.playButton && !domState.value.availableSlots) return {
    eyebrow: '实际操作', title: '准备打出军团', body: '在移动端先点“打出”，让战场标出本次可选的合法空阵地。',
    reason: '“打出”把手牌选择切换为战场落点选择。', outcome: '原界面会高亮合法空阵地；军团尚未登场。',
    anchor: { selector: tutorialActionSelector('play-card', instanceId), label: '打出', unavailable: '“打出”按钮当前不可见，请重新点选练习军团。' },
  }
  if (kind === 'move' && domState.value.moveButton && !domState.value.availableSlots) return {
    eyebrow: '实际操作', title: '选择普通移动', body: '点击“移动”。随后只选择原界面亮起的合法空格。',
    reason: '“移动”进入本次位移目标选择，而不是直接改变位置。', outcome: '原界面会高亮合法相邻空格；尚未支付或移动。',
    anchor: { selector: tutorialActionSelector('move', instanceId), label: '移动', unavailable: '“移动”按钮当前不可见，请重新点选练习军团。' },
  }
  if (domState.value.availableSlots) return {
    eyebrow: '实际操作', title: kind === 'deploy' ? '选择合法空阵地' : '选择亮起的相邻空格',
    body: kind === 'deploy' ? '选择一个高亮的合法空阵地，随后按支付提示继续。' : '选择一个已经高亮的相邻空格。',
    reason: kind === 'deploy' ? '选择落点决定新军团登场后的站位与后续攻防作用。' : '选择新格位是在资源代价与保护、进攻机会之间作取舍。',
    outcome: '选定后会进入支付；付清士气后才会完成登场或移动。',
    anchor: { selector: `${tutorialPlayerSelector(facts.value.self.playerIndex)} [data-tutorial-anchor="field-slot"].available`,
      label: '合法空格', unavailable: '当前没有可见的合法空格，请重新选择练习军团。', multiple: true },
  }
  return { eyebrow: '等待结果', title: '等待场面更新', body: '操作已经提交，稍等场面显示结果。',
    reason: '登场或移动结算后，卡牌位置和士气状态才会改变。', outcome: '场面更新后再一起查看资源与位置变化。', anchor: null, waiting: true }
}

function combatGuide(): GuideView {
  if (props.tutorial.stepId === 'end-turn') return {
    eyebrow: '实际操作', title: '结束当前回合', body: '点击“结束回合”。随后对手会按教学场景中的合法动作进攻，你仍需亲自完成防守选择。',
    reason: '结束回合把行动权交给对方，才能进入防守教学。', outcome: '对方将按回合规则行动；点击本身不是防守完成。',
    anchor: domState.value.endTurn
      ? { selector: tutorialActionSelector('end-turn'), label: '结束回合', unavailable: '结束回合按钮当前不可见。' }
      : null,
    waiting: !domState.value.endTurn,
  }
  if (props.tutorial.stepId === 'defend') return {
    eyebrow: '实际操作', title: '完成本次防守选择',
    body: `来袭进攻值为 ${facts.value.pendingAttackValue ?? '—'}。可先选择合法军团再确认抵挡，也可明确选择不抵挡；选好后等待场面显示结果。`,
    reason: '抵挡是在保护主宰或军团与投入手牌、场上兵力之间作取舍。', outcome: '结果会反映在主宰血量与军团兵力上。',
    anchor: { selector: '[data-tutorial-anchor="defense-actions"]', label: '抵挡选择',
      unavailable: '抵挡选择当前被最小化或不在可见范围，请先恢复抵挡面板。' },
    waiting: !domState.value.defenseActions,
  }
  const instanceId = facts.value.practiceInstanceId
  const exactCard = practiceSlotSelector()
  if (!instanceId || !exactCard) return { eyebrow: '等待场面', title: '练习军团尚未出现', body: '正在准备本段的练习场面。',
    reason: '先看到前排左格的练习军团，才能继续进攻。', outcome: '军团出现后，提示会指向正确的卡牌。', anchor: null, waiting: true }
  if (!domState.value.selectedPractice && !domState.value.targetMaster) return {
    eyebrow: '实际操作', title: `选择${facts.value.practiceCard?.name || '进攻军团'}`, body: '点击前排左格的练习军团，打开它当前可执行的操作。',
    reason: '先选中这张军团，才能使用它本回合的进攻机会。', outcome: '选择只打开操作入口，尚未进攻或消耗进攻机会。',
    anchor: { selector: exactCard, label: facts.value.practiceCard?.name || '进攻军团', unavailable: '前排左格的练习军团当前不可见。' },
  }
  if (domState.value.attackButton && !domState.value.targetMaster) return {
    eyebrow: '实际操作', title: '选择进攻', body: '点击“进攻”，让原界面标出本次合法目标。',
    reason: '进入进攻模式后，只能选择界面亮起的合法目标。', outcome: '对方主宰会被标出；尚未造成血量变化。',
    anchor: { selector: tutorialActionSelector('attack', instanceId), label: '进攻', unavailable: '“进攻”按钮当前不可见，请重新点选练习军团。' },
  }
  if (domState.value.targetMaster) return {
    eyebrow: '实际操作', title: '进攻对方主宰', body: '对方主宰已经由原界面标为合法目标。点击主宰并等待结算。',
    reason: '进攻主宰直接推进把其血量降到 0 的对局目标。', outcome: '结算后，主宰血量与进攻军团活跃/休整状态会更新。',
    anchor: { selector: `${tutorialPlayerSelector(facts.value.enemy.playerIndex)} [data-tutorial-anchor="master"].targetable`,
      label: '对方主宰', unavailable: '对方主宰当前不在可见范围，请先展开对方战场。' },
  }
  return { eyebrow: '等待结果', title: '等待进攻结算', body: '进攻已经提交，稍等场面显示结果。',
    reason: '进攻可能还有响应与防守，要等这些选择完成。', outcome: '结算后再查看主宰血量与军团状态。', anchor: null, waiting: true }
}

const actionGuide = computed<GuideView>(() => props.tutorial.lessonId === 'deploy'
  ? deployOrMoveGuide('deploy')
  : props.tutorial.lessonId === 'move' ? deployOrMoveGuide('move') : combatGuide())

const ownResponseGuide = computed<GuideView | null>(() => {
  const prompt = props.game.prompts?.find(item => item.playerIndex === props.game.you
    && item.kind === 'response' && item.validChoices.includes('pass'))
  if (!prompt) return null
  const prefix = `[data-tutorial-prompt-id="${cssAttributeValue(prompt.promptId)}"]`
  const selected = domState.value.responsePassSelected
  return {
    eyebrow: '进攻中的响应机会', title: selected ? '确认不响应' : '先处理当前响应',
    body: '进攻结算前，符合时机的卡牌可能有响应机会。这段练习先不使用响应牌；选择“不响应”，再继续观察进攻和防守。',
    reason: '不响应表示这一次不使用响应牌，不会取消对方进攻，也不等于已经选择不抵挡。',
    outcome: '关闭这次响应选择后，对局会继续到接下来的结算或抵挡选择。',
    anchor: { selector: selected ? `${prefix} [data-tutorial-action="confirm-prompt"]`
      : `${prefix} [data-tutorial-choice="pass"]`, label: selected ? '确认选择' : '不响应',
      unavailable: '请在当前响应面板中选择“不响应”。' },
  }
})

const visibleGuide = computed<GuideView | null>(() => {
  if (!stateAligned.value) return {
    eyebrow: '准备中', title: '等待当前教学场面', body: '正在进入这段练习，请稍候。', anchor: null, waiting: true,
    reason: '先准备好正确的练习场面，提示才能指向要操作的位置。', outcome: '场面出现后即可继续主线。',
  }
  if (ownResponseGuide.value) return ownResponseGuide.value
  if (!dialogueDone.value && dialoguePage.value) return dialoguePage.value
  return props.tutorial.status === 'completed' ? dialoguePage.value : actionGuide.value
})

const helpCopy = computed(() => helpTopic.value ? tutorialHelpCopy(helpTopic.value, facts.value) : '')

function advanceDialogue() {
  if (props.busy || helpTopic.value) return
  if (props.tutorial.status === 'completed') {
    if (stateAligned.value) emit('continueMainline')
    return
  }
  if (!dialogueDone.value) dialogueIndex.value++
}

function restartScene() {
  if (!props.busy) emit('restartScene')
}

function exitTutorial() {
  if (!props.busy) emit('exitTutorial')
}

type RectView = { left: number; top: number; width: number; height: number }
const focusRects = ref<RectView[]>([])
const arrow = ref<{ x1: number; y1: number; x2: number; y2: number } | null>(null)
const panelSide = ref<'left' | 'right'>('left')
const anchorUnavailable = ref('')
let mutationObserver: MutationObserver | null = null
let resizeObserver: ResizeObserver | null = null
let observedElements: HTMLElement[] = []
let refreshQueued = false

function intersects(left: RectView, right: RectView) {
  return Math.min(left.left + left.width, right.left + right.width) - Math.max(left.left, right.left) > 3
    && Math.min(left.top + left.height, right.top + right.height) - Math.max(left.top, right.top) > 3
}

function visibleElement(element: HTMLElement, viewport: RectView) {
  if (!element.getClientRects().length) return false
  const style = getComputedStyle(element)
  if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity) === 0) return false
  const rect = viewportRect(element)
  if (rect.width < 2 || rect.height < 2 || !intersects(rect, viewport)) return false
  let ancestor = element.parentElement
  while (ancestor && ancestor !== document.body) {
    const overflow = getComputedStyle(ancestor)
    if ([overflow.overflow, overflow.overflowX, overflow.overflowY].some(value => ['auto', 'scroll', 'hidden', 'clip'].includes(value))) {
      const ancestorRect = viewportRect(ancestor)
      if (!intersects(rect, ancestorRect)) return false
    }
    ancestor = ancestor.parentElement
  }
  return true
}

function unionRect(rects: RectView[]) {
  const left = Math.min(...rects.map(rect => rect.left))
  const top = Math.min(...rects.map(rect => rect.top))
  const right = Math.max(...rects.map(rect => rect.left + rect.width))
  const bottom = Math.max(...rects.map(rect => rect.top + rect.height))
  return { left, top, width: right - left, height: bottom - top }
}

function observeGeometry(elements: HTMLElement[]) {
  if (elements.length === observedElements.length && elements.every((element, index) => element === observedElements[index])) return
  resizeObserver?.disconnect()
  resizeObserver = null
  observedElements = elements
  if (!elements.length) return
  resizeObserver = new ResizeObserver(queueRefresh)
  elements.forEach(element => resizeObserver?.observe(element))
}

function updateGeometry() {
  focusRects.value = []
  arrow.value = null
  anchorUnavailable.value = ''
  const anchor = !minimized.value && !helpTopic.value ? visibleGuide.value?.anchor : null
  if (!anchor || typeof document === 'undefined') { observeGeometry([]); return }
  const viewportSize = visibleViewport()
  const viewport = { left: 0, top: 0, width: viewportSize.width, height: viewportSize.height }
  const matches = Array.from(document.querySelectorAll(anchor.selector)).filter((element): element is HTMLElement => element instanceof HTMLElement)
  const visible = matches.filter(element => visibleElement(element, viewport))
  const targets = anchor.multiple ? visible : visible.slice(0, 1)
  if (!targets.length) {
    observeGeometry([])
    anchorUnavailable.value = anchor.unavailable
    return
  }
  const rects = targets.map(element => {
    const rect = viewportRect(element)
    return { left: rect.left, top: rect.top, width: rect.width, height: rect.height }
  })
  focusRects.value = rects
  const target = unionRect(rects)
  panelSide.value = target.left + target.width / 2 < viewport.width / 2 ? 'right' : 'left'
  void nextTick(() => {
    const panel = panelElement.value
    if (!panel) return
    const panelRect = viewportRect(panel)
    const targetX = target.left + target.width / 2
    const targetY = target.top + target.height / 2
    const fromX = panelSide.value === 'right' ? panelRect.left : panelRect.right
    const fromY = Math.max(panelRect.top + 24, Math.min(panelRect.bottom - 24, targetY))
    arrow.value = { x1: fromX, y1: fromY, x2: targetX, y2: targetY }
  })
  observeGeometry(panelElement.value ? [...targets, panelElement.value] : targets)
}

function refreshFromDom() {
  refreshQueued = false
  const next = scanDomState()
  if (!sameDomState(next, domState.value)) domState.value = next
  void nextTick(updateGeometry)
}

function queueRefresh() {
  if (refreshQueued) return
  refreshQueued = true
  requestAnimationFrame(refreshFromDom)
}

function onMutation(records: MutationRecord[]) {
  const meaningful = records.some(record => {
    const element = record.target instanceof Element ? record.target : record.target.parentElement
    return !element?.closest('.tutorial-guidance-shell,.tutorial-guide-layer')
  })
  if (meaningful) queueRefresh()
}

watch(() => `${props.tutorial.matchId}:${props.tutorial.lessonId}`, () => {
  if (props.tutorial.matchId !== props.game.matchId) {
    practiceInstanceId.value = null
    return
  }
  const next = tutorialFacts(props.tutorial, props.game)
  practiceInstanceId.value = next.practiceInstanceId
  baseline.value = tutorialSnapshot(next)
}, { flush: 'sync' })

watch(() => [stateAligned.value, facts.value.practiceInstanceId] as const, ([aligned, instanceId]) => {
  if (aligned && !practiceInstanceId.value && instanceId) {
    practiceInstanceId.value = instanceId
    baseline.value = tutorialSnapshot(facts.value)
  }
}, { flush: 'sync' })

watch(() => `${props.tutorial.matchId}:${props.tutorial.lessonId}:${props.tutorial.stepId}:${props.tutorial.status}`, () => {
  minimized.value = false
  dialogueIndex.value = 0
  helpTopic.value = null
  queueRefresh()
}, { flush: 'sync' })

watch(() => [props.game.revision, props.game.phase, props.game.prompts?.map(prompt => prompt.promptId).join('|'),
  dialogueIndex.value, helpTopic.value, minimized.value], queueRefresh, { flush: 'post' })
watch(() => visibleGuide.value?.anchor?.selector, queueRefresh, { flush: 'post' })

onMounted(() => {
  mutationObserver = new MutationObserver(onMutation)
  mutationObserver.observe(document.body, { subtree: true, childList: true, attributes: true,
    attributeFilter: ['class', 'aria-pressed', 'disabled', 'data-tutorial-card-instance', 'data-tutorial-action'] })
  window.addEventListener('resize', queueRefresh)
  window.addEventListener('scroll', queueRefresh, true)
  window.addEventListener('l12-viewport-change', queueRefresh)
  queueRefresh()
})

onBeforeUnmount(() => {
  mutationObserver?.disconnect()
  resizeObserver?.disconnect()
  window.removeEventListener('resize', queueRefresh)
  window.removeEventListener('scroll', queueRefresh, true)
  window.removeEventListener('l12-viewport-change', queueRefresh)
})
</script>

<template>
  <BattleDockPortal lane="context">
    <button v-if="minimized" class="tutorial-guidance-restore" type="button" aria-label="展开教学说明"
      @click="minimized = false">教学提示</button>
  </BattleDockPortal>
  <BattleOverlayPortal>
    <div class="tutorial-guide-layer" aria-hidden="true">
      <div v-for="(rect, index) in focusRects" :key="index" class="tutorial-focus-ring"
        :style="{ left: `${rect.left - 5}px`, top: `${rect.top - 5}px`, width: `${rect.width + 10}px`, height: `${rect.height + 10}px` }" />
      <svg v-if="arrow" class="tutorial-guide-arrow" :viewBox="`0 0 ${visibleViewport().width} ${visibleViewport().height}`" preserveAspectRatio="none">
        <defs><marker id="tutorial-arrow-head" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto"><path d="M0,0 L0,6 L9,3 z" /></marker></defs>
        <path :d="`M ${arrow.x1} ${arrow.y1} L ${arrow.x2} ${arrow.y2}`" marker-end="url(#tutorial-arrow-head)" />
      </svg>
    </div>
    <div v-if="!minimized" ref="panelElement" class="tutorial-guidance-shell" :class="[`panel-${panelSide}`, { completed: tutorial.status === 'completed' }]"
      data-ui-contract="tutorial-authoritative-guidance"
      :data-tutorial-guide-id="dialogueDone ? (tutorial.status === 'completed' ? 'result' : 'action') : dialoguePage?.id"
      :data-tutorial-scene="tutorial.lessonId" :data-tutorial-reading="!ownResponseGuide && !dialogueDone">
      <section class="tutorial-guidance" aria-live="polite">
        <header>
          <div><small>{{ visibleGuide?.eyebrow || '新手教学' }}</small><h2>{{ visibleGuide?.title || lessonTitle }}</h2></div>
          <button type="button" aria-label="最小化教学说明" @click="minimized = true">−</button>
        </header>

        <template v-if="helpTopic">
          <p>{{ helpCopy }}</p>
          <button class="tutorial-primary" type="button" @click="helpTopic = null">明白了，回到主线</button>
        </template>
        <template v-else>
          <p>{{ visibleGuide?.body }}</p>
          <dl class="tutorial-cause-and-result">
            <div><dt>为什么</dt><dd>{{ visibleGuide?.reason }}</dd></div>
            <div><dt>会发生什么</dt><dd>{{ visibleGuide?.outcome }}</dd></div>
          </dl>
          <p v-if="anchorUnavailable" class="tutorial-anchor-unavailable" role="status">{{ anchorUnavailable }}</p>
          <div v-if="!ownResponseGuide && (!dialogueDone || tutorial.status === 'completed')" class="tutorial-main-action">
            <button class="tutorial-primary" type="button" :disabled="busy || !stateAligned" @click="advanceDialogue">
              {{ busy ? '正在切换…' : tutorial.status === 'completed' ? '继续主线' : '继续' }}
            </button>
          </div>
          <div class="tutorial-help">
            <span>我想了解…</span>
            <button type="button" @click="helpTopic = 'master'">主宰与胜负</button>
            <button type="button" @click="helpTopic = 'cards'">军团、战术与圣物</button>
            <button type="button" @click="helpTopic = 'morale'">士气与费用</button>
            <button type="button" @click="helpTopic = 'current'">当前练习军团</button>
          </div>
        </template>

        <footer>
          <button type="button" :disabled="busy" @click="restartScene">重学本段</button>
          <button type="button" :disabled="busy" @click="exitTutorial">退出教学</button>
        </footer>
      </section>
    </div>
  </BattleOverlayPortal>
</template>

<style scoped>
.tutorial-guide-layer{position:fixed;z-index:var(--l12-battle-fixed-controls-z,5100);inset:0;pointer-events:none}.tutorial-focus-ring{position:fixed;box-sizing:border-box;border:3px solid #f5d45f;border-radius:10px;box-shadow:0 0 0 3px #071014cc,0 0 22px #f5d45fcc;animation:tutorial-guide-pulse 1.4s ease-in-out infinite;pointer-events:none}.tutorial-guide-arrow{position:fixed;inset:0;width:100%;height:100%;overflow:visible;pointer-events:none}.tutorial-guide-arrow path{fill:none;stroke:#f5d45f;stroke-width:4;stroke-linecap:round;filter:drop-shadow(0 2px 2px #000)}.tutorial-guide-arrow marker path{fill:#f5d45f;stroke:none}@keyframes tutorial-guide-pulse{50%{border-color:#fff2a7;box-shadow:0 0 0 4px #071014cc,0 0 30px #fff2a799}}
.tutorial-guidance-shell{position:fixed;z-index:calc(var(--l12-battle-fixed-controls-z,5100) + 1);top:14px;box-sizing:border-box;width:min(390px,calc(var(--l12-viewport-width,100vw) - 28px));max-height:calc(var(--l12-viewport-height,100dvh) - 28px);overflow:auto;pointer-events:none;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.tutorial-guidance-shell.panel-left{left:14px;right:auto}.tutorial-guidance-shell.panel-right{right:14px;left:auto}.tutorial-guidance{box-sizing:border-box;width:100%;min-width:0;padding:13px 15px;border:1px solid #5c6b73;background:#091118f2;color:#f0f2ee;box-shadow:0 14px 38px #000b;pointer-events:auto}.tutorial-guidance-shell.completed .tutorial-guidance{border-color:#b59a4b;background:#171408f2}.tutorial-guidance header{display:flex;align-items:flex-start;justify-content:space-between;gap:12px}.tutorial-guidance small{color:#62cbd1;font-size:12px;font-weight:900;letter-spacing:.08em}.tutorial-guidance h2{margin:3px 0 0;font-size:18px}.tutorial-guidance header button{min-width:44px;min-height:44px;border:1px solid #57656d;background:#151e24;color:#fff;font:900 18px inherit}.tutorial-guidance p{margin:10px 0;color:#d5dcdd;font-size:14px;font-weight:800;line-height:1.65}.tutorial-cause-and-result{display:grid;gap:6px;margin:8px 0}.tutorial-cause-and-result div{display:grid;grid-template-columns:74px minmax(0,1fr);gap:8px;padding:7px 8px;background:#0c181d}.tutorial-cause-and-result dt{color:#e4c96f;font-size:12px;font-weight:900}.tutorial-cause-and-result dd{margin:0;color:#c8d3d4;font-size:13px;font-weight:800;line-height:1.5}.tutorial-anchor-unavailable{padding:7px 9px;border-left:3px solid #e0b85a;background:#241d0d;color:#f4d994!important}.tutorial-main-action{display:flex;justify-content:flex-end;margin:10px 0}.tutorial-guidance button{min-height:44px;padding:8px 12px;border:1px solid #57656d;background:#151e24;color:#fff;font:900 13px inherit}.tutorial-guidance button:disabled{cursor:not-allowed;opacity:.55}.tutorial-guidance .tutorial-primary{border-color:#e0b85a;background:#3b2e12;color:#fff1b8}.tutorial-help{display:flex;align-items:center;flex-wrap:wrap;gap:6px;margin-top:10px;padding-top:9px;border-top:1px solid #344149}.tutorial-help span{width:100%;color:#91a2a8;font-size:12px;font-weight:900}.tutorial-help button{min-height:40px;padding:7px 9px;color:#d4e8e7}.tutorial-guidance footer{display:flex;justify-content:space-between;gap:8px;margin-top:11px;padding-top:9px;border-top:1px solid #344149}.tutorial-guidance footer button{min-width:108px}.tutorial-guidance-restore{z-index:var(--l12-battle-fixed-controls-z,5100);min-height:44px;padding:9px 13px;border:1px solid #5c6b73;background:#091118f2;color:#ecd47f;box-shadow:0 14px 38px #000b;font:900 14px 'Microsoft YaHei','微软雅黑',sans-serif}
:global(html[data-l12-mobile=true] #l12-landscape-teleports > .tutorial-guidance-shell){width:min(350px,calc(var(--l12-viewport-width,100vw) - 188px));max-width:calc(var(--l12-viewport-width,100vw) - 188px)}:global(html[data-l12-mobile=true] #l12-landscape-teleports > .tutorial-guidance-shell.panel-left){left:86px}:global(html[data-l12-mobile=true] #l12-landscape-teleports > .tutorial-guidance-shell.panel-right){right:86px}:global(html[data-l12-mobile=true] #l12-landscape-teleports .mobile-battle-dock__context > .tutorial-guidance-restore){position:static!important;box-sizing:border-box;width:100%;max-width:100%;min-height:44px!important;padding:8px 4px!important;white-space:normal;overflow-wrap:normal}
/* The landscape portal enables pointer input on its direct children. The
   teaching focus is decorative, while only the dialogue itself is interactive. */
:global(#l12-landscape-teleports > .tutorial-guide-layer),:global(#l12-landscape-teleports > .tutorial-guide-layer *){pointer-events:none}
:global(#l12-landscape-teleports > .tutorial-guidance-shell){pointer-events:none}
</style>
