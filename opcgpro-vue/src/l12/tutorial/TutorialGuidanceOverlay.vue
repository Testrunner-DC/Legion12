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
  tutorialInstructionBlocked,
  tutorialPlayerSelector,
  tutorialPaymentSelectionCount,
  tutorialSnapshot,
  type TutorialAnchor,
  type TutorialSceneSnapshot,
} from './tutorialGuidance'
import { responseTutorialAction, responseTutorialDialogue, responseTutorialFacts, responseTutorialReturnCost } from './tutorialResponseGuidance'
import { finaleAction, finaleAttacker, finaleDialogue, preparationAction, preparationDialogue } from './tutorialFinalChapter'

const props = withDefaults(defineProps<{ tutorial: ActiveTutorialState; game: GameState; busy?: boolean; presentationBusy?: boolean; acceptedHandoffIdentity?: string; connected?: boolean; unsavedProgress?: boolean; progressRetryRequired?: boolean }>(),
  { busy: false, presentationBusy: false, acceptedHandoffIdentity: '', connected: true, unsavedProgress: false, progressRetryRequired: false })
const emit = defineEmits<{ continueMainline: []; restartScene: []; exitTutorial: []; retryProgressSave: [] }>()

const minimized = ref(false)
const guidanceVisible = computed(() => !minimized.value
  && !(props.tutorial.lessonId === 'finale' && props.tutorial.status === 'completed' && !props.progressRetryRequired))
const dialogueIndex = ref(0)
const finaleIntroduced = ref(false)
const panelElement = ref<HTMLElement | null>(null)
const initialFacts = tutorialFacts(props.tutorial, props.game)
const practiceInstanceId = ref<string | null>(initialFacts.practiceInstanceId)
const facts = computed(() => tutorialFacts(props.tutorial, props.game, practiceInstanceId.value || undefined))
const baseline = ref<TutorialSceneSnapshot>(tutorialSnapshot(facts.value))
const responseFacts = computed(() => responseTutorialFacts(props.game, props.tutorial.focus))
const stateAligned = computed(() => props.tutorial.matchId === props.game.matchId
  && (props.tutorial.lessonId === 'response'
    ? props.tutorial.version === 2 && props.tutorial.recipeId === 'public-response-v1'
    : props.tutorial.version === 1))
const instructionBlocked = computed(() => tutorialInstructionBlocked(props.game, props.busy, props.presentationBusy))
const pages = computed(() => props.tutorial.lessonId === 'response'
  ? responseTutorialDialogue(props.tutorial.stepId, responseFacts.value)
  : props.tutorial.lessonId === 'preparation' ? preparationDialogue(props.game)
    : props.tutorial.lessonId === 'finale' ? finaleDialogue(finaleIntroduced.value ? props.tutorial.stepId : 'finale-ready', props.game)
      : tutorialDialoguePages(props.tutorial, facts.value, baseline.value))
const dialoguePage = computed(() => pages.value[Math.min(dialogueIndex.value, pages.value.length - 1)] ?? null)
const dialogueDone = computed(() => dialogueIndex.value >= pages.value.length)
const lessonTitle = computed(() => ({ deploy: '打出军团', move: '普通移动', combat: '进攻与防守', response: '卡牌效果与响应', preparation: '对局准备', finale: '完成一场对局' })[props.tutorial.lessonId])
const completedIdentity = computed(() => [props.tutorial.accountId, props.tutorial.sessionId,
  props.tutorial.connectionGeneration, props.tutorial.roomCode, props.tutorial.matchId,
  props.tutorial.lessonId, props.tutorial.version, props.tutorial.recipeId || '', props.tutorial.stepId].join(':'))

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
  selectedPaymentCount: number
  confirmPayment: boolean
  restorePayment: boolean
  endTurn: boolean
  defenseActions: boolean
  responsePassSelected: boolean
  responsePlayButton: boolean
  responseAvailableSlots: boolean
  responsePromptChoiceSelected: boolean
  responseArtifactAbility: boolean
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
  selectedPaymentCount: 0,
  confirmPayment: false,
  restorePayment: false,
  endTurn: false,
  defenseActions: false,
  responsePassSelected: false,
  responsePlayButton: false,
  responseAvailableSlots: false,
  responsePromptChoiceSelected: false,
  responseArtifactAbility: false,
}
const domState = ref<DomState>({ ...emptyDomState })

