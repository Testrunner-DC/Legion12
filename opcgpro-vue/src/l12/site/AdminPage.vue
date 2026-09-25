<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { authState, canAccessAdmin, hasPermission, platformState, refreshCurrentAccount } from '@/l12/platform'
import { adminDomains, visibleAdminSections } from './adminSections'
import { useSectionScroll } from './useSectionScroll'

const route = useRoute()
const router = useRouter()
const sections = computed(() => visibleAdminSections(hasPermission))
const currentSection = computed(() => typeof route.meta.adminSection === 'string' ? route.meta.adminSection : 'overview')
const currentNavigationPath = computed(() => sections.value.find(item => item.id === currentSection.value)?.path ?? '/admin')
const visibleDomains = computed(() => adminDomains.filter(domain =>
  sections.value.some(section => section.domain === domain.id)))
const notice = ref('')
watch(() => route.fullPath, () => { notice.value = '' })
useSectionScroll(() => platformState.account?.id ?? 'guest')

function navigate(event: Event) {
  const path = (event.target as HTMLSelectElement).value
  if (sections.value.some(section => section.path === path)) void router.push(path)
}
onMounted(() => { void refreshCurrentAccount() })
</script>

<template>
  <div class="admin-page">
    <header>
      <div><small>ADMINISTRATION</small><h1>管理后台</h1><p>按任务进入工作区，页面地址可直接分享并支持前进、后退与刷新恢复。</p></div>
      <router-link to="/me">← 返回我的</router-link>
    </header>
    <section v-if="!authState.initialized || authState.refreshing" class="denied"><b>正在验证管理员权限</b><span>管理数据只会在服务端身份确认后加载。</span></section>
    <section v-else-if="!canAccessAdmin" class="denied"><b>需要管理员权限</b><span>请先在“我的”页面登录管理员账号。</span></section>
    <div v-else class="admin-shell">
      <label class="admin-mobile-navigation"><span>后台任务</span><select :value="currentNavigationPath" aria-label="选择后台任务" @change="navigate"><option v-for="item in sections" :key="item.id" :value="item.path">{{ item.label }}</option></select></label>
      <aside class="admin-sidebar" aria-label="后台主导航">
        <nav v-for="domain in visibleDomains" :key="domain.id">
          <small>{{ domain.label }}</small>
          <router-link v-for="item in sections.filter(section => section.domain === domain.id)" :key="item.id" :to="item.path" :aria-current="currentSection === item.id ? 'page' : undefined">
            <span aria-hidden="true">{{ item.icon }}</span>{{ item.label }}
          </router-link>
        </nav>
      </aside>
      <main class="admin-content"><router-view v-slot="{ Component }"><component :is="Component" @notice="notice = $event"/></router-view><p v-if="notice" class="admin-notice" role="status">{{ notice }}</p></main>
    </div>
  </div>
</template>

<style scoped>
.admin-page{min-height:100%;padding:30px clamp(18px,3vw,46px) 70px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.admin-page>header{display:flex;align-items:flex-start;justify-content:space-between;gap:20px}.admin-page>header small{color:#d5b85e;font:900 14px monospace;letter-spacing:.16em}.admin-page h1{margin:5px 0;font-size:30px}.admin-page p{color:#7d898e;font-size:14px;line-height:1.7}.admin-page>header a{color:#e1c36e;text-decoration:none;font-size:14px;font-weight:900}.admin-shell{display:grid;grid-template-columns:224px minmax(0,1fr);gap:18px;margin-top:22px}.admin-sidebar{align-self:start;position:sticky;top:16px;display:grid;gap:5px;padding:12px;border:1px solid #35424a;background:#0b1218}.admin-sidebar nav{display:grid;gap:4px;padding:9px 0;border-bottom:1px solid #26323a}.admin-sidebar nav:last-child{border-bottom:0}.admin-sidebar small{padding:0 8px 5px;color:#68757b;font:900 12px 'Microsoft YaHei'}.admin-sidebar a{display:flex;box-sizing:border-box;width:100%;gap:8px;padding:10px;border:1px solid transparent;color:#9da8ad;text-align:left;text-decoration:none;font:900 14px 'Microsoft YaHei'}.admin-sidebar a:hover,.admin-sidebar a[aria-current="page"]{border-color:#6d5d31;background:#211b0e;color:#f0d579}.admin-content{min-width:0}.admin-notice{margin-top:12px;padding:10px 12px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584}.admin-mobile-navigation{display:none}.denied{display:flex;flex-direction:column;gap:7px;margin-top:22px;padding:20px;border:1px solid #35424a;background:#101821}.denied span{color:#7e8a90}
@media(max-width:850px){.admin-page{padding:18px max(12px,env(safe-area-inset-left)) 46px;padding-right:max(12px,env(safe-area-inset-right))}.admin-shell{display:block}.admin-sidebar{display:none}.admin-mobile-navigation{display:grid;gap:6px;margin-bottom:14px;color:#9aa7ae;font-weight:900}.admin-mobile-navigation select{box-sizing:border-box;width:100%;min-height:44px;border:1px solid #665a35;background:#101820;color:#fff;padding:8px;font:800 14px 'Microsoft YaHei'}}
@media(max-width:560px){.admin-page>header{align-items:stretch;flex-direction:column}.admin-page>header a{align-self:flex-start}.admin-page h1{font-size:24px}}
</style>
