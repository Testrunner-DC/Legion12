<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
const props = defineProps<{ modelValue: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()
function closeOnEscape(event: KeyboardEvent) { if (props.modelValue && event.key === 'Escape') emit('update:modelValue', false) }
onMounted(() => window.addEventListener('keydown', closeOnEscape))
onBeforeUnmount(() => window.removeEventListener('keydown', closeOnEscape))
</script>

<template>
  <Teleport to="body">
    <div v-if="modelValue" class="master-title-rules-backdrop" @click.self="emit('update:modelValue', false)">
      <section class="master-title-rules-modal" role="dialog" aria-modal="true" aria-labelledby="master-title-rules-heading">
        <header>
          <div><small>STRONGEST MASTER TITLE</small><h2 id="master-title-rules-heading">“最强”称号规则</h2></div>
          <button type="button" aria-label="关闭最强主宰称号规则" @click="emit('update:modelValue', false)">×</button>
        </header>
        <div class="l12-effect-body master-title-rules-body">
          <p>每个主宰都会单独评选一名全服最强使用者。评选固定采用近30日数据，不会随榜单当前选择的“近7天 / 近30天 / 全部”切换。</p>
          <ol><li>默认需使用该主宰完成至少40场有效公开对局；若该主宰近30日全服总场次少于200局，候选门槛降为20场。全服总场次按对局计，同主宰镜像局只算一局；个人场次按玩家实际出场计。</li>
          <li>候选人还需在至少5个UTC+8自然日参赛，并遇到至少10名不同对手；对手仅以服务器匿名标识去重。</li>
          <li>只统计排位匹配；其他不计入。</li>
          <li>少于6回合、掉线结束、没有明确胜负或同账号之间的对局不计入。</li>
          <li>候选人按贝叶斯修正胜率排名。服务器先用该主宰近30日其他玩家战绩作为基准；计算某位候选时会排除其本人对局，镜像局会整局排除，再以40场、50%胜率平滑基准。候选战绩最后按15场等效样本向该基准收缩，降低小样本连胜优势。<br/>得分相同时依次比较场次、胜场；仍相同时由服务器的稳定顺序选出唯一持有者。</li></ol>
        </div>
        <footer><button type="button" @click="emit('update:modelValue', false)">我知道了</button></footer>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.master-title-rules-body ol{margin:0;padding-left:1.6em}.master-title-rules-body li{margin:0 0 18px;line-height:1.8}
.master-title-rules-backdrop{position:fixed;z-index:5200;inset:0;display:grid;place-items:center;padding:20px;background:rgba(0,0,0,.76);backdrop-filter:blur(5px)}
.master-title-rules-modal{display:grid;grid-template-rows:auto minmax(0,1fr) auto;width:min(780px,96vw);height:min(820px,92vh);min-height:0;overflow:hidden;border:1px solid #a98d3f;background:#0d151d;color:#eef1ed;box-shadow:0 24px 80px #000}
.master-title-rules-modal>header{display:flex;align-items:flex-start;justify-content:space-between;padding:20px 22px;border-bottom:1px solid #34414a}.master-title-rules-modal header small{color:#58c6cd;font:900 14px monospace;letter-spacing:.16em}.master-title-rules-modal header h2{margin:6px 0 0;font-size:24px}.master-title-rules-modal header button{border:0;background:transparent;color:#c3cbce;font-size:30px}
.master-title-rules-body{display:grid;min-height:0;align-content:start;gap:12px;overflow-x:hidden;overflow-y:auto;padding:18px 22px;font-size:14px!important;overscroll-behavior:contain;scrollbar-gutter:stable}.master-title-rules-body article{padding:15px;border:1px solid #2c3943;background:#101b25}.master-title-rules-body h3{margin:0 0 9px;color:#e7ce72;font-size:16px}.master-title-rules-body p{margin:0;color:#c2cbce;font-size:14px!important;line-height:1.8}.master-title-rules-body code{color:#f2d67c;font-size:14px!important}
.master-title-rules-modal>footer{display:flex;justify-content:flex-end;padding:14px 22px;border-top:1px solid #34414a}.master-title-rules-modal>footer button{min-width:128px;padding:11px 24px;border:1px solid #ddc15a;background:#332a12;color:#f0d879;font-size:14px;font-weight:900}
.master-title-rules-modal small{font-size:14px!important}
@media(max-width:640px){.master-title-rules-backdrop{padding:8px}.master-title-rules-modal{width:100%;height:96vh}.master-title-rules-modal>header,.master-title-rules-body,.master-title-rules-modal>footer{padding-right:14px;padding-left:14px}}
</style>