function queryExists(selector: string) {
  return typeof document !== 'undefined' && Boolean(document.querySelector(selector))
}

function practiceSlotSelector() {
  const instanceId = props.tutorial.lessonId === 'finale' ? finaleAttacker(props.game)?.instanceId : facts.value.practiceInstanceId
  if (!instanceId) return ''
  return `${tutorialPlayerSelector(facts.value.self.playerIndex)} [data-tutorial-anchor="field-slot"]`
    + `[data-tutorial-card-instance="${cssAttributeValue(instanceId)}"]`
}

function practiceHandSelector() {
  const instanceId = facts.value.practiceInstanceId
  return instanceId ? `.hand-card-wrap[data-tutorial-card-instance="${cssAttributeValue(instanceId)}"]` : ''
}

function scanDomState(): DomState {
  const instanceId = props.tutorial.lessonId === 'finale' ? finaleAttacker(props.game)?.instanceId : facts.value.practiceInstanceId
  const exactAction = (action: string) => instanceId ? tutorialActionSelector(action, instanceId) : '[data-tutorial-never]'
  const selfSlots = `${tutorialPlayerSelector(facts.value.self.playerIndex)} [data-tutorial-anchor="field-slot"]`
  const enemyMaster = `${tutorialPlayerSelector(facts.value.enemy.playerIndex)} [data-tutorial-anchor="master"]`
  const practiceSelected = [practiceHandSelector() && `${practiceHandSelector()}.selected`,
    practiceSlotSelector() && `${practiceSlotSelector()}.source`].filter(Boolean).some(queryExists)
  const responseCardId = props.tutorial.stepId === 'response-cover' ? responseFacts.value.counterId : responseFacts.value.legionId
  const responsePrompt = responseFacts.value.prompt
  const responsePrefix = responsePrompt ? `[data-tutorial-prompt-id="${cssAttributeValue(responsePrompt.promptId)}"]` : '[data-tutorial-never]'
  const paymentPickerClosed = queryExists(`${tutorialPlayerSelector(facts.value.self.playerIndex)} .mobile-morale-stack-trigger`)
    && !queryExists('.mobile-morale-overlay[aria-label="选择士气"]')
  return {
    selectedPractice: practiceSelected,
    playButton: queryExists(exactAction('play-card')),
    moveButton: queryExists(exactAction('move')),
    attackButton: queryExists(props.tutorial.lessonId === 'finale' ? tutorialActionSelector('attack') : exactAction('attack')),
    availableSlots: queryExists(`${selfSlots}.available`),
    targetMaster: queryExists(`${enemyMaster}.targetable`),
    paymentPanel: !paymentPickerClosed && queryExists('[data-tutorial-anchor="payment-panel"]'),
    paymentChoices: queryExists('[data-tutorial-payment-resource]'),
    selectedPayment: queryExists('[data-tutorial-payment-resource][aria-pressed="true"]'),
    selectedPaymentCount: tutorialPaymentSelectionCount(Array.from(document.querySelectorAll('[data-tutorial-payment-resource][aria-pressed="true"]'))
      .map(element => element.getAttribute('data-tutorial-payment-resource'))),
    confirmPayment: queryExists(tutorialActionSelector('confirm-payment')),
    restorePayment: queryExists(tutorialActionSelector('restore-payment')),
    endTurn: queryExists(tutorialActionSelector('end-turn')),
    defenseActions: queryExists('[data-tutorial-anchor="defense-actions"]'),
    responsePassSelected: queryExists('[data-tutorial-choice="pass"].selected'),
    responsePlayButton: Boolean(responseCardId && queryExists(tutorialActionSelector('play-card', responseCardId))),
    responseAvailableSlots: queryExists(`${selfSlots}[data-tutorial-row="${props.tutorial.stepId === 'response-cover' ? 1 : 0}"].available`),
    responsePromptChoiceSelected: queryExists(`${responsePrefix} [data-tutorial-choice][aria-pressed="true"],${responsePrefix} [data-tutorial-choice].selected`),
    responseArtifactAbility: Boolean(responseFacts.value.artifactId && queryExists(`[data-tutorial-ability="artifactSearch"][data-tutorial-card-instance="${cssAttributeValue(responseFacts.value.artifactId)}"]`)),
  }
}

