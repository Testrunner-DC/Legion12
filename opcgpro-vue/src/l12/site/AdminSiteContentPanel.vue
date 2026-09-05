<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { adminApi, hasPermission, type ContentBatch, type ContentEntry, type SiteCategory, type SiteContentKind, type SiteMedia, type SiteMediaKind } from '@/l12/platform'
import AdminArticlesPanel from './AdminArticlesPanel.vue'
import MediaUploadField from './MediaUploadField.vue'
import { createHomeHeroSlide, createHomeNotice, defaultHomeComposition, defaultSiteLegal, homeCompositionKey, parseHomeComposition, parseSiteLegal, serializeHomeComposition, serializeSiteLegal, siteLegalKey, type HomeComposition, type SiteLegalContent } from './homeContent'

type SiteSection = 'media' | 'home' | 'news' | 'video' | 'product' | 'categories' | 'legal'
const emit = defineEmits<{ notice: [value: string] }>()
const section = ref<SiteSection>('media')
const composition = reactive<HomeComposition>(defaultHomeComposition())
const legal = reactive<SiteLegalContent>(defaultSiteLegal())
const ruleNotice = ref('')
const contentEntries = reactive<Record<string, ContentEntry | undefined>>({})
const contentBatches = ref<ContentBatch[]>([])
const media = ref<SiteMedia[]>([])
const mediaKind = ref<SiteMediaKind>('hero')
const categories = ref<SiteCategory[]>([])
const categoryKind = ref<SiteContentKind>('news')
const categoryMigration = reactive<Record<string, string>>({})
const newCategory = reactive({ name: '', slug: '', active: true })
const busy = ref(false)

const sections: { id: SiteSection; label: string }[] = [
  { id: 'media', label: '素材库' }, { id: 'home', label: '首页编排' }, { id: 'news', label: '资讯中心' },
  { id: 'video', label: '社群视频' }, { id: 'product', label: '商品情报' },
  { id: 'categories', label: '分类管理' }, { id: 'legal', label: '页尾与法务' },
]
const mediaByKind = computed(() => media.value.filter(item => item.kind === mediaKind.value))
const heroMedia = computed(() => media.value.filter(item => item.kind === 'hero'))
const categoriesByKind = computed(() => categories.value.filter(item => item.kind === categoryKind.value)
  .sort((left, right) => left.sortOrder - right.sortOrder))

function showNotice(message: string) { emit('notice', message) }
function move<T>(items: T[], index: number, direction: -1 | 1) {
  const target = index + direction
  if (target < 0 || target >= items.length) return
  const [item] = items.splice(index, 1)
  if (item !== undefined) items.splice(target, 0, item)
}
function addSlide() { composition.heroSlides.push(createHomeHeroSlide()) }
function removeSlide(index: number) { composition.heroSlides.splice(index, 1) }
function addNotice() { composition.notices.push(createHomeNotice()) }
function removeNotice(index: number) { composition.notices.splice(index, 1) }
function mediaUrl(id?: string, variant: 'desktopUrl' | 'thumbnailUrl' = 'thumbnailUrl') {
  return media.value.find(item => item.id === id)?.[variant] || ''
}
function mediaUploaded(item: SiteMedia, targetSlide?: number) {
  media.value = [item, ...media.value.filter(row => row.id !== item.id)]
  if (targetSlide !== undefined && composition.heroSlides[targetSlide]) composition.heroSlides[targetSlide].mediaAssetId = item.id
}

