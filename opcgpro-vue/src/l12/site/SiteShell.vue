<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { l12State } from '@/l12/net'
import SiteIcon from './SiteIcon.vue'

const route = useRoute()
const mobileOpen = ref(false)
const modal = ref<'settings' | 'updates' | 'online' | null>(null)

const nav = [
  { to: '/', icon: 'home', label: '主页' },
  { to: '/news', icon: 'news', label: '资讯' },
  { to: '/lobby', icon: 'battle', label: '大厅' },
  { to: '/tournaments', icon: 'tournament', label: '赛事中心' },
  { to: '/decks', icon: 'decks', label: '牌库' },
  { to: '/friends', icon: 'friends', label: '好友' },
  { to: '/cards', icon: 'archive', label: '卡牌图鉴' },
  { to: '/rules', icon: 'rules', label: '规则' },
  { to: '/rankings', icon: 'ranking', label: '排行榜' },
  { to: '/me', icon: 'profile', label: '我的' },
  { to: '/records', icon: 'records', label: '对局记录' },
]

const stored = (() => {
  try { return JSON.parse(localStorage.getItem('l12-site-settings-v1') || '{}') } catch { return {} }
})()
const settings = reactive({ animation: stored.animation ?? 'standard', sound: stored.sound ?? true, cardSize: stored.cardSize ?? 'auto' })
const onlineCount = computed(() => l12State.status === 'online' ? 1 : 0)
const connectionLabel = computed(() => ({ online: '连接正常', connecting: '连接中', offline: '未连接' }[l12State.status]))

watch(() => route.fullPath, () => { mobileOpen.value = false })
watch(settings, value => localStorage.setItem('l12-site-settings-v1', JSON.stringify(value)), { deep: true })
</script>

<template>
  <div class="site-shell">
    <header class="site-mobile-head">
      <router-link class="mobile-brand" to="/"><img src="/brand/l12-mark-white.png" alt="十二军团"/><span>十二军团</span></router-link>
      <button aria-label="打开导航" @click="mobileOpen = !mobileOpen">{{ mobileOpen ? '×' : '☰' }}</button>
    </header>

    <aside class="site-sidebar" :class="{ open: mobileOpen }">
      <router-link class="site-brand" to="/" title="十二军团官方网站">
        <img src="/brand/l12-mark-white.png" alt="十二军团"/>
        <span>LEGION 12<small>十二军团</small></span>
      </router-link>

      <nav class="site-nav" aria-label="主要导航">
        <router-link v-for="item in nav" :key="item.to" :to="item.to" :title="item.label">
          <SiteIcon :name="item.icon"/><span>{{ item.label }}</span>
        </router-link>
      </nav>

      <div class="site-utilities">
        <button title="设置" @click="modal = 'settings'"><SiteIcon name="settings"/><span>设置</span></button>
        <button title="更新日志" @click="modal = 'updates'"><SiteIcon name="updates"/><span>更新日志</span></button>
        <button title="在线人数" @click="modal = 'online'"><span class="utility-icon"><SiteIcon name="online"/><i>{{ onlineCount }}</i></span><span>在线人数</span></button>
        <button class="connection" :class="l12State.status" :title="connectionLabel"><span class="utility-icon"><SiteIcon name="connection"/><i/></span><span>{{ connectionLabel }}</span></button>
      </div>
    </aside>

    <main class="site-content"><slot /></main>

    <div v-if="modal" class="site-modal-mask" @click.self="modal = null">
      <section v-if="modal === 'settings'" class="site-modal">
        <header><div><small>SETTINGS</small><h2>设置</h2></div><button @click="modal = null">×</button></header>
        <div class="setting-row"><div><b>卡牌显示</b><span>调整图鉴、牌库与对战中的卡牌尺寸</span></div><select v-model="settings.cardSize"><option value="auto">自动</option><option value="small">小</option><option value="medium">中</option><option value="large">大</option></select></div>
        <div class="setting-row"><div><b>对局动画</b><span>不会跳过必要的公开与结算信息</span></div><select v-model="settings.animation"><option value="off">关闭</option><option value="fast">快速</option><option value="standard">标准</option></select></div>
        <div class="setting-row"><div><b>游戏音效</b><span>卡牌、战斗、回合及系统提示音</span></div><button class="toggle" :class="{ on: settings.sound }" @click="settings.sound = !settings.sound">{{ settings.sound ? '已开启' : '已关闭' }}</button></div>
        <p class="setting-note">官网与资料页面适配竖屏；对战与回放在移动端使用横屏布局。</p>
      </section>

      <section v-else-if="modal === 'updates'" class="site-modal update-modal">
        <header><div><small>CHANGELOG</small><h2>更新日志</h2></div><button @click="modal = null">×</button></header>
        <article><time>当前开发版</time><h3>官方网站框架与导航升级</h3><ul><li>建立官网、资料库、牌库社区与对战器四个产品域。</li><li>新增独立页面路由、桌面侧栏与移动端导航。</li><li>规划牌库码、牌库图和 JSON 回放交换能力。</li></ul></article>
        <article><time>对战内核</time><h3>S1 完整回归与 S2 持续接入</h3><ul><li>响应堆叠、目标预声明、派生兵力与牌库操作已纳入公共框架。</li><li>当前仍为开发测试环境，卡效覆盖情况以测试报告为准。</li></ul></article>
      </section>

      <section v-else class="site-modal online-modal">
        <header><div><small>ONLINE</small><h2>在线玩家</h2></div><button @click="modal = null">×</button></header>
        <div v-if="l12State.status === 'online'" class="online-entry"><i/><div><b>{{ l12State.nickname || '当前玩家' }}</b><span>在线 · 当前设备</span></div></div>
        <div v-else class="modal-empty">连接服务器后可查看在线玩家并直接进入允许观战的对局。</div>
      </section>
    </div>
  </div>