function sameDomState(left: DomState, right: DomState) {
  return (Object.keys(left) as Array<keyof DomState>).every(key => left[key] === right[key])
}

type GuideView = { eyebrow: string; title: string; body: string; reason: string; outcome: string; anchor: TutorialAnchor | null; waiting?: boolean }

function paymentGuide(): GuideView {
  const prompt = facts.value.resourcePrompt
  const returning = prompt?.kind === 'resource-return' || prompt?.data?.choiceMode === 'resource-return'
    || props.tutorial.lessonId === 'response' && responseTutorialReturnCost(props.tutorial.stepId, responseFacts.value)
  if (domState.value.restorePayment) return {
    eyebrow: '支付费用', title: '恢复支付面板', body: '支付任务已最小化。先恢复面板，再从亮起的士气中选择。',
    reason: '必须回到支付面板，才能继续选择本次费用来源。', outcome: '恢复面板本身不提交费用，也不会推进教学。',
    anchor: { selector: tutorialActionSelector('restore-payment'), label: '恢复支付面板', unavailable: '支付恢复按钮当前不可见。' },
  }
  if (!domState.value.paymentPanel || !domState.value.paymentChoices) return {
    eyebrow: '支付费用', title: '打开士气选择',
    body: `本次需要${returning ? '返还' : '选择'} ${prompt?.minChoose ?? 1} 张士气。点击士气区，查看可选士气。`,
    reason: '打出卡牌、盖伏或移动，都要先付清本次费用。', outcome: '打开面板只显示合法选择；尚未消耗士气。',
    anchor: { selector: `${tutorialPlayerSelector(facts.value.self.playerIndex)} [data-tutorial-anchor="morale"]`,
      label: '我方士气', unavailable: '士气区当前不在可见范围，请先展开我方战场。' },
  }
  if (domState.value.selectedPaymentCount < (prompt?.minChoose ?? 1)) return {
    eyebrow: '支付费用', title: '选择亮起的士气',
    body: `本次需${returning ? '返还' : '选择'} ${prompt?.minChoose ?? 1}${prompt?.maxChoose !== prompt?.minChoose ? ` 至 ${prompt?.maxChoose}` : ''} 张士气。${returning ? '所选士气会返回士气牌库，而不是休整。' : '确认支付后，所选士气转为休整。'}`,
    reason: returning ? '返还与休整不同。' : '选择支付来源。', outcome: `目前已选择 ${domState.value.selectedPaymentCount} 张；选够后确认。`,
    anchor: { selector: '[data-tutorial-payment-resource]', label: '可支付士气',
      unavailable: '当前支付面板没有可见的可选士气。', multiple: true },
  }
  return {
    eyebrow: '支付费用', title: returning ? '确认返还士气' : '确认本次支付', body: returning ? '确认后，所选士气返回士气牌库；场上可用士气随之减少。' : '确认后，所选士气休整，再完成本次行动。',
    reason: '确认后才会真正支付。', outcome: returning ? '返还后继续处理效果。' : '所付士气转为休整。',
    anchor: { selector: tutorialActionSelector('confirm-payment'), label: '确认支付', unavailable: '确认支付按钮当前不可见。' },
  }
}