async function load() {
  busy.value = true
  try {
    const [home, footer, rules, nextMedia, nextCategories, batches] = await Promise.all([
      adminApi.getContent(homeCompositionKey), adminApi.getContent(siteLegalKey), adminApi.getContent('rules.notice'),
      adminApi.siteMedia(), adminApi.siteCategories(), adminApi.contentBatches(),
    ])
    contentEntries[homeCompositionKey] = home; contentEntries[siteLegalKey] = footer; contentEntries['rules.notice'] = rules
    Object.assign(composition, parseHomeComposition(home.draftValue))
    Object.assign(legal, parseSiteLegal(footer.draftValue))
    ruleNotice.value = rules.draftValue
    media.value = nextMedia; categories.value = nextCategories; contentBatches.value = batches
  } catch (error) { showNotice(error instanceof Error ? error.message : '站点内容加载失败') }
  finally { busy.value = false }
}
async function saveHome(show = true) {
  try {
    const entry = await adminApi.saveContentDraft(homeCompositionKey, serializeHomeComposition(composition))
    contentEntries[homeCompositionKey] = entry
    if (show) showNotice('首页编排草稿已保存，线上首页未改变')
    return true
  } catch (error) { showNotice(error instanceof Error ? error.message : '首页编排保存失败'); return false }
}
async function saveLegal(show = true) {
  try {
    const [footer, rules] = await Promise.all([
      adminApi.saveContentDraft(siteLegalKey, serializeSiteLegal(legal)),
      adminApi.saveContentDraft('rules.notice', ruleNotice.value),
    ])
    contentEntries[siteLegalKey] = footer; contentEntries['rules.notice'] = rules
    if (show) showNotice('页尾、法务与规则公告草稿已保存')
    return true
  } catch (error) { showNotice(error instanceof Error ? error.message : '页尾与法务保存失败'); return false }
}
async function preview(keys: string[], saver: (show?: boolean) => Promise<boolean>) {
  if (!(await saver(false))) return
  try {
    const result = await adminApi.previewContent(keys)
    showNotice(`发布预览完成：${result.items.filter(item => item.wouldChange).length} 项将更新，未写入线上内容`)
  } catch (error) { showNotice(error instanceof Error ? error.message : '发布预览失败') }
}
async function publish(keys: string[], saver: (show?: boolean) => Promise<boolean>) {
  if (!(await saver(false))) return
  try {
    const result = await adminApi.publishContent(keys)
    showNotice('commandId' in result ? `内容发布已提交双人审批（命令 ${result.commandId}）` : '站点内容已正式发布')
    contentBatches.value = await adminApi.contentBatches()
  } catch (error) { showNotice(error instanceof Error ? error.message : '内容发布失败') }
}
async function rollback(batch: ContentBatch) {
  try {
    const result = await adminApi.rollbackContent(batch.id)
    showNotice('commandId' in result ? `回滚已提交双人审批（命令 ${result.commandId}）` : '内容已回滚')
  } catch (error) { showNotice(error instanceof Error ? error.message : '回滚失败') }
}
async function deleteMedia(item: SiteMedia) {
  if (!window.confirm(`删除素材“${item.altText || item.contentHash.slice(0, 12)}”？内容寻址文件会保留用于恢复。`)) return
  try { await adminApi.deleteSiteMedia(item.id); media.value = media.value.filter(row => row.id !== item.id); showNotice('素材记录已软删除') }
  catch (error) { showNotice(error instanceof Error ? error.message : '素材删除失败') }
}
async function createCategory() {
  if (!newCategory.name.trim()) return
  try {
    const saved = await adminApi.saveSiteCategory({ kind: categoryKind.value, name: newCategory.name, slug: newCategory.slug, sortOrder: categoriesByKind.value.length, active: true })
    categories.value.push(saved); newCategory.name = ''; newCategory.slug = ''; showNotice('分类已新增并写入审计')
  } catch (error) { showNotice(error instanceof Error ? error.message : '分类新增失败') }
}
async function saveCategory(item: SiteCategory) {
  try {
    const saved = await adminApi.saveSiteCategory(item)
    categories.value = categories.value.map(row => row.id === saved.id ? saved : row)
    showNotice('分类已保存')
  } catch (error) { showNotice(error instanceof Error ? error.message : '分类保存失败') }
}
async function reorderCategory(index: number, direction: -1 | 1) {
  const rows = [...categoriesByKind.value]
  move(rows, index, direction)
  try {
    const reordered = await adminApi.reorderSiteCategories(categoryKind.value, rows.map(item => item.id))
    categories.value = [...categories.value.filter(item => item.kind !== categoryKind.value), ...reordered]
  } catch (error) { showNotice(error instanceof Error ? error.message : '分类排序失败') }
}
async function deleteCategory(item: SiteCategory) {
  const target = categoryMigration[item.id] || undefined
  if (!window.confirm(item.itemCount ? `该分类有 ${item.itemCount} 个内容快照，将迁移后删除，确认继续？` : '确认删除空分类？')) return
  try {
    await adminApi.deleteSiteCategory(item.id, target)
    categories.value = categories.value.filter(row => row.id !== item.id)
    showNotice(item.itemCount ? '分类引用已原子迁移并删除' : '空分类已删除')
  } catch (error) { showNotice(error instanceof Error ? error.message : '分类删除失败') }
}

