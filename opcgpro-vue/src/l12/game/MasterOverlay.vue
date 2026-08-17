<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { PlayerView } from '../types'

const props = defineProps<{ player: PlayerView; mine: boolean; canActivate: boolean; busy?: boolean }>()
const emit = defineEmits<{ close: []; activate: [ability: string] }>()
const minimized = ref(false)
watch(() => props.player.playerIndex, () => { minimized.value = false })

const abilities = computed<Array<[string, string]>>(() => {
  if (!props.mine) return []
  if (props.player.master.abilities?.length) return props.player.master.abilities.map(entry => [entry.id, entry.label])
  if (props.player.master.masterId === 'S01-01M1') return [
    ['drawCycle', '消耗 1 张活跃士气：抽 1 张牌，再将 1 张手牌放回牌库顶部或底部。'],
    ['nonLethal', '返还 4 张士气：对方主宰失去 1 点血量，此效果不能令其血量低于 1。'],
  ]
  if (props.player.master.masterId === 'S01-04M2') return [
    ['frontBuff', '消耗 1 张活跃士气：选择我方 1 张【高天原】军团发动前排强化。'],
    ['kusanagi', '消耗 2 张活跃士气：将圣物区的〈草雉剑〉作为军团置入我方前排。'],
  ]
  return []
})
</script>

<template>
  <Teleport to="body">
    <div class="master-overlay" :class="{ minimized }" @click.self="emit('close')">
      <section v-if="minimized" class="master-minimized">
        <strong>{{ player.master.masterName }} · 主宰效果</strong>
        <button @click="minimized = false">展开</button>
        <button aria-label="关闭" @click="emit('close')">×</button>
      </section>
      <section v-else class="master-dialog" role="dialog" aria-modal="true" :aria-label="`${player.master.masterName}主宰效果`">
        <button class="master-minimize" aria-label="最小化弹框" title="最小化以查看场面" @click="minimized = true">—</button>
        <button class="master-close" aria-label="关闭" @click="emit('close')">×</button>
        <img v-if="player.master.masterImageUrl" :src="player.master.masterImageUrl" :alt="player.master.masterName" />
        <div class="master-content">
          <small>{{ mine ? '我方主宰' : '对方主宰' }}</small>
          <h2>{{ player.master.masterName }}</h2>
          <b>血量 {{ player.master.hp }}/{{ player.master.maxHp }}</b>
          <p>{{ player.master.effectText || '暂无效果文字' }}</p>
          <article v-if="player.master.masterId === 'S01-01M1'" class="master-related-card">
            <img src="/cards/faces/天廷/哮天犬·稚.png" alt="哮天犬·稚" />
            <div><strong>【杨戬专属】哮天犬·稚</strong><p>我方 回合1次 我方士气因主宰效果返还4张及以上时，&lt;哮天犬·稚&gt;可在前排活跃登场，视为1张兵力2000的【特殊】军团。\n阵亡时 可从士气牌库追加1张休整的士气。</p></div>
          </article>
          <div v-if="abilities.length" class="master-abilities">
            <button v-for="entry in abilities" :key="entry[0]" :disabled="!canActivate || busy" @click="emit('activate', entry[0])">
              <span>{{ entry[1] }}</span>
            </button>
          </div>
          <span v-if="mine && !canActivate" class="master-hint">仅在我方主要阶段可以发动主宰效果</span>
        </div>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.master-related-card{display:grid;grid-template-columns:72px 1fr;gap:10px;margin-top:12px;padding:8px;border:1px solid #736731;background:#15150f}.master-related-card>img{width:72px;height:101px;object-fit:contain}.master-related-card strong{color:#e4c653;font-size:11px}.master-related-card p{margin:5px 0 0;color:#ddd9c9;font-size:9px;line-height:1.6;white-space:pre-wrap}
.master-overlay{position:fixed;z-index:1080;inset:0;display:flex;align-items:center;justify-content:center;padding:18px;background:rgba(2,4,5,.7);backdrop-filter:blur(5px)}
.master-dialog{position:relative;display:grid;width:min(720px,calc(100vw - 36px));max-height:calc(100vh - 36px);grid-template-columns:220px 1fr;gap:22px;box-sizing:border-box;padding:22px;border:1px solid #ded9cc;background:linear-gradient(145deg,#171c1d,#07090a);box-shadow:0 24px 70px #000}.master-dialog>img{width:220px;height:308px;object-fit:contain;background:#050708}.master-content small{color:#70d7df;font-size:9px;letter-spacing:.15em}.master-content h2{margin:7px 0 4px;color:#fff;font-size:27px}.master-content>b{color:#d2525b;font-size:11px}.master-content>p{max-height:92px;overflow:auto;color:#d4d5cf;font-size:12px;font-weight:800;line-height:1.75;white-space:pre-wrap}.master-abilities{display:grid;gap:8px;margin-top:16px}.master-abilities button{padding:10px 12px;border:1px solid #70d7df;background:#132b2e;color:#fff;text-align:left}.master-abilities button:hover:not(:disabled){background:#1b6f77}.master-abilities button:disabled{border-color:#515755;background:#222625;color:#777}.master-abilities strong,.master-abilities span{display:block}.master-abilities strong{font-size:12px}.master-abilities span{margin-top:3px;color:#c6cbc5;font-size:9px;line-height:1.45}.master-hint{display:block;margin-top:13px;color:#8d9490;font-size:10px}.master-minimize,.master-close{position:absolute;top:9px;width:31px;height:29px;border:1px solid #777;background:#111;color:#fff;font-size:18px}.master-minimize{right:47px}.master-close{right:9px}.master-overlay.minimized{inset:auto 16px 16px auto;padding:0;background:transparent;backdrop-filter:none;pointer-events:none}.master-minimized{display:flex;align-items:center;gap:8px;padding:9px 10px;border:1px solid #ded9cc;background:#0c1112;box-shadow:0 12px 35px #000;pointer-events:auto}.master-minimized strong{max-width:280px;overflow:hidden;color:#fff;font-size:11px;text-overflow:ellipsis;white-space:nowrap}.master-minimized button{padding:6px 10px;border:1px solid #70d7df;background:#174e54;color:#fff}
@media(max-width:650px){.master-dialog{grid-template-columns:1fr;overflow:auto}.master-dialog>img{width:140px;height:196px;margin:auto}}
</style>
