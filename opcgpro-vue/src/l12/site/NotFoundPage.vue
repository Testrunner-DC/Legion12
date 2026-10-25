<template>
  <section class="not-found-page" aria-labelledby="not-found-title">
    <div class="not-found-panel">
      <span aria-hidden="true">NOT FOUND</span>
      <h1 id="not-found-title">页面不存在</h1>
      <p>你访问的页面不存在，可能已被移动或地址有误。</p>
      <router-link to="/">返回主页</router-link>
    </div>
  </section>
</template>

<style scoped>
.not-found-page{box-sizing:border-box;min-height:100%;display:grid;place-items:center;padding:clamp(28px,7vw,92px) 20px;background:radial-gradient(circle at 50% 24%,rgba(51,174,181,.12),transparent 36%),#080d11;color:#f2f0e9;font-family:'Microsoft YaHei','微软雅黑',system-ui,sans-serif}.not-found-panel{width:min(520px,100%);padding:clamp(28px,6vw,52px);border:1px solid rgba(232,227,213,.18);background:rgba(13,20,26,.92);box-shadow:0 28px 80px rgba(0,0,0,.35);text-align:center}.not-found-panel span{color:#58cbd1;font-size:13px;font-weight:900;letter-spacing:.18em}.not-found-panel h1{margin:12px 0 0;font-size:clamp(28px,7vw,46px);line-height:1.2}.not-found-panel p{margin:18px 0 28px;color:#a7b0b4;font-size:16px;line-height:1.8}.not-found-panel a{display:inline-flex;min-height:44px;align-items:center;justify-content:center;padding:0 24px;border:1px solid #d2b861;background:#211c0d;color:#f6df91;font-weight:900;text-decoration:none}.not-found-panel a:hover{border-color:#f0d578;background:#2b2411}.not-found-panel a:focus-visible{outline:2px solid #58cbd1;outline-offset:3px}
</style>