onMounted(load)
</script>

<template>
  <section class="site-content-admin">
    <header class="site-content-head"><div><small>SITE CONTENT</small><h2>站点内容</h2><p>素材、首页、资讯、视频、商品、分类与法务共用既有权限、发布审批、审计和持久化边界。</p></div><button @click="load">{{ busy ? '读取中…' : '刷新全部' }}</button></header>
    <nav class="site-content-nav" aria-label="站点内容模块"><button v-for="item in sections" :key="item.id" :class="{ active: section === item.id }" @click="section = item.id">{{ item.label }}</button></nav>

    <section v-if="section === 'media'" class="content-panel media-library">
      <header><div><h3>素材库</h3><p>原图仅归档；公开端只提供内容哈希 URL 的桌面、移动 WebP 与缩略图，删除前检查全部草稿和历史引用。</p></div><select v-model="mediaKind"><option value="hero">首页轮播</option><option value="news">资讯封面</option><option value="video">视频封面</option><option value="product">商品图片</option></select></header>
      <MediaUploadField :kind="mediaKind" @uploaded="mediaUploaded" @notice="showNotice"/>
      <div class="media-grid"><article v-for="item in mediaByKind" :key="item.id"><img :src="item.thumbnailUrl" :alt="item.altText"><div><b>{{ item.altText || '未填写替代文字' }}</b><code>{{ item.contentHash }}</code><small>{{ item.originalFormat }} · 原图 {{ Math.ceil(item.originalBytes / 1024) }}KB · 交付 {{ Math.ceil(item.deliveryBytes / 1024) }}KB</small><small>焦点 {{ Math.round(item.focalX * 100) }}% / {{ Math.round(item.focalY * 100) }}% · 引用 {{ item.referenceCount }}</small><button :disabled="item.referenceCount > 0" @click="deleteMedia(item)">{{ item.referenceCount ? '被引用，禁止删除' : '软删除素材' }}</button></div></article><p v-if="!mediaByKind.length" class="empty">此类型尚无上传素材。</p></div>
    </section>

    <section v-else-if="section === 'home'" class="content-panel home-compose">
      <header><div><h3>首页编排</h3><p>轮播与通知按钮数量不限于固定模板；启用的轮播发布前必须绑定后台上传素材。</p></div><div class="panel-actions"><button @click="addSlide">＋ 轮播</button><button @click="addNotice">＋ 通知按钮</button><button v-if="hasPermission('admin.content.draft')" @click="saveHome()">保存草稿</button><button @click="preview([homeCompositionKey], saveHome)">预览</button><button v-if="hasPermission('admin.content.publish')" class="publish" @click="publish([homeCompositionKey], saveHome)">提交发布</button></div></header>
      <h4>轮播主视觉 <em :data-status="contentEntries[homeCompositionKey]?.status">{{ contentEntries[homeCompositionKey]?.status === 'draft' ? '有未发布草稿' : '已发布' }}</em></h4>
      <article v-for="(slide, index) in composition.heroSlides" :key="slide.id" class="compose-row hero-compose-row">
        <div class="compose-order"><button @click="move(composition.heroSlides, index, -1)">↑</button><b>{{ index + 1 }}</b><button @click="move(composition.heroSlides, index, 1)">↓</button></div>
        <div class="compose-fields"><label>眉题<input v-model="slide.eyebrow" maxlength="80"></label><label>标题<input v-model="slide.title" maxlength="180"></label><label class="wide">说明<textarea v-model="slide.summary" rows="3" maxlength="600"></textarea></label><label>跳转<input v-model="slide.href" placeholder="/news 或 https://"></label><label>按钮文字<input v-model="slide.linkLabel" maxlength="40"></label><label class="check"><input v-model="slide.enabled" type="checkbox"> 启用</label></div>
        <div class="compose-media"><select v-model="slide.mediaAssetId"><option value="">请选择轮播素材</option><option v-for="asset in heroMedia" :key="asset.id" :value="asset.id">{{ asset.altText || asset.contentHash.slice(0, 12) }}</option></select><MediaUploadField v-model="slide.mediaAssetId" kind="hero" :preview-url="mediaUrl(slide.mediaAssetId)" :initial-alt="slide.title" @uploaded="mediaUploaded($event, index)" @notice="showNotice"/><button class="danger" @click="removeSlide(index)">删除轮播</button></div>
      </article><p v-if="!composition.heroSlides.length" class="empty">暂无轮播；公开首页将显示品牌占位主视觉，添加并上传图片后再发布。</p>
      <h4>轮播通知按钮组</h4>
      <article v-for="(item, index) in composition.notices" :key="item.id" class="notice-compose-row"><b>{{ index + 1 }}</b><input v-model="item.label" maxlength="80" placeholder="通知文字"><input v-model="item.href" placeholder="/news 或 https://"><select v-model="item.tone"><option value="light">浅色</option><option value="dark">深色</option><option value="accent">强调</option></select><label><input v-model="item.enabled" type="checkbox">启用</label><button @click="move(composition.notices, index, -1)">↑</button><button @click="move(composition.notices, index, 1)">↓</button><button class="danger" @click="removeNotice(index)">删除</button></article>
      <h4>四段首页标题</h4><div class="section-copy-grid"><label>资讯眉题<input v-model="composition.newsEyebrow"></label><label>资讯标题<input v-model="composition.newsTitle"></label><label>资讯说明<textarea v-model="composition.newsDescription" rows="2"></textarea></label><label>视频眉题<input v-model="composition.videoEyebrow"></label><label>视频标题<input v-model="composition.videoTitle"></label><label>视频说明<textarea v-model="composition.videoDescription" rows="2"></textarea></label><label>商品眉题<input v-model="composition.productEyebrow"></label><label>商品标题<input v-model="composition.productTitle"></label><label>商品说明<textarea v-model="composition.productDescription" rows="2"></textarea></label></div>
    </section>

    <AdminArticlesPanel v-else-if="section === 'news'" kind="news" @notice="showNotice"/>
    <AdminArticlesPanel v-else-if="section === 'video'" kind="video" @notice="showNotice"/>
    <AdminArticlesPanel v-else-if="section === 'product'" kind="product" @notice="showNotice"/>

    <section v-else-if="section === 'categories'" class="content-panel category-manager">
      <header><div><h3>分类管理</h3><p>分类完全由后台维护；非空分类删除时必须选择同类型启用分类迁移，否则服务端拒绝。</p></div><select v-model="categoryKind"><option value="news">资讯分类</option><option value="video">视频分类</option><option value="product">商品分类</option></select></header>
      <div class="new-category"><input v-model="newCategory.name" placeholder="新分类名称"><input v-model="newCategory.slug" placeholder="URL 标识（可留空）"><button @click="createCategory">新增分类</button></div>
      <article v-for="(item, index) in categoriesByKind" :key="item.id" class="category-row"><input v-model="item.name" maxlength="40"><input v-model="item.slug" maxlength="80"><label><input v-model="item.active" type="checkbox">{{ item.active ? '启用' : '停用' }}</label><small>{{ item.itemCount }} 个内容快照</small><button @click="reorderCategory(index, -1)">↑</button><button @click="reorderCategory(index, 1)">↓</button><button @click="saveCategory(item)">保存</button><select v-if="item.itemCount" v-model="categoryMigration[item.id]"><option value="">选择迁移目标</option><option v-for="target in categoriesByKind.filter(row => row.id !== item.id && row.active)" :key="target.id" :value="target.id">迁移到 {{ target.name }}</option></select><button class="danger" :disabled="item.itemCount > 0 && !categoryMigration[item.id]" @click="deleteCategory(item)">删除</button></article>
    </section>

    <section v-else class="content-panel legal-editor">
      <header><div><h3>页尾与法务</h3><p>版权、商标、备案与联系信息独立于首页结构；与规则页公告同批可预览和审批发布。</p></div><div class="panel-actions"><button v-if="hasPermission('admin.content.draft')" @click="saveLegal()">保存草稿</button><button @click="preview([siteLegalKey, 'rules.notice'], saveLegal)">预览</button><button v-if="hasPermission('admin.content.publish')" class="publish" @click="publish([siteLegalKey, 'rules.notice'], saveLegal)">提交发布</button></div></header>
      <label>版权行<input v-model="legal.copyright" maxlength="300"></label><label>商标与权利声明<textarea v-model="legal.trademark" rows="5" maxlength="1200"></textarea></label><label>备案 / 登记信息<input v-model="legal.registration" maxlength="200"></label><label>联系标签<input v-model="legal.contactLabel" maxlength="100"></label><label>联系链接<input v-model="legal.contactHref" maxlength="2000" placeholder="mailto: 不允许；使用站内页或 https://"></label><label>规则页公告<textarea v-model="ruleNotice" rows="5"></textarea></label>
      <details class="batch-history"><summary>发布与回滚批次（{{ contentBatches.length }}）</summary><article v-for="batch in contentBatches" :key="batch.id"><span><code>{{ batch.id }}</code><b>{{ batch.action }} · {{ batch.status }}</b><small>{{ batch.actorName }} · {{ new Date(batch.createdAt).toLocaleString() }} · {{ batch.items.length }} 项</small></span><button v-if="batch.action === 'publish' && batch.status === 'published' && hasPermission('admin.content.rollback')" @click="rollback(batch)">提交回滚审批</button></article></details>
    </section>
  </section>
