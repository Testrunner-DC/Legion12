<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import BattleDockPortal from '../game/BattleDockPortal.vue'
import BattleOverlayPortal from '../game/BattleOverlayPortal.vue'
import type { ActiveTutorialState } from '../types'

const props = defineProps<{ tutorial: ActiveTutorialState }>()
const minimized = ref(false)

const lessonTitle = computed(() => ({ deploy: '部署与支付', move: '普通移动', combat: '进攻与防守' })[props.tutorial.lessonId])
const stepCopy = computed(() => {
  if (props.tutorial.status === 'completed') return '本关完成，可查看场面变化或返回目录。'
  const key = `${props.tutorial.lessonId}:${props.tutorial.stepId}`
  return ({
    'deploy:deploy': '从手牌选择 1 费《西施》，点选亮起的空阵地，并按支付提示确认费用。',
    'move:move': '选择后排中格的《西施》，使用普通移动，支付费用后点选亮起的相邻空格。',
    'combat:attack': '选择我方前排左格的练习军团，再进攻对方主宰。',
    'combat:end-turn': '进攻已经完成。结束当前回合，等待对手按合法动作发起进攻。',
    'combat:defend': '在防守阶段选择合法抵挡，或明确选择不抵挡，再确认本次防守。',
  } as Record<string, string>)[key] || '按当前场面中亮起的提示完成操作。'
})

watch(() => `${props.tutorial.matchId}:${props.tutorial.lessonId}:${props.tutorial.stepId}`, () => {
  minimized.value = false
}, { flush: 'sync' })
</script>

<template>
  <BattleDockPortal lane="context">
    <button v-if="minimized" class="tutorial-guidance-restore" type="button" aria-label="展开教学说明" @click="minimized = false">教学提示</button>
  </BattleDockPortal>
  <BattleOverlayPortal>
    <div v-if="!minimized" class="tutorial-guidance-shell" data-ui-contract="tutorial-authoritative-guidance">
      <section :key="`${tutorial.matchId}:${tutorial.stepId}`" class="tutorial-guidance" :class="{ completed: tutorial.status === 'completed' }" aria-live="polite">
        <header><div><small>新手教学</small><h2>{{ lessonTitle }}</h2></div><button type="button" aria-label="最小化教学说明" @click="minimized = true">−</button></header>
        <p>{{ stepCopy }}</p>
        <details><summary>学习提示</summary><span>先观察亮起的卡牌、格位和按钮；支付或防守提示出现后，再完成对应选择。</span></details>
      </section>
    </div>
  </BattleOverlayPortal>
</template>

<style scoped>
.tutorial-guidance-shell{position:fixed;z-index:var(--l12-battle-fixed-controls-z,5100);left:14px;top:14px;width:min(390px,calc(100vw - 28px));pointer-events:none;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.tutorial-guidance,.tutorial-guidance-restore{pointer-events:auto;border:1px solid #5c6b73;background:#091118f2;color:#f0f2ee;box-shadow:0 14px 38px #000b}.tutorial-guidance{padding:13px 15px}.tutorial-guidance.completed{border-color:#b59a4b;background:#171408f2}.tutorial-guidance header{display:flex;align-items:flex-start;justify-content:space-between;gap:12px}.tutorial-guidance small{color:#62cbd1;font-size:12px;font-weight:900;letter-spacing:.08em}.tutorial-guidance h2{margin:3px 0 0;font-size:18px}.tutorial-guidance header button{min-width:44px;min-height:44px;border:1px solid #57656d;background:#151e24;color:#fff;font:900 18px inherit}.tutorial-guidance p{margin:10px 0;color:#d5dcdd;font-size:14px;font-weight:800;line-height:1.65}.tutorial-guidance details{border-top:1px solid #344149;padding-top:8px;color:#94a2a7;font-size:13px;line-height:1.55}.tutorial-guidance summary{min-height:44px;display:flex;align-items:center;cursor:pointer;color:#d9c474;font-weight:900}.tutorial-guidance details span{display:block;margin-top:7px}.tutorial-guidance-restore{min-height:44px;padding:9px 13px;color:#ecd47f;font:900 14px inherit}
.tutorial-guidance-shell{box-sizing:border-box;max-height:calc(var(--l12-viewport-height,100dvh) - 28px);overflow:auto}.tutorial-guidance{box-sizing:border-box;width:100%;min-width:0}.tutorial-guidance-restore{position:fixed;z-index:var(--l12-battle-fixed-controls-z,5100);left:14px;top:14px}
:global(html[data-l12-mobile=true] #l12-landscape-teleports > .tutorial-guidance-shell){left:90px;width:min(360px,calc(var(--l12-viewport-width,100vw) - 200px));max-width:calc(var(--l12-viewport-width,100vw) - 200px)}
:global(html[data-l12-mobile=true] #l12-landscape-teleports .mobile-battle-dock__context > .tutorial-guidance-restore){position:static!important;box-sizing:border-box;width:100%;max-width:100%;min-height:44px!important;padding:8px 4px!important;white-space:normal;overflow-wrap:normal}
</style>
