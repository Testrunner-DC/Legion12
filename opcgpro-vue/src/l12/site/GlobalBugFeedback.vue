<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { l12State } from '@/l12/net'
import { platformState, submitBug } from '@/l12/platform'

const route = useRoute()
const open = ref(false)
const busy = ref(false)
const message = ref('')
const form = reactive({ title: '', description: '' })

async function submit() {
  if (!form.description.trim()) { message.value = '请填写可以复现问题的描述'; return }
  busy.value = true
  message.value = ''
  try {
    const report = await submitBug({
      title: form.title,
      description: form.description,
      page: `${route.fullPath} · ${navigator.userAgent}`,
      roomCode: l12State.room?.roomCode,
      matchId: l12State.game?.matchId,
      version: String(import.meta.env.VITE_APP_VERSION || 'dev'),
    })
    message.value = `已提交：${report.id}`
    form.title = ''
    form.description = ''
  } catch (error) { message.value = error instanceof Error ? error.message : '提交失败' }
  finally { busy.value = false }
}
</script>

<template>
  <button class="bug-feedback-trigger" type="button" @click="open = true">反馈 Bug</button>
  <Teleport to="body">
    <div v-if="open" class="bug-feedback-mask" @click.self="open = false">
      <section class="bug-feedback-dialog" role="dialog" aria-modal="true" aria-label="反馈 Bug">
        <header><div><small>BUG REPORT</small><h2>反馈 Bug</h2></div><button @click="open = false">×</button></header>
        <p>请写明发生步骤、预期结果和实际结果。页面、房间、对局、版本与时间会自动附带。</p>
        <label>标题（可选）<input v-model="form.title" maxlength="100" placeholder="一句话概括问题"/></label>
        <label>问题描述<textarea v-model="form.description" maxlength="5000" rows="8" placeholder="例如：在主要阶段点击……后，预期……，实际……"/></label>
        <div class="bug-context"><span>提交身份：{{ platformState.account?.username || '匿名玩家' }}</span><span>页面：{{ route.fullPath }}</span><span v-if="l12State.room">房间：{{ l12State.room.roomCode }}</span></div>
        <p v-if="message" class="bug-message">{{ message }}</p>
        <footer><button @click="open = false">取消</button><button class="submit" :disabled="busy" @click="submit">{{ busy ? '提交中…' : '提交反馈' }}</button></footer>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.bug-feedback-trigger{position:fixed;z-index:1900;right:16px;bottom:16px;padding:10px 16px;border:1px solid #d2b861;background:#221c0d;color:#f6df91;box-shadow:0 8px 28px #000;font:900 11px 'Microsoft YaHei','微软雅黑',sans-serif}.bug-feedback-mask{position:fixed;z-index:5000;inset:0;display:grid;place-items:center;padding:20px;background:rgba(1,4,7,.78);backdrop-filter:blur(8px)}.bug-feedback-dialog{width:min(560px,95vw);padding:22px;border:1px solid #687277;background:#101820;color:#f3f0e8;box-shadow:0 30px 90px #000;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.bug-feedback-dialog header{display:flex;align-items:center;justify-content:space-between}.bug-feedback-dialog small{color:#d5b85e;font:900 9px monospace;letter-spacing:.18em}.bug-feedback-dialog h2{margin:4px 0;font-size:24px}.bug-feedback-dialog header button{width:34px;height:34px;border:1px solid #515d64;background:#080d11;color:#fff;font-size:20px}.bug-feedback-dialog>p{color:#89959b;font-size:11px;line-height:1.7}.bug-feedback-dialog label{display:block;margin-top:15px;color:#c7ccca;font-size:11px;font-weight:900}.bug-feedback-dialog input,.bug-feedback-dialog textarea{box-sizing:border-box;width:100%;margin-top:7px;padding:12px;border:1px solid #46535b;background:#070d12;color:#fff;font:700 12px 'Microsoft YaHei','微软雅黑';outline:none;resize:vertical}.bug-feedback-dialog input:focus,.bug-feedback-dialog textarea:focus{border-color:#56bec5}.bug-context{display:flex;flex-wrap:wrap;gap:6px;margin-top:12px}.bug-context span{padding:4px 7px;background:#172129;color:#88959a;font-size:9px}.bug-message{color:#e5c76d!important;font-weight:900}.bug-feedback-dialog footer{display:flex;justify-content:center;gap:10px;margin-top:18px}.bug-feedback-dialog footer button{min-width:120px;padding:11px;border:1px solid #5b676d;background:#121b22;color:#fff;font-weight:900}.bug-feedback-dialog footer .submit{border-color:#d8ba62;background:#d8ba62;color:#111}
@media(max-width:760px){.bug-feedback-trigger{right:10px;bottom:10px}.bug-feedback-dialog{padding:17px}}
</style>