</template>

<style scoped>
.site-content-admin{min-width:0}.site-content-head,.content-panel>header{display:flex;align-items:center;justify-content:space-between;gap:15px;padding:18px;border:1px solid #35424a;background:#101821}.site-content-head small{color:#55c6cd;font:900 9px monospace;letter-spacing:.18em}.site-content-head h2,.content-panel h3{margin:4px 0}.site-content-head p,.content-panel header p{margin:3px 0;color:#7d898f;font-size:10px}.site-content-head button,.content-panel button,.content-panel input,.content-panel textarea,.content-panel select{box-sizing:border-box;padding:9px;border:1px solid #4a5860;background:#070d12;color:#fff}.site-content-nav{display:flex;overflow-x:auto;margin:10px 0;border:1px solid #35424a;background:#0a1117}.site-content-nav button{flex:1 0 115px;padding:12px;border:0;border-right:1px solid #2d3940;background:transparent;color:#859197;font-weight:900}.site-content-nav button.active{background:#2a2311;color:#efd170;box-shadow:inset 0 -3px #d3b256}.content-panel{padding:16px;border:1px solid #35424a;background:#0e161d}.content-panel>header{margin:-16px -16px 16px;border-width:0 0 1px}.panel-actions{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:7px}.panel-actions .publish{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.media-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px;margin-top:14px}.media-grid article{overflow:hidden;border:1px solid #35434a;background:#091016}.media-grid img{width:100%;aspect-ratio:16/9;object-fit:cover}.media-grid article>div{display:flex;flex-direction:column;gap:5px;padding:11px}.media-grid code{overflow:hidden;color:#d6ba66;font-size:8px;text-overflow:ellipsis}.media-grid small{color:#78868c;font-size:8px}.media-grid button,.danger{border-color:#81434c!important;background:#281117!important;color:#eea6ae!important}.media-grid button:disabled,.category-row button:disabled{opacity:.45}.home-compose h4{margin:22px 0 8px;padding-bottom:7px;border-bottom:1px solid #344149}.home-compose h4 em{float:right;color:#7fd3ae;font-size:8px;font-style:normal}.compose-row{display:grid;grid-template-columns:42px minmax(300px,.9fr) minmax(380px,1.1fr);gap:12px;margin-top:10px;padding:12px;border:1px solid #35434a;background:#091016}.compose-order{display:flex;flex-direction:column;align-items:center;justify-content:center;gap:5px}.compose-fields,.section-copy-grid{display:grid;grid-template-columns:1fr 1fr;gap:9px}.compose-fields label,.section-copy-grid label,.legal-editor>label{display:grid;gap:5px;color:#aeb8bb;font-size:9px;font-weight:900}.compose-fields .wide{grid-column:1/-1}.compose-fields .check{display:flex;align-items:center}.compose-fields .check input{width:auto}.compose-media{display:grid;gap:8px}.compose-media>.danger{justify-self:end}.notice-compose-row{display:grid;grid-template-columns:28px 1fr 1fr 90px 70px repeat(3,38px);gap:6px;align-items:center;margin-top:7px}.notice-compose-row label{display:flex;align-items:center;font-size:9px}.notice-compose-row label input{width:auto}.section-copy-grid{grid-template-columns:repeat(3,1fr)}.section-copy-grid input,.section-copy-grid textarea,.compose-fields input,.compose-fields textarea,.compose-media select,.notice-compose-row input,.notice-compose-row select,.category-manager input,.category-manager select,.legal-editor input,.legal-editor textarea{width:100%}.new-category{display:grid;grid-template-columns:1fr 1fr auto;gap:7px}.category-row{display:grid;grid-template-columns:1fr 1fr 90px 110px 38px 38px 60px minmax(150px,1fr) 60px;gap:6px;align-items:center;margin-top:8px;padding:8px;border-bottom:1px solid #303c43}.category-row label{font-size:9px}.category-row label input{width:auto}.category-row small{color:#7b898f}.legal-editor>label{margin-top:12px}.batch-history{margin-top:20px}.batch-history summary{color:#d8bc69;cursor:pointer;font-weight:900}.batch-history article{display:flex;align-items:center;justify-content:space-between;padding:9px;border-bottom:1px solid #303c43}.batch-history article span{display:flex;flex-direction:column}.batch-history small{color:#77858b}.empty{grid-column:1/-1;padding:30px;color:#75838a;text-align:center}@media(max-width:1200px){.media-grid{grid-template-columns:1fr 1fr}.compose-row{grid-template-columns:42px 1fr}.compose-media{grid-column:2}.category-row{grid-template-columns:1fr 1fr 80px 100px repeat(3,38px)}.category-row select,.category-row .danger{grid-column:auto / span 3}.notice-compose-row{grid-template-columns:28px 1fr 1fr 80px}.notice-compose-row button{grid-row:2}}@media(max-width:760px){.site-content-head,.content-panel>header{align-items:flex-start;flex-direction:column}.media-grid{grid-template-columns:1fr}.compose-row{grid-template-columns:1fr}.compose-order{flex-direction:row}.compose-media{grid-column:auto}.compose-fields,.section-copy-grid{grid-template-columns:1fr}.compose-fields .wide{grid-column:auto}.notice-compose-row,.new-category,.category-row{grid-template-columns:1fr}.notice-compose-row button,.category-row select,.category-row .danger{grid-column:auto;grid-row:auto}.panel-actions{justify-content:flex-start}}
</style>
