<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import CardImage from '../CardImage.vue'
import { roundCardUrl } from '../specialAssets'

const emit = defineEmits<{ complete: [] }>()
const stage = ref(0)
const jarIds = ['S01-0216', 'S01-0217', 'S01-0218', 'S01-0219', 'S01-0220']
const timers: Array<ReturnType<typeof setTimeout>> = []

onMounted(() => {
  jarIds.forEach((_, index) => timers.push(setTimeout(() => { stage.value = index + 1 }, 320 + index * 360)))
  timers.push(setTimeout(() => { stage.value = 6 }, 2350))
  timers.push(setTimeout(() => { stage.value = 7 }, 3050))
  timers.push(setTimeout(() => emit('complete'), 5450))
})
onBeforeUnmount(() => timers.forEach(timer => clearTimeout(timer)))
</script>

<template>
  <Teleport to="body">
    <div class="osiris-victory-sequence" data-ui-contract="osiris-special-victory-sequence" role="status"
      aria-label="复苏的奥西里斯特殊胜利动画">
      <div class="osiris-void" />
      <div class="canopic-pentagram">
        <i class="pentagram-lines" />
        <span v-for="(cardId, index) in jarIds" :key="cardId" class="canopic-vessel"
          :class="[`vessel-${index + 1}`, { visible: stage >= index + 1 }]">
          <img :src="roundCardUrl(cardId)" alt="卡诺匹斯圣物" />
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
.osiris-victory-sequence{position:fixed;z-index:2147483646;inset:0;display:grid;place-items:center;overflow:hidden;background:radial-gradient(circle at 50% 48%,rgba(20,93,70,.44),rgba(0,5,6,.97) 58%,#000);pointer-events:all}.osiris-void{position:absolute;inset:0;background:repeating-radial-gradient(circle at center,transparent 0 32px,rgba(84,217,168,.045) 34px 35px);animation:osiris-void-turn 12s linear infinite}.canopic-pentagram{position:absolute;left:50%;top:50%;width:min(68vw,590px);aspect-ratio:1;transform:translate(-50%,-50%)}.pentagram-lines{position:absolute;inset:12%;opacity:.55;background:conic-gradient(from 18deg,transparent 0 7%,rgba(105,245,194,.58) 7.5% 8%,transparent 8.5% 27%,rgba(105,245,194,.58) 27.5% 28%,transparent 28.5% 47%,rgba(105,245,194,.58) 47.5% 48%,transparent 48.5% 67%,rgba(105,245,194,.58) 67.5% 68%,transparent 68.5% 87%,rgba(105,245,194,.58) 87.5% 88%,transparent 88.5%);clip-path:polygon(50% 96%,61% 62%,98% 61%,68% 40%,79% 5%,50% 27%,21% 5%,32% 40%,2% 61%,39% 62%);filter:drop-shadow(0 0 8px #62e4b6);animation:osiris-lines 2.8s ease-out both}.canopic-vessel{position:absolute;width:62px;height:62px;opacity:0;transform:translate(-50%,-50%) scale(.25);filter:drop-shadow(0 0 10px rgba(103,242,190,.8));transition:opacity .25s ease,transform .45s cubic-bezier(.2,1.45,.35,1)}.canopic-vessel.visible{opacity:1;transform:translate(-50%,-50%) scale(.78)}.canopic-vessel img{width:100%;height:100%;object-fit:contain}.vessel-1{left:50%;top:92%}.vessel-2{left:7%;top:58%}.vessel-3{left:23%;top:10%}.vessel-4{left:77%;top:10%}.vessel-5{left:93%;top:58%}.osiris-card{position:absolute;left:50%;top:50%;width:min(180px,28vw);aspect-ratio:5/7;opacity:0;transform:translate(-50%,-46%) scale(.42);transition:opacity .45s ease,transform .72s cubic-bezier(.18,1.4,.28,1);filter:drop-shadow(0 0 18px rgba(117,255,203,.85)) drop-shadow(0 18px 28px #000)}.osiris-card.visible{opacity:1;transform:translate(-50%,-50%) scale(1)}.osiris-card :deep(.l12-card-image){width:100%;height:100%}.osiris-card strong{position:absolute;left:50%;bottom:-34px;transform:translateX(-50%);color:#dffff1;font:900 17px Georgia,'Songti SC',serif;letter-spacing:.18em;text-shadow:0 0 12px #55f0b4;white-space:nowrap}.osiris-flash{position:absolute;inset:-20%;opacity:0;background:radial-gradient(circle,#fff 0 2%,#b7ffe2 4%,rgba(65,242,172,.34) 18%,transparent 46%);pointer-events:none}.osiris-flash.active{animation:osiris-flash 1.1s ease-out}.osiris-card.visible::after{content:'';position:absolute;inset:-14px;border:2px solid rgba(134,255,210,.65);animation:osiris-aura 1.4s ease-in-out infinite}
@keyframes osiris-lines{from{opacity:0;transform:scale(.55) rotate(22deg)}to{opacity:.55;transform:scale(1)}}@keyframes osiris-void-turn{to{transform:rotate(360deg) scale(1.05)}}@keyframes osiris-flash{0%{opacity:0}18%{opacity:1}45%{opacity:.55}100%{opacity:0}}@keyframes osiris-aura{50%{opacity:.25;transform:scale(1.08)}}
@media(max-width:700px){.canopic-vessel{width:48px;height:48px}.osiris-card strong{font-size:12px}}
@media(prefers-reduced-motion:reduce){.osiris-void,.pentagram-lines,.osiris-flash.active,.osiris-card.visible::after{animation:none}.canopic-vessel,.osiris-card{transition-duration:.01s}}
</style>