</template>

<style scoped>
.site-shell{--nav-w:92px;width:100vw;height:100vh;background:radial-gradient(circle at 80% 10%,rgba(18,101,108,.12),transparent 34%),radial-gradient(circle at 12% 84%,rgba(121,22,32,.13),transparent 35%),#060a0d;color:#f2f0e9;font-family:'Microsoft YaHei','微软雅黑',system-ui,sans-serif}.site-sidebar{position:fixed;z-index:40;inset:0 auto 0 0;width:var(--nav-w);display:flex;flex-direction:column;border-right:1px solid rgba(232,227,213,.16);background:#0d1318}.site-brand{display:flex;height:96px;flex-direction:column;align-items:center;justify-content:center;gap:5px;color:#f3eee1;text-decoration:none}.site-brand img{width:44px;height:44px;border:1px solid rgba(255,255,255,.6);border-radius:50%;object-fit:cover}.site-brand span{font:900 9px Georgia;letter-spacing:.12em}.site-brand small{display:block;margin-top:2px;color:#748087;font:800 8px 'Microsoft YaHei'}.site-nav{display:flex;flex:1;min-height:0;flex-direction:column;overflow-y:auto}.site-nav a,.site-utilities button{position:relative;display:flex;min-height:58px;flex-direction:column;align-items:center;justify-content:center;gap:5px;border:0;background:transparent;color:#7d8991;text-decoration:none}.site-nav a:hover,.site-nav a.router-link-active{background:linear-gradient(90deg,rgba(48,181,190,.2),transparent);color:#f4f1e9}.site-nav a.router-link-active::before{content:'';position:absolute;left:0;top:12px;bottom:12px;width:3px;background:#51c5cc;box-shadow:0 0 12px #51c5cc}.site-nav b,.site-utilities b{font-size:15px}.site-nav span,.site-utilities span{font-size:9px;font-weight:900}.site-utilities{padding:8px 0;border-top:1px solid rgba(232,227,213,.12)}.site-utilities button{width:100%;min-height:48px}.site-utilities .connection i{width:8px;height:8px;border-radius:50%;background:#6b7272}.site-utilities .connection.online i{background:#55c99a;box-shadow:0 0 8px #55c99a}.site-utilities .connection.connecting i{background:#d7b15f}.site-content{position:absolute;inset:0 0 0 var(--nav-w);overflow:auto}.site-mobile-head{display:none}.site-modal-mask{position:fixed;z-index:100;inset:0;display:grid;place-items:center;padding:20px;background:rgba(1,4,7,.75);backdrop-filter:blur(10px)}.site-modal{width:min(560px,94vw);max-height:min(720px,90vh);overflow:auto;border:1px solid rgba(235,230,216,.28);background:#111923;box-shadow:0 28px 90px #000;padding:24px}.site-modal>header{display:flex;align-items:center;justify-content:space-between;padding-bottom:15px;border-bottom:1px solid rgba(235,230,216,.14)}.site-modal header small{color:#51c5cc;font:900 9px monospace;letter-spacing:.18em}.site-modal h2{margin:4px 0 0;font-size:24px}.site-modal header button{width:34px;height:34px;border:1px solid #48545c;background:#0a1016;color:#fff}.setting-row{display:flex;align-items:center;justify-content:space-between;gap:18px;padding:18px 0;border-bottom:1px solid rgba(235,230,216,.1)}.setting-row b,.setting-row span{display:block}.setting-row span{margin-top:5px;color:#7f8b93;font-size:11px}.setting-row select,.toggle{min-width:118px;padding:10px;border:1px solid #52606a;background:#081018;color:#fff;font-weight:900}.toggle.on{border-color:#54b48f;color:#7ee2b9}.setting-note{color:#7e898f;font-size:11px;line-height:1.7}.update-modal article{padding:18px 0;border-bottom:1px solid rgba(235,230,216,.1)}.update-modal time{color:#d6ad59;font-size:10px;font-weight:900}.update-modal h3{margin:6px 0;font-size:15px}.update-modal li{margin:7px 0;color:#a8b0b3;font-size:12px;line-height:1.6}.online-entry{display:flex;align-items:center;gap:12px;margin-top:18px;padding:14px;background:#0a1118}.online-entry i{width:9px;height:9px;border-radius:50%;background:#55c99a;box-shadow:0 0 8px #55c99a}.online-entry b,.online-entry span{display:block}.online-entry span{margin-top:3px;color:#718088;font-size:10px}.modal-empty{margin-top:18px;padding:38px 20px;border:1px dashed #39444b;color:#738089;text-align:center;font-size:12px;line-height:1.7}
@media(max-width:760px){.site-shell{--nav-w:0px}.site-mobile-head{position:fixed;z-index:60;top:0;left:0;right:0;height:58px;display:flex;align-items:center;justify-content:space-between;padding:0 14px;border-bottom:1px solid rgba(232,227,213,.16);background:#0d1318}.mobile-brand{display:flex;align-items:center;gap:9px;color:#fff;text-decoration:none}.mobile-brand b{display:grid;width:30px;height:30px;place-items:center;border:1px solid #d8b362;font:900 10px Georgia}.mobile-brand span{font-weight:900}.site-mobile-head button{width:38px;height:38px;border:1px solid #46525a;background:#111a22;color:#fff;font-size:20px}.site-sidebar{top:58px;width:min(310px,84vw);transform:translateX(-105%);transition:transform .2s}.site-sidebar.open{transform:none;box-shadow:18px 0 50px #000}.site-brand{display:none}.site-nav a,.site-utilities button{min-height:52px;flex-direction:row;justify-content:flex-start;padding:0 24px;gap:15px}.site-nav span,.site-utilities span{font-size:13px}.site-utilities{display:grid;grid-template-columns:1fr 1fr}.site-content{top:58px}.site-modal{padding:18px}.setting-row{align-items:flex-start;flex-direction:column}.setting-row select,.toggle{width:100%}}
.utility-icon{position:relative;display:grid;place-items:center}.utility-icon>i{position:absolute;top:-7px;right:-9px;display:grid!important;min-width:15px!important;width:auto!important;height:15px!important;place-items:center;padding:0 3px;border-radius:8px!important;background:#71303a;color:#fff;font:900 8px monospace!important;font-style:normal}.site-utilities .connection .utility-icon>i{top:auto;right:-5px;bottom:-3px;width:7px!important;min-width:7px!important;height:7px!important;padding:0;border-radius:50%!important;background:#6b7272}.site-utilities .connection.online .utility-icon>i{background:#55c99a!important;box-shadow:0 0 8px #55c99a}.site-utilities .connection.connecting .utility-icon>i{background:#d7b15f!important}
</style>