function deployOrMoveGuide(kind: 'deploy' | 'move'): GuideView {
  const instanceId = facts.value.practiceInstanceId
  const practiceName = facts.value.practiceCard?.name || '练习军团'
  const exactCard = kind === 'deploy' ? practiceHandSelector() : practiceSlotSelector()
  if (!instanceId || !exactCard) return {
    eyebrow: '等待场面', title: '练习军团尚未出现', body: '正在准备本段的练习场面。', anchor: null, waiting: true,
    reason: '先看到本段要使用的军团，才能继续操作。', outcome: '军团出现后，提示会指向正确的卡牌。',
  }
  if (facts.value.resourcePrompt) return paymentGuide()
  if (!domState.value.selectedPractice && !domState.value.availableSlots) return {
    eyebrow: '实际操作', title: `选择${facts.value.practiceCard?.name || '练习军团'}`,
    body: kind === 'deploy' ? `选择手牌中高亮的${practiceName}，准备让它登场。`
      : `选择后排的${practiceName}，准备移到前排，创造进攻主宰的机会。`,
    reason: kind === 'deploy' ? `先选中${practiceName}，再把它打到战场。` : '移动用资源换取新的站位。',
    outcome: '选择只打开原操作入口，不等于已经打出、移动或支付。',
    anchor: { selector: exactCard, label: facts.value.practiceCard?.name || '练习军团',
      unavailable: kind === 'deploy' ? '练习军团不在可见手牌范围，请滑动或展开我方手牌。' : '后排中格的练习军团不在可见战场内。' },
  }
  if (kind === 'deploy' && domState.value.playButton && !domState.value.availableSlots) return {
    eyebrow: '实际操作', title: '准备打出军团', body: '点击“打出”，选择军团的登场格位。',
    reason: '“打出”把手牌选择切换为战场落点选择。', outcome: '原界面会高亮合法空阵地；军团尚未登场。',
    anchor: { selector: tutorialActionSelector('play-card', instanceId), label: '打出', unavailable: '“打出”按钮当前不可见，请重新点选练习军团。' },
  }
  if (kind === 'move' && domState.value.moveButton && !domState.value.availableSlots) return {
    eyebrow: '实际操作', title: '选择普通移动', body: '点击“移动”，再选择亮起的相邻空格。普通移动要支付费用，但不会增加兵力。',
    reason: '“移动”进入本次位移目标选择，而不是直接改变位置。', outcome: '原界面会高亮合法相邻空格；尚未支付或移动。',
    anchor: { selector: tutorialActionSelector('move', instanceId), label: '移动', unavailable: '“移动”按钮当前不可见，请重新点选练习军团。' },
  }
  if (domState.value.availableSlots) return {
    eyebrow: '实际操作', title: kind === 'deploy' ? '选择合法空阵地' : '选择亮起的相邻空格',
    body: kind === 'deploy' ? '选择亮起的空格，随后按提示支付费用。' : '选择亮起的相邻空格，随后按提示支付费用。',
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
    eyebrow: '实际操作', title: '结束当前回合', body: '点击“结束回合”，把行动权交给对手。对手进攻后，选择如何保护我方主宰。',
    reason: '结束回合把行动权交给对方，才能进入防守教学。', outcome: '对方将按回合规则行动；点击本身不是防守完成。',
    anchor: domState.value.endTurn
      ? { selector: tutorialActionSelector('end-turn'), label: '结束回合', unavailable: '结束回合按钮当前不可见。' }
      : null,
    waiting: !domState.value.endTurn,
  }
  if (props.tutorial.stepId === 'defend') return {
    eyebrow: '实际操作', title: '完成本次防守选择',
    body: `来袭兵力为 ${facts.value.pendingAttackValue ?? '—'}。选择军团抵挡可保护目标；不抵挡则保留资源，承受这次进攻。`,
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
    eyebrow: '实际操作', title: '选择进攻', body: '点击“进攻”，再选择亮起的合法目标。',
    reason: '进入进攻模式后，只能选择界面亮起的合法目标。', outcome: '对方主宰会被标出；尚未造成血量变化。',
    anchor: { selector: tutorialActionSelector('attack', instanceId), label: '进攻', unavailable: '“进攻”按钮当前不可见，请重新点选练习军团。' },
  }
  if (domState.value.targetMaster) return {
    eyebrow: '实际操作', title: '进攻对方主宰', body: '点击亮起的对方主宰。对方未抵挡时，主宰血量会减少，进一步接近胜利。',
    reason: '进攻主宰直接推进把其血量降到 0 的对局目标。', outcome: '结算后，主宰血量与进攻军团活跃/休整状态会更新。',
    anchor: { selector: `${tutorialPlayerSelector(facts.value.enemy.playerIndex)} [data-tutorial-anchor="master"].targetable`,
      label: '对方主宰', unavailable: '对方主宰当前不在可见范围，请先展开对方战场。' },
  }
  return { eyebrow: '等待结果', title: '等待进攻结算', body: '进攻已经提交，稍等场面显示结果。',
    reason: '进攻可能还有响应与防守，要等这些选择完成。', outcome: '结算后再查看主宰血量与军团状态。', anchor: null, waiting: true }
}

