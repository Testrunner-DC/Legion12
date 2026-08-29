<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import CardImage from '../CardImage.vue'
import { playL12OsirisVictorySound } from './useL12ActionAudio'

const emit = defineEmits<{ complete: [] }>()
const stage = ref(0)
const jarIds = ['S01-0216', 'S01-0217', 'S01-0218', 'S01-0219', 'S01-0220']
const timers: Array<ReturnType<typeof setTimeout>> = []

onMounted(() => {
  playL12OsirisVictorySound()
  jarIds.forEach((_, index) => timers.push(setTimeout(() => { stage.value = index + 1 }, 650 + index * 700)))
  timers.push(setTimeout(() => { stage.value = 6 }, 4700))
  timers.push(setTimeout(() => { stage.value = 7 }, 5650))
  timers.push(setTimeout(() => emit('complete'), 7000))
})
onBeforeUnmount(() => timers.forEach(timer => clearTimeout(timer)))
</script>

<template>
  <Teleport to="body">
    <div class="osiris-victory-sequence" data-ui-contract="osiris-special-victory-sequence" role="status"
      aria-label="复苏的奥西里斯特殊胜利动画">
      <div class="osiris-void" />
      <div class="canopic-pentagram">
        <svg class="pentagram-lines" viewBox="0 0 100 100" aria-hidden="true">
          <polyline points="50,92 20,18 92,56 8,56 80,18 50,92" pathLength="100" />
        </svg>
        <span v-for="(cardId, index) in jarIds" :key="cardId" class="canopic-vessel"
          :class="[`vessel-${index + 1}`, { visible: stage >= index + 1 }]">
          <CardImage :card-id="cardId" alt="卡诺匹斯圣物" intent="board" eager />
        </span>
      </div>
      <div class="osiris-card" :class="{ visible: stage >= 6 }">
        <CardImage card-id="S01-02M2" alt="复苏的奥西里斯" intent="detail" eager />
        <strong>复苏的奥西里斯</strong>
      </div>
      <div class="osiris-flash" :class="{ active: stage >= 7 }" />
    </div>
  </Teleport>
</template>

<style scoped>
.osiris-victory-sequence{position:fixed;z-index:2147483646;inset:0;display:grid;place-items:center;overflow:hidden;background:radial-gradient(circle at 50% 48%,rgba(158,118,19,.5),rgba(34,23,2,.97) 58%,#080500);pointer-events:all}.osiris-void{position:absolute;inset:0;background:repeating-radial-gradient(circle at center,transparent 0 32px,rgba(255,220,93,.055) 34px 35px);animation:osiris-void-turn 14s linear infinite}.canopic-pentagram{position:absolute;left:50%;top:50%;width:min(70vw,620px);aspect-ratio:1;transform:translate(-50%,-50%)}.pentagram-lines{position:absolute;inset:0;width:100%;height:100%;overflow:visible;filter:drop-shadow(0 0 10px #e8bb39)}.pentagram-lines polyline{fill:none;stroke:#e6bd4d;stroke-width:.7;stroke-linecap:round;stroke-linejoin:round;stroke-dasharray:100;stroke-dashoffset:100;animation:osiris-lines 4.4s .35s ease-in-out forwards}.canopic-vessel{position:absolute;width:min(104px,14vw);aspect-ratio:5/7;opacity:0;transform:translate(-50%,-50%) scale(.35);filter:drop-shadow(0 0 12px rgba(255,211,78,.8));transition:opacity .3s ease,transform .58s cubic-bezier(.2,1.3,.35,1)}.canopic-vessel.visible{opacity:1;transform:translate(-50%,-50%) scale(1)}.canopic-vessel :deep(.l12-card-image){width:100%;height:100%}.vessel-1{left:50%;top:92%}.vessel-2{left:20%;top:18%}.vessel-3{left:92%;top:56%}.vessel-4{left:8%;top:56%}.vessel-5{left:80%;top:18%}.osiris-card{position:absolute;left:50%;top:50%;width:min(190px,29vw);aspect-ratio:5/7;opacity:0;transform:translate(-50%,-46%) scale(.42);transition:opacity .55s ease,transform .85s cubic-bezier(.18,1.32,.28,1);filter:drop-shadow(0 0 22px rgba(255,219,94,.92)) drop-shadow(0 18px 28px #000)}.osiris-card.visible{opacity:1;transform:translate(-50%,-50%) scale(1)}.osiris-card :deep(.l12-card-image){width:100%;height:100%}.osiris-card strong{position:absolute;left:50%;bottom:-34px;transform:translateX(-50%);color:#fff0a8;font:900 17px Georgia,'Songti SC',serif;letter-spacing:.18em;text-shadow:0 0 12px #e8b82f;white-space:nowrap}.osiris-flash{position:absolute;inset:-20%;opacity:0;background:radial-gradient(circle,#fff 0 2%,#fff1a6 4%,rgba(255,205,54,.4) 18%,transparent 46%);pointer-events:none}.osiris-flash.active{animation:osiris-flash 1.25s ease-out}.osiris-card.visible::after{content:'';position:absolute;inset:-14px;border:2px solid rgba(255,224,110,.72);animation:osiris-aura 1.6s ease-in-out infinite}
@keyframes osiris-lines{to{stroke-dashoffset:0}}@keyframes osiris-void-turn{to{transform:rotate(360deg) scale(1.05)}}@keyframes osiris-flash{0%{opacity:0}18%{opacity:1}48%{opacity:.58}100%{opacity:0}}@keyframes osiris-aura{50%{opacity:.25;transform:scale(1.08)}}
@media(max-width:700px){.canopic-vessel{width:min(76px,16vw)}.osiris-card strong{font-size:12px}}
@media(prefers-reduced-motion:reduce){.osiris-void,.pentagram-lines,.osiris-flash.active,.osiris-card.visible::after{animation:none}.canopic-vessel,.osiris-card{transition-duration:.01s}}
</style>
