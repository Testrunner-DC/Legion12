<script setup lang="ts">
import { onMounted } from 'vue'

// Old mail links remain safe to open: discard their fragment, never consume a
// token, contact the recovery API, or change the current login/session.
onMounted(() => {
  if (location.hash) history.replaceState(null, '', `${location.pathname}${location.search}`)
})
</script>

<template>
  <main class="recovery-page">
    <section class="recovery-card">
      <h1>邮箱功能已停用</h1>
      <p>不再提供邮箱绑定、验证或密码找回。</p>
      <router-link to="/me">返回登录 / 我的 →</router-link>
    </section>
  </main>
</template>

<style scoped>
.recovery-page{display:grid;min-height:calc(100vh - 90px);place-items:center;padding:30px 16px;font-family:var(--l12-font-family,'Microsoft YaHei','微软雅黑',sans-serif)}
.recovery-card{box-sizing:border-box;width:min(520px,100%);padding:28px;border:1px solid #3a4850;background:#101821}
.recovery-card h1{margin:0 0 12px;font-size:24px}
.recovery-card p{color:#a8b4b9;font-size:14px;line-height:1.7}
.recovery-card a{display:inline-flex;min-height:38px;align-items:center;margin-top:14px;padding:0 8px;color:#72cbd2;font-size:14px;text-decoration:none}
@media(max-width:520px){.recovery-page{min-height:calc(100vh - 64px);padding:16px 10px}.recovery-card{padding:18px}.recovery-card h1{font-size:22px}.recovery-card p,.recovery-card a{font-size:13px}}
</style>