const actionGuide = computed<GuideView>(() => {
  if (props.tutorial.lessonId === 'preparation') return preparationAction(props.game, domState.value.responsePromptChoiceSelected)
  if (props.tutorial.lessonId === 'finale') return finaleAction(props.game, {
    selected: domState.value.selectedPractice, attackButton: domState.value.attackButton, targetMaster: domState.value.targetMaster,
  })
  if (props.tutorial.lessonId === 'response') return facts.value.resourcePrompt ? paymentGuide()
    : responseTutorialAction(props.tutorial.stepId, responseFacts.value, {
      selected: domState.value.selectedPractice, playButton: domState.value.responsePlayButton,
      availableSlots: domState.value.responseAvailableSlots, endTurn: domState.value.endTurn,
      selectedPromptChoice: domState.value.responsePromptChoiceSelected,
      artifactAbility: domState.value.responseArtifactAbility,
    })
  return props.tutorial.lessonId === 'deploy' ? deployOrMoveGuide('deploy')
    : props.tutorial.lessonId === 'move' ? deployOrMoveGuide('move') : combatGuide()
})

const ownResponseGuide = computed<GuideView | null>(() => {
  if (props.tutorial.lessonId === 'response') return null
  const prompt = props.game.prompts?.find(item => item.playerIndex === props.game.you
    && item.kind === 'response' && item.validChoices.includes('pass'))
  if (!prompt) return null
  const prefix = `[data-tutorial-prompt-id="${cssAttributeValue(prompt.promptId)}"]`
  const selected = domState.value.responsePassSelected
  return {
    eyebrow: '进攻中的响应机会', title: selected ? '确认不响应' : '先处理当前响应',
    body: '这次先选择“不响应”，再确认。不响应不会取消进攻，也不代表选择不抵挡。',
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
    reason: '先准备好正确的练习场面，提示才能指向要操作的位置。', outcome: '场面出现后进入下一步。',
  }
  if (ownResponseGuide.value) return ownResponseGuide.value
  if (!dialogueDone.value && dialoguePage.value) return dialoguePage.value
  return props.tutorial.status === 'completed' ? dialoguePage.value : actionGuide.value
})

