<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { emailApi, logout, requestPasswordReset, resetPassword } from '@/l12/platform'

const mode = ref<'request' | 'verify-email' | 'reset-password'>('request')
const token = ref('')
const form = reactive({ email: '', password: '', confirmPassword: '' })
const busy = ref(false)
const notice = ref('')
const complete = ref(false)
const capabilityLoaded = ref(false)
const emailFeatureEnabled = ref(false)
const title = computed(() => mode.value === 'verify-email' ? '验证邮箱'
  : mode.value === 'reset-password' ? '设置新密码' : '找回密码')

onMounted(async () => {
  const params = new URLSearchParams(location.search)
  const requestedMode = params.get('mode')
  if (requestedMode === 'verify-email' || requestedMode === 'reset-password') mode.value = requestedMode
  const fragment = new URLSearchParams(location.hash.replace(/^#/, ''))
  token.value = fragment.get('token') || ''
  if (location.hash) history.replaceState(null, '', `${location.pathname}${location.search}`)
  if (mode.value !== 'request' && !token.value) notice.value = '链接缺少令牌，请重新发起请求'
  try { emailFeatureEnabled.value = (await emailApi.capability()).enabled }
  catch { emailFeatureEnabled.value = false }
  finally { capabilityLoaded.value = true }
})

async function submitRequest() {
  busy.value = true; notice.value = ''
  try { const result = await requestPasswordReset(form.email); notice.value = result.message; complete.value = true }
  catch (error) { notice.value = error instanceof Error ? error.message : '请求失败' }
  finally { busy.value = false }
}

async function submitVerification() {
  if (!token.value) return
  busy.value = true; notice.value = ''
  try { const result = await emailApi.verify(token.value); notice.value = result.message; complete.value = true; token.value = '' }
  catch (error) { notice.value = error instanceof Error ? error.message : '验证失败' }
  finally { busy.value = false }
}

async function submitReset() {
  if (form.password !== form.confirmPassword) { notice.value = '两次输入的新密码不一致'; return }
  if (!token.value) { notice.value = '链接缺少令牌，请重新发起请求'; return }
  busy.value = true; notice.value = ''
  try { const result = await resetPassword(token.value, form.password); await logout({ revokeServer: false }); notice.value = result.message; complete.value = true; token.value = '' }
  catch (error) { notice.value = error instanceof Error ? error.message : '重置失败' }
  finally { busy.value = false }
}
</script>

<template>
  <main class="recovery-page">
    <section class="recovery-card">
      <small>ACCOUNT RECOVERY</small><h1>{{ title }}</h1>
      <p v-if="!capabilityLoaded">正在读取账号恢复能力…</p>
      <p v-else-if="!emailFeatureEnabled" class="feature-disabled">邮箱绑定、验证与找回功能当前未开放。</p>
      <template v-else-if="mode === 'request'">
        <p>仅已验证的绑定邮箱可以找回密码。无论邮箱是否存在，服务端都会返回相同提示。</p>
        <label>绑定邮箱<input v-model="form.email" type="email" maxlength="254" autocomplete="email"/></label>
        <button :disabled="busy || complete" @click="submitRequest">发送重置邮件</button>
      </template>
      <template v-else-if="mode === 'verify-email'">
        <p>确认后才会消费一次性验证令牌；验证完成前，原已验证邮箱保持有效。</p>
        <button :disabled="busy || complete || !token" @click="submitVerification">确认验证邮箱</button>
      </template>
      <template v-else>
        <p>重置成功后，账号在所有设备上的会话都会立即撤销。</p>
        <label>新密码<input v-model="form.password" type="password" minlength="8" maxlength="128" autocomplete="new-password"/></label>
        <label>再次输入<input v-model="form.confirmPassword" type="password" minlength="8" maxlength="128" autocomplete="new-password"/></label>
        <button :disabled="busy || complete || !token" @click="submitReset">重置密码</button>
      </template>
      <p v-if="notice" class="notice">{{ notice }}</p>
      <router-link to="/me">返回登录 / 我的 →</router-link>
    </section>
  </main>
</template>

<style scoped>
.recovery-page{display:grid;min-height:calc(100vh - 90px);place-items:center;padding:30px 16px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.recovery-card{box-sizing:border-box;width:min(520px,100%);padding:28px;border:1px solid #3a4850;background:#101821}.recovery-card>small{color:#58c7ce;font:900 9px monospace;letter-spacing:.18em}.recovery-card h1{margin:7px 0 12px}.recovery-card p{color:#879499;font-size:10px;line-height:1.7}.recovery-card label{display:block;margin:16px 0;color:#b8c0c1;font-size:10px;font-weight:900}.recovery-card input{box-sizing:border-box;width:100%;margin-top:7px;padding:11px;border:1px solid #4b5960;background:#080e13;color:#fff}.recovery-card button{display:block;width:100%;margin-top:16px;padding:11px;border:1px solid #dfbf68;background:#dfbf68;color:#080b0d;font-weight:900}.recovery-card button:disabled{opacity:.45}.recovery-card a{display:inline-block;margin-top:18px;color:#72cbd2;font-size:10px;text-decoration:none}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}
.feature-disabled{padding:12px;border-left:3px solid #8a6b32;background:#20190d;color:#d9c082!important}
</style>
