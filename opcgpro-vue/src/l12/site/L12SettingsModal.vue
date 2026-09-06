<script setup lang="ts">
import { audioPreferences } from '@/l12/audioPreferences'

defineEmits<{ close: [] }>()
</script>

<template>
  <section class="l12-settings-modal" data-ui-contract="l12-settings-all-features">
    <header><div><small>SETTINGS</small><h2>设置</h2></div><button aria-label="关闭设置" @click="$emit('close')">×</button></header>
    <div class="settings-grid">
      <label class="setting-row"><span><b>卡牌显示</b><small>图鉴、牌库与对战同步调整</small></span><select v-model="audioPreferences.cardSize"><option value="auto">自动</option><option value="small">小</option><option value="medium">中</option><option value="large">大</option></select></label>
      <label class="setting-row"><span><b>对局动画</b><small>必要的公开与结算信息始终保留</small></span><select v-model="audioPreferences.animation"><option value="off">关闭</option><option value="fast">快速</option><option value="standard">标准</option></select></label>
      <div class="setting-row"><span><b>游戏音乐</b><small>官网与对局曲目平滑切换；低音量区可精细调整</small></span><div class="audio-setting"><button type="button" class="toggle" :class="{ on: audioPreferences.musicEnabled }" @click="audioPreferences.musicEnabled = !audioPreferences.musicEnabled">{{ audioPreferences.musicEnabled ? '已开启' : '已关闭' }}</button><label><span class="sr-only">音乐音量</span><input v-model.number="audioPreferences.musicVolume" :disabled="!audioPreferences.musicEnabled" type="range" min="0" max="1" step="0.01"/></label><output>{{ Math.round(audioPreferences.musicVolume * 100) }}%</output></div></div>
      <div class="setting-row"><span><b>游戏音效</b><small>卡牌、战斗、回合与系统提示</small></span><div class="audio-setting"><button type="button" class="toggle" :class="{ on: audioPreferences.sfxEnabled }" @click="audioPreferences.sfxEnabled = !audioPreferences.sfxEnabled">{{ audioPreferences.sfxEnabled ? '已开启' : '已关闭' }}</button><label><span class="sr-only">音效音量</span><input v-model.number="audioPreferences.sfxVolume" :disabled="!audioPreferences.sfxEnabled" type="range" min="0" max="1" step="0.05"/></label><output>{{ Math.round(audioPreferences.sfxVolume * 100) }}%</output></div></div>
    </div>
    <p class="setting-note">修改会立即作用于当前页面与对局；登录后保存到账号并同步至其他设备。</p>
  </section>
</template>

<style scoped>
.l12-settings-modal{box-sizing:border-box;width:min(610px,94vw);max-height:min(720px,90vh);overflow:auto;padding:24px;border:1px solid rgba(235,230,216,.28);background:#111923;box-shadow:0 28px 90px #000;color:#f2f0e9;font-family:'Microsoft YaHei','微软雅黑',system-ui,sans-serif}.l12-settings-modal>header{display:flex;align-items:center;justify-content:space-between;padding-bottom:15px;border-bottom:1px solid rgba(235,230,216,.14)}header small{color:#51c5cc;font:900 14px monospace;letter-spacing:.18em}h2{margin:4px 0 0;font-size:24px}header button{width:34px;height:34px;border:1px solid #48545c;background:#0a1016;color:#fff}.settings-grid{display:grid}.setting-row{display:grid;grid-template-columns:minmax(190px,1fr) minmax(230px,1fr);align-items:center;gap:18px;padding:18px 0;border:0;border-bottom:1px solid rgba(235,230,216,.1);color:inherit}.setting-row>span,.setting-row>span b,.setting-row>span small{display:block}.setting-row>span small{margin-top:5px;color:#7f8b93;font-size:14px}.setting-row select,.toggle{box-sizing:border-box;min-width:118px;padding:10px;border:1px solid #52606a;background:#081018;color:#fff;font-weight:900}.toggle.on{border-color:#54b48f;color:#7ee2b9}.audio-setting{display:grid;grid-template-columns:92px minmax(100px,1fr) 42px;align-items:center;gap:10px}.audio-setting label,.audio-setting input{width:100%}.audio-setting output{color:#9eabad;font:900 14px monospace;text-align:right}.audio-setting input:disabled{opacity:.4}.setting-note{margin:16px 0 0;color:#7e898f;font-size:14px;line-height:1.7}.sr-only{position:absolute;width:1px;height:1px;overflow:hidden;clip-path:inset(50%)}
@media(max-width:620px){.l12-settings-modal{padding:18px}.setting-row{grid-template-columns:1fr;gap:10px}.setting-row select{width:100%}.audio-setting{grid-template-columns:92px minmax(90px,1fr) 40px}}
</style>