function advanceDialogue() {
  if (instructionBlocked.value) return
  if (props.tutorial.lessonId === 'finale') finaleIntroduced.value = true
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
let lastRevealedChoiceAnchor = ''

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
  const anchor = !minimized.value ? visibleGuide.value?.anchor : null
  if (typeof document === 'undefined') return
  if (!anchor) { observeGeometry([]); return }
  const viewportSize = visibleViewport()
  const viewport = { left: 0, top: 0, width: viewportSize.width, height: viewportSize.height }
  const roots = Array.from(document.querySelectorAll(anchor.selector)).filter((element): element is HTMLElement => element instanceof HTMLElement)
  const revealIdentity = `${props.tutorial.matchId}:${props.tutorial.stepId}:${anchor.selector}:${document.documentElement.dataset.l12Rotated}`
  const choiceTarget = roots.map(element => element.closest<HTMLElement>('.prompt-card-candidate'))
    .find(element => element?.closest('.prompt-choice-body'))
  if (document.documentElement.dataset.l12Mobile === 'true' && choiceTarget && lastRevealedChoiceAnchor !== revealIdentity) {
    lastRevealedChoiceAnchor = revealIdentity
    // A teaching reference must reveal its real interaction, not highlight a
    // clipped portion. Reveal once per bound step/orientation, not for toolbar
    // resizes or subsequent scrolling by the player.
    choiceTarget.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' })
  }
  const visible = roots.filter(element => visibleElement(element, viewport))
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
    const targetX = panelSide.value === 'right' ? target.left + target.width : target.left
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
  void nextTick(() => { updateGeometry(); attemptCompletedHandoff() })
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

function completedResultVisible() {
  if (typeof document === 'undefined') return false
  const anchor = visibleGuide.value?.anchor
  const selector = props.tutorial.lessonId === 'finale' ? '.game-over[role="dialog"]' : anchor?.selector
    ?? `${tutorialPlayerSelector(facts.value.self.playerIndex)} [data-tutorial-anchor="master"]`
  const viewportSize = visibleViewport()
  const viewport = { left: 0, top: 0, width: viewportSize.width, height: viewportSize.height }
  return Array.from(document.querySelectorAll(selector))
    .some(element => element instanceof HTMLElement && visibleElement(element, viewport))
}

function attemptCompletedHandoff() {
  const key = completedIdentity.value
  if (!props.connected || props.busy || props.presentationBusy || props.progressRetryRequired || !stateAligned.value || props.tutorial.status !== 'completed'
    || props.tutorial.stepId !== 'completed' || props.acceptedHandoffIdentity === key
    || !completedResultVisible()) return
  emit('continueMainline')
}

watch(() => `${props.tutorial.matchId}:${props.tutorial.lessonId}:${props.tutorial.version}:${props.tutorial.recipeId || ''}`, () => {
  finaleIntroduced.value = false
  if (props.tutorial.matchId !== props.game.matchId) {
    practiceInstanceId.value = null
    return
  }
  const next = tutorialFacts(props.tutorial, props.game)
  practiceInstanceId.value = next.practiceInstanceId
  baseline.value = tutorialSnapshot(next)
}, { flush: 'sync', immediate: true })

watch(() => [stateAligned.value, facts.value.practiceInstanceId] as const, ([aligned, instanceId]) => {
  if (aligned && !practiceInstanceId.value && instanceId) {
    practiceInstanceId.value = instanceId
    baseline.value = tutorialSnapshot(facts.value)
  }
}, { flush: 'sync' })

watch(() => `${props.tutorial.matchId}:${props.tutorial.lessonId}:${props.tutorial.stepId}:${props.tutorial.status}:${props.game.prompts?.find(prompt => prompt.playerIndex === props.game.you)?.promptId || ''}`, () => {
  minimized.value = false
  dialogueIndex.value = 0
  queueRefresh()
}, { flush: 'sync' })

watch(() => [completedIdentity.value, props.tutorial.status, props.connected, stateAligned.value,
  props.busy, props.presentationBusy, props.acceptedHandoffIdentity, props.progressRetryRequired] as const, queueRefresh, { immediate: true, flush: 'post' })

watch(() => [props.game.revision, props.game.phase, props.game.prompts?.map(prompt => prompt.promptId).join('|'),
  dialogueIndex.value, minimized.value], queueRefresh, { flush: 'post' })
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
  <BattleOverlayPortal force>
    <div class="tutorial-guide-layer" aria-hidden="true">
      <div v-for="(rect, index) in focusRects" :key="index" class="tutorial-focus-ring"
        :style="{ left: `${rect.left - 5}px`, top: `${rect.top - 5}px`, width: `${rect.width + 10}px`, height: `${rect.height + 10}px` }" />
      <svg v-if="arrow" class="tutorial-guide-arrow" :viewBox="`0 0 ${visibleViewport().width} ${visibleViewport().height}`" preserveAspectRatio="none">
        <defs><marker id="tutorial-arrow-head" markerWidth="10" markerHeight="10" refX="8" refY="3" orient="auto"><path d="M0,0 L0,6 L9,3 z" /></marker></defs>
        <path :d="`M ${arrow.x1} ${arrow.y1} L ${arrow.x2} ${arrow.y2}`" marker-end="url(#tutorial-arrow-head)" />
      </svg>
    </div>
    <div v-if="guidanceVisible" ref="panelElement" class="tutorial-guidance-shell"
      :class="[`panel-${panelSide}`, { completed: tutorial.status === 'completed' }]"
      data-ui-contract="tutorial-authoritative-guidance"
      :data-tutorial-guide-id="dialogueDone ? (tutorial.status === 'completed' ? 'result' : 'action') : dialoguePage?.id"
      :data-tutorial-anchor-expected="Boolean(visibleGuide?.anchor)"
      :data-tutorial-scene="tutorial.lessonId" :data-tutorial-reading="!ownResponseGuide && !dialogueDone">
      <section class="tutorial-guidance" aria-live="polite">
        <header>
          <div><small>{{ visibleGuide?.eyebrow || '新手教学' }}</small><h2>{{ visibleGuide?.title || lessonTitle }}</h2></div>
          <button type="button" aria-label="最小化教学说明" @click="minimized = true">−</button>
        </header>

        <p>{{ visibleGuide?.body }}</p>
        <p v-if="progressRetryRequired" class="tutorial-anchor-unavailable" role="status">进度暂时无法确认。<button type="button" :disabled="busy || !connected" @click="emit('retryProgressSave')">重试进度</button></p>
        <p v-else-if="unsavedProgress" class="tutorial-anchor-unavailable" role="status">进度暂未保存，当前学习可继续。<button type="button" @click="emit('retryProgressSave')">重试保存</button></p>
        <p v-if="anchorUnavailable" class="tutorial-anchor-unavailable" role="status">{{ anchorUnavailable }}</p>
        <div v-if="!ownResponseGuide && !dialogueDone && tutorial.status !== 'completed'" class="tutorial-main-action">
          <button class="tutorial-primary" type="button" :disabled="instructionBlocked || !stateAligned" @click="advanceDialogue">
            {{ instructionBlocked ? '请稍候…' : '下一步' }}
          </button>
        </div>

        <details class="tutorial-secondary-actions">
          <summary>教学选项</summary>
          <div><button type="button" :disabled="busy" @click="restartScene">重学本段</button>
            <button type="button" :disabled="busy" @click="exitTutorial">退出教学</button></div>
        </details>
      </section>
    </div>
  </BattleOverlayPortal>
</template>

<style scoped>
.tutorial-guide-layer{position:fixed;z-index:2147483633;inset:0;pointer-events:none}.tutorial-focus-ring{position:fixed;box-sizing:border-box;border:3px solid #f5d45f;border-radius:10px;box-shadow:0 0 0 3px #071014cc,0 0 22px #f5d45fcc;animation:tutorial-guide-pulse 1.4s ease-in-out infinite;pointer-events:none}.tutorial-guide-arrow{position:fixed;inset:0;width:100%;height:100%;overflow:visible;pointer-events:none}.tutorial-guide-arrow path{fill:none;stroke:#f5d45f;stroke-width:4;stroke-linecap:round;filter:drop-shadow(0 2px 2px #000)}.tutorial-guide-arrow marker path{fill:#f5d45f;stroke:none}@keyframes tutorial-guide-pulse{50%{border-color:#fff2a7;box-shadow:0 0 0 4px #071014cc,0 0 30px #fff2a799}}
.tutorial-guidance-shell{position:fixed;z-index:2147483634;top:16px;box-sizing:border-box;width:min(354px,calc(var(--l12-viewport-width,100vw) - 28px));max-height:calc(var(--l12-viewport-height,100dvh) - 28px);overflow:auto;pointer-events:none;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.tutorial-guidance-shell.panel-left{left:14px;right:auto}.tutorial-guidance-shell.panel-right{right:14px;left:auto}.tutorial-guidance{box-sizing:border-box;width:100%;min-width:0;padding:15px 16px 12px;border:1px solid #50636b;background:linear-gradient(150deg,#101b22f7,#081116f7);color:#f0f2ee;box-shadow:0 12px 30px #000b,0 0 0 1px #9acfd111 inset;pointer-events:auto}.tutorial-guidance-shell.completed .tutorial-guidance{border-color:#a58c48;background:#171408f7}.tutorial-guidance header{display:flex;align-items:flex-start;justify-content:space-between;gap:12px}.tutorial-guidance small{color:#70cdd1;font-size:11px;font-weight:800;letter-spacing:.12em}.tutorial-guidance h2{margin:4px 0 0;font-size:17px;line-height:1.35}.tutorial-guidance header button{min-width:40px;min-height:40px;border:1px solid #465a63;background:#132129;color:#dce8e7;font:800 18px inherit}.tutorial-guidance p{margin:9px 0 0;color:#d4dedc;font-size:13px;font-weight:500;line-height:1.6}.tutorial-anchor-unavailable{padding:7px 9px;border-left:3px solid #d4b657;background:#241d0d;color:#f4d994!important}.tutorial-main-action{display:flex;justify-content:flex-end;margin:11px 0 0}.tutorial-guidance button{min-height:42px;padding:7px 12px;border:1px solid #4b616b;background:#132129;color:#edf4f2;font:800 13px inherit}.tutorial-guidance button:disabled{cursor:not-allowed;opacity:.55}.tutorial-guidance .tutorial-primary{min-width:108px;border-color:#d9bc62;background:#d9bc62;color:#101719;box-shadow:0 4px 12px #0004}.tutorial-secondary-actions{margin-top:9px;padding-top:8px;border-top:1px solid #2f4149;color:#91a4a8;font-size:12px}.tutorial-secondary-actions summary{width:max-content;cursor:pointer;user-select:none}.tutorial-secondary-actions>div{display:flex;gap:7px;margin-top:8px}.tutorial-secondary-actions button{min-width:0;flex:1;min-height:38px;color:#c9d6d4;background:#101b21}.tutorial-guidance-restore{z-index:var(--l12-battle-fixed-controls-z,5100);min-height:44px;padding:9px 13px;border:1px solid #5c6b73;background:#091118f2;color:#ecd47f;box-shadow:0 14px 38px #000b;font:900 14px 'Microsoft YaHei','微软雅黑',sans-serif}
@media(max-height:480px) and (min-width:650px){.tutorial-guidance-shell{width:min(286px,calc(var(--l12-viewport-width,100vw) - 176px));top:8px;max-height:calc(100dvh - 16px)}.tutorial-guidance-shell .tutorial-guidance{padding:11px 12px}.tutorial-guidance-shell h2{font-size:15px}.tutorial-guidance-shell p{font-size:12px;line-height:1.5}.tutorial-secondary-actions{margin-top:6px;padding-top:6px}}
@media(max-width:480px){.tutorial-secondary-actions{margin-top:6px;padding-top:6px}}
:global(html[data-l12-mobile=true] #l12-landscape-teleports > .tutorial-guidance-shell){width:min(350px,calc(var(--l12-viewport-width,100vw) - 188px));max-width:calc(var(--l12-viewport-width,100vw) - 188px)}:global(html[data-l12-mobile=true] #l12-landscape-teleports > .tutorial-guidance-shell.panel-left){left:86px}:global(html[data-l12-mobile=true] #l12-landscape-teleports > .tutorial-guidance-shell.panel-right){right:86px}:global(html[data-l12-mobile=true] #l12-landscape-teleports .mobile-battle-dock__context > .tutorial-guidance-restore){position:static!important;box-sizing:border-box;width:100%;max-width:100%;min-height:44px!important;padding:8px 4px!important;white-space:normal;overflow-wrap:normal}
/* The landscape portal enables pointer input on its direct children. The
   teaching focus is decorative, while only the dialogue itself is interactive. */
:global(#l12-landscape-teleports > .tutorial-guide-layer),:global(#l12-landscape-teleports > .tutorial-guide-layer *){pointer-events:none}
:global(#l12-landscape-teleports > .tutorial-guidance-shell){pointer-events:none}
.tutorial-focus-ring{border-width:2px;border-radius:6px;box-shadow:0 0 0 2px #071014dd,0 0 12px #f5d45f66;animation:none}.tutorial-guide-arrow path{stroke-width:2.5;filter:drop-shadow(0 1px 1px #0008)}
/* Preparation has a card-selection surface of its own. Keep it beside the
   teaching dialogue instead of obscuring the first disaster or starting hand. */
:global(html[data-l12-mobile=true] #l12-landscape-teleports > .tutorial-guidance-shell[data-tutorial-scene=preparation]){left:10px;right:auto;top:8px;width:280px;max-width:280px}
:global(html[data-l12-mobile=true] #l12-landscape-teleports:has(.tutorial-guidance-shell[data-tutorial-scene=preparation]) .l12-prompt-overlay:not(.minimized)){padding-left:304px!important;padding-right:102px!important}
:global(html[data-l12-mobile=true] #l12-landscape-teleports:has(.tutorial-guidance-shell[data-tutorial-scene=preparation]) .l12-prompt-overlay:not(.minimized) .prompt-panel){box-sizing:border-box;width:100%!important;max-width:100%!important;margin:auto;padding:12px}
</style>
