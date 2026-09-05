<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { adminApi, hasPermission, type Article, type ContentBatch, type ContentEntry, type SiteCategory, type SiteContentKind, type SiteMedia, type SiteMediaKind } from '@/l12/platform'
import AdminArticlesPanel from './AdminArticlesPanel.vue'
import MediaUploadField from './MediaUploadField.vue'
import { createHomeHeroSlide, createHomeNotice, defaultHomeComposition, defaultSiteLegal, homeCompositionKey, parseHomeComposition, parseSiteLegal, serializeHomeComposition, serializeSiteLegal, siteLegalKey, type HomeComposition, type HomeNotice, type SiteLegalContent } from './homeContent'

type SiteSection = 'media' | 'hero' | 'notices' | 'home-news' | 'home-product' | 'home-video' | 'news' | 'video' | 'product' | 'categories' | 'legal'
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
const publishedNews = ref<Article[]>([])
const categoryKind = ref<SiteContentKind>('news')
const categoryMigration = reactive<Record<string, string>>({})
const newCategory = reactive({ name: '', slug: '', active: true })
const busy = ref(false)

const sections: { id: SiteSection; label: string }[] = [
  { id: 'media', label: '素材库' }, { id: 'hero', label: '轮播图' }, { id: 'notices', label: '通知按钮' },
  { id: 'home-news', label: '资讯区外观' }, { id: 'news', label: '资讯稿件' },
  { id: 'home-product', label: '产品区外观' }, { id: 'product', label: '产品稿件' },
  { id: 'home-video', label: '视频区外观' }, { id: 'video', label: '视频稿件' },
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
function longestHeroLine(value: string) {
  return Math.max(0, ...value.replace(/\r\n?/g, '\n').split('\n').map(line => Array.from(line).length))
}
function heroLineTooLong(value: string, recommended: number) { return longestHeroLine(value) > recommended }
function heroLineHint(value: string, recommended: number, multiline = false) {
  const current = longestHeroLine(value)
  return `${multiline ? '可按回车手动分行；' : ''}移动端建议每行不超过 ${recommended} 个全角字符；当前最长 ${current}。前台不会自动换行。`
}
function addNotice() { composition.notices.push(createHomeNotice()) }
function removeNotice(index: number) { composition.notices.splice(index, 1) }
function syncNoticeLabel(item: HomeNotice) {
  const article = publishedNews.value.find(row => item.href === `/news#article-${row.id}`)
  if (article && !item.label.trim()) item.label = article.title
}
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
    const [home, footer, rules, nextMedia, nextCategories, batches, nextPublishedNews] = await Promise.all([
      adminApi.getContent(homeCompositionKey), adminApi.getContent(siteLegalKey), adminApi.getContent('rules.notice'),
      adminApi.siteMedia(), adminApi.siteCategories(), adminApi.contentBatches(), adminApi.articles({ status: 'published', kind: 'news' }),
    ])
    contentEntries[homeCompositionKey] = home; contentEntries[siteLegalKey] = footer; contentEntries['rules.notice'] = rules
    Object.assign(composition, parseHomeComposition(home.draftValue))
    Object.assign(legal, parseSiteLegal(footer.draftValue))
    ruleNotice.value = rules.draftValue
    media.value = nextMedia; categories.value = nextCategories; contentBatches.value = batches; publishedNews.value = nextPublishedNews
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
      <header><div><h3>素材库</h3><p>原图仅归档；公开端只提供内容哈希 URL 的桌面、移动 WebP 与缩略图，删除前检查全部草稿和历史引用。</p></div><select v-model="mediaKind"><option value="hero">首页轮播</option><option value="news">资讯封面</option><option value="article">资讯正文图片</option><option value="video">视频封面 16:9</option><option value="product">商品图片</option></select></header>
      <MediaUploadField :kind="mediaKind" @uploaded="mediaUploaded" @notice="showNotice"/>
      <div class="media-grid"><article v-for="item in mediaByKind" :key="item.id" :data-kind="item.kind"><img :src="item.thumbnailUrl" :alt="item.thumbnailAltText || item.altText"><div><b>{{ item.altText || '未填写替代文字' }}</b><code>{{ item.contentHash }}</code><small>{{ item.originalFormat }} · 原图 {{ Math.ceil(item.originalBytes / 1024) }}KB · 交付 {{ Math.ceil(item.deliveryBytes / 1024) }}KB</small><small v-if="item.kind === 'hero' && item.independentVariants">独立三版本 · 桌面：{{ item.desktopAltText }} · 移动：{{ item.mobileAltText }} · 缩略：{{ item.thumbnailAltText }}</small><small v-else>焦点 {{ Math.round(item.focalX * 100) }}% / {{ Math.round(item.focalY * 100) }}% · 引用 {{ item.referenceCount }}</small><small v-if="item.kind === 'hero' && item.independentVariants">素材组引用 {{ item.referenceCount }}；三个版本统一保护、统一软删除</small><button :disabled="item.referenceCount > 0" @click="deleteMedia(item)">{{ item.referenceCount ? '被引用，禁止删除' : '软删除素材组' }}</button></div></article><p v-if="!mediaByKind.length" class="empty">此类型尚无上传素材。</p></div>
    </section>

    <section v-else-if="section === 'hero'" class="content-panel home-compose">
      <header><div><h3>首页轮播图</h3><p>每张轮播分别维护图片、可选文本与整图点击链接；启用项发布前必须绑定后台上传素材。</p></div><div class="panel-actions"><button @click="addSlide">＋ 轮播</button><button v-if="hasPermission('admin.content.draft')" @click="saveHome()">保存草稿</button><button @click="preview([homeCompositionKey], saveHome)">预览</button><button v-if="hasPermission('admin.content.publish')" class="publish" @click="publish([homeCompositionKey], saveHome)">提交发布</button></div></header>
      <h4>轮播主视觉 <em :data-status="contentEntries[homeCompositionKey]?.status">{{ contentEntries[homeCompositionKey]?.status === 'draft' ? '有未发布草稿' : '已发布' }}</em></h4>
      <article v-for="(slide, index) in composition.heroSlides" :key="slide.id" class="compose-row hero-compose-row">
        <div class="compose-order"><button @click="move(composition.heroSlides, index, -1)">↑</button><b>{{ index + 1 }}</b><button @click="move(composition.heroSlides, index, 1)">↓</button></div>
        <div class="compose-fields">
          <label>第一行：系列/编号（可空）<input v-model="slide.eyebrow" maxlength="80"><small class="line-length-hint" :class="{ warning: heroLineTooLong(slide.eyebrow, 18) }">{{ heroLineHint(slide.eyebrow, 18) }}</small></label>
          <label>第二行：主标题（可空）<input v-model="slide.title" maxlength="180"><small class="line-length-hint" :class="{ warning: heroLineTooLong(slide.title, 10) }">{{ heroLineHint(slide.title, 10) }}</small></label>
          <label class="wide">第三行：副标题/说明（可空）<textarea v-model="slide.summary" rows="3" maxlength="600"></textarea><small class="line-length-hint" :class="{ warning: heroLineTooLong(slide.summary, 20) }">{{ heroLineHint(slide.summary, 20, true) }}</small></label>
          <label class="wide">第四行：日期/发布信息（可空）<input v-model="slide.footer" maxlength="180"><small class="line-length-hint" :class="{ warning: heroLineTooLong(slide.footer, 12) }">{{ heroLineHint(slide.footer, 12) }}</small></label>
          <div class="hero-copy-preview wide" aria-label="轮播文案手动换行预览"><small v-if="slide.eyebrow">{{ slide.eyebrow }}</small><b v-if="slide.title">{{ slide.title }}</b><p v-if="slide.summary">{{ slide.summary }}</p><strong v-if="slide.footer">{{ slide.footer }}</strong><i v-if="!slide.eyebrow && !slide.title && !slide.summary && !slide.footer">未填写轮播文案</i></div>
          <label class="wide">整张轮播图点击链接（可空）<input v-model="slide.href" placeholder="留空不跳转；可填 /news 或 https://"></label><label class="check"><input v-model="slide.enabled" type="checkbox"> 启用</label>
        </div>
        <div class="compose-media"><select v-model="slide.mediaAssetId"><option value="">请选择轮播素材</option><option v-for="asset in heroMedia" :key="asset.id" :value="asset.id">{{ asset.altText || asset.contentHash.slice(0, 12) }}</option></select><MediaUploadField v-model="slide.mediaAssetId" kind="hero" :preview-url="mediaUrl(slide.mediaAssetId)" :initial-alt="slide.title" @uploaded="mediaUploaded($event, index)" @notice="showNotice"/><button class="danger" @click="removeSlide(index)">删除轮播</button></div>
      </article><p v-if="!composition.heroSlides.length" class="empty">暂无轮播；公开首页只显示无文案的基础主视觉，添加并上传图片后再发布。</p>
    </section>

    <section v-else-if="section === 'notices'" class="content-panel home-compose">
      <header><div><h3>首页通知按钮</h3><p>通知按钮数量、内容、链接、颜色、排序和启停均独立维护。</p></div><div class="panel-actions"><button @click="addNotice">＋ 通知按钮</button><button v-if="hasPermission('admin.content.draft')" @click="saveHome()">保存草稿</button><button @click="preview([homeCompositionKey], saveHome)">预览</button><button v-if="hasPermission('admin.content.publish')" class="publish" @click="publish([homeCompositionKey], saveHome)">提交发布</button></div></header>
      <article v-for="(item, index) in composition.notices" :key="item.id" class="notice-compose-row"><b>{{ index + 1 }}</b><input v-model="item.label" maxlength="80" placeholder="通知显示文字"><select v-model="item.href" @change="syncNoticeLabel(item)"><option value="">选择一篇已发布资讯</option><option v-for="article in publishedNews" :key="article.id" :value="`/news#article-${article.id}`">{{ article.title }}</option></select><select v-model="item.tone"><option value="light">浅色</option><option value="dark">深色</option><option value="accent">强调</option></select><label><input v-model="item.enabled" type="checkbox">启用</label><button @click="move(composition.notices, index, -1)">↑</button><button @click="move(composition.notices, index, 1)">↓</button><button class="danger" @click="removeNotice(index)">删除</button></article>
      <p v-if="!composition.notices.length" class="empty">暂无通知按钮；点击“＋ 通知按钮”新增。</p>
    </section>

    <section v-else-if="section === 'home-news'" class="content-panel home-compose">
      <header><div><h3>资讯区外观</h3><p>只控制主页资讯区标题与说明；已发布资讯在“资讯稿件”独立维护。</p></div><div class="panel-actions"><button v-if="hasPermission('admin.content.draft')" @click="saveHome()">保存草稿</button><button @click="preview([homeCompositionKey], saveHome)">预览</button><button v-if="hasPermission('admin.content.publish')" class="publish" @click="publish([homeCompositionKey], saveHome)">提交发布</button></div></header>
      <div class="section-copy-grid"><label>英文标题<input v-model="composition.newsEyebrow"></label><label>中文标题<input v-model="composition.newsTitle"></label></div>
    </section>

    <section v-else-if="section === 'home-product'" class="content-panel home-compose">
      <header><div><h3>产品上新区外观</h3><p>只控制主页产品上新区标题与说明；已发布产品在“产品稿件”独立维护。</p></div><div class="panel-actions"><button v-if="hasPermission('admin.content.draft')" @click="saveHome()">保存草稿</button><button @click="preview([homeCompositionKey], saveHome)">预览</button><button v-if="hasPermission('admin.content.publish')" class="publish" @click="publish([homeCompositionKey], saveHome)">提交发布</button></div></header>
      <div class="section-copy-grid"><label>英文标题<input v-model="composition.productEyebrow"></label><label>中文标题<input v-model="composition.productTitle"></label></div>
    </section>

    <section v-else-if="section === 'home-video'" class="content-panel home-compose">
      <header><div><h3>最新视频区外观</h3><p>只控制主页视频区标题与说明；已发布视频在“视频稿件”独立维护。</p></div><div class="panel-actions"><button v-if="hasPermission('admin.content.draft')" @click="saveHome()">保存草稿</button><button @click="preview([homeCompositionKey], saveHome)">预览</button><button v-if="hasPermission('admin.content.publish')" class="publish" @click="publish([homeCompositionKey], saveHome)">提交发布</button></div></header>
      <div class="section-copy-grid"><label>英文标题<input v-model="composition.videoEyebrow"></label><label>中文标题<input v-model="composition.videoTitle"></label></div>
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
.site-content-admin{min-width:0;font-size:14px;line-height:1.55}
.site-content-head,.content-panel>header{display:flex;align-items:center;justify-content:space-between;gap:24px;padding:24px 26px;border:1px solid #35424a;background:#101821}
.site-content-head small{color:#55c6cd;font:900 12px monospace;letter-spacing:.18em}.site-content-head h2,.content-panel h3{margin:6px 0}.site-content-head p,.content-panel header p{margin:4px 0;color:#98a4a9;font-size:13px;line-height:1.6}
.site-content-head button,.content-panel button,.content-panel input,.content-panel textarea,.content-panel select{box-sizing:border-box;min-height:42px;padding:10px 12px;border:1px solid #4a5860;background:#070d12;color:#fff;font-size:14px}
.site-content-nav{display:flex;overflow-x:auto;margin:16px 0;border:1px solid #35424a;background:#0a1117}.site-content-nav button{flex:1 0 135px;min-height:46px;padding:12px 15px;border:0;border-right:1px solid #2d3940;background:transparent;color:#a0aaae;font-size:13px;font-weight:900}.site-content-nav button.active{background:#2a2311;color:#efd170;box-shadow:inset 0 -3px #d3b256}
.content-panel{padding:22px;border:1px solid #35424a;background:#0e161d}.content-panel>header{margin:-22px -22px 22px;border-width:0 0 1px}.panel-actions{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:10px}.panel-actions .publish{border-color:#2f785e;background:#0d251c;color:#7fe0b9}
.media-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(300px,100%),1fr));gap:16px;margin-top:20px}.media-grid article{min-width:0;overflow:hidden;border:1px solid #35434a;background:#091016}.media-grid img{width:100%;aspect-ratio:16/9;object-fit:cover}.media-grid article[data-kind="product"] img{aspect-ratio:4/3}.media-grid article[data-kind="hero"] img{aspect-ratio:600/351}.media-grid article[data-kind="article"] img{aspect-ratio:8/5}.media-grid article>div{display:flex;min-width:0;flex-direction:column;gap:8px;padding:15px}.media-grid code{overflow:hidden;color:#d6ba66;font-size:12px;text-overflow:ellipsis}.media-grid small{color:#98a3a8;font-size:12px}.media-grid button,.danger{border-color:#81434c!important;background:#281117!important;color:#eea6ae!important}.media-grid button:disabled,.category-row button:disabled{opacity:.45}
.home-compose h4{margin:28px 0 12px;padding-bottom:10px;border-bottom:1px solid #344149;font-size:17px}.home-compose h4 em{float:right;color:#7fd3ae;font-size:12px;font-style:normal}
.compose-row{display:grid;grid-template-columns:54px minmax(360px,.9fr) minmax(480px,1.1fr);gap:18px;margin-top:16px;padding:18px;border:1px solid #35434a;background:#091016}.compose-order{display:flex;flex-direction:column;align-items:center;justify-content:center;gap:8px}.compose-fields,.section-copy-grid{display:grid;grid-template-columns:1fr 1fr;gap:14px}.compose-fields label,.section-copy-grid label,.legal-editor>label{display:grid;gap:7px;color:#c0c8ca;font-size:13px;font-weight:900}.compose-fields .wide{grid-column:1/-1}.compose-fields .check{display:flex;align-items:center;gap:8px}.compose-fields .check input{width:auto;min-height:auto}.compose-media{display:grid;gap:13px}.compose-media>.danger{justify-self:end}
.line-length-hint{color:#8f9da3;font-size:12px;font-weight:500;line-height:1.55}.line-length-hint.warning{color:#f0bb72}.hero-copy-preview{min-width:0;overflow-x:auto;padding:16px;border-left:3px solid #d2b257;background:#0d151b;color:#f2f3ef}.hero-copy-preview>*{display:block;width:max-content;max-width:none;margin:0 0 8px;white-space:pre;overflow-wrap:normal;word-break:normal}.hero-copy-preview>small{color:#d8bc69;font-size:12px}.hero-copy-preview>b{font-size:24px;line-height:1.2}.hero-copy-preview>p{color:#bdc6c7;font-size:14px;line-height:1.6}.hero-copy-preview>strong{font-size:15px}.hero-copy-preview>i{color:#7f8c91;font-size:12px;font-style:normal}
.notice-compose-row{display:grid;grid-template-columns:34px 1fr 1fr 110px 80px repeat(3,58px);gap:10px;align-items:center;margin-top:12px}.notice-compose-row label{display:flex;align-items:center;gap:6px;font-size:13px}.notice-compose-row label input{width:auto;min-height:auto}.section-copy-grid{grid-template-columns:repeat(3,1fr)}.section-copy-grid input,.section-copy-grid textarea,.compose-fields input,.compose-fields textarea,.compose-media select,.notice-compose-row input,.notice-compose-row select,.category-manager input,.category-manager select,.legal-editor input,.legal-editor textarea{width:100%}
.new-category{display:grid;grid-template-columns:1fr 1fr auto;gap:12px}.category-row{display:grid;grid-template-columns:1fr 1fr 100px 120px 56px 56px 74px minmax(180px,1fr) 72px;gap:10px;align-items:center;margin-top:12px;padding:12px;border-bottom:1px solid #303c43}.category-row label{font-size:13px}.category-row label input{width:auto;min-height:auto}.category-row small{color:#98a3a8;font-size:12px}.legal-editor>label{margin-top:18px}.batch-history{margin-top:26px}.batch-history summary{color:#d8bc69;cursor:pointer;font-size:13px;font-weight:900}.batch-history article{display:flex;align-items:center;justify-content:space-between;padding:13px;border-bottom:1px solid #303c43}.batch-history article span{display:flex;flex-direction:column;gap:4px}.batch-history small{color:#98a3a8;font-size:12px}.empty{grid-column:1/-1;padding:38px;color:#8d999e;font-size:14px;text-align:center}
@media(max-width:1300px){.media-grid{grid-template-columns:1fr 1fr}.compose-row{grid-template-columns:54px 1fr}.compose-media{grid-column:2}.category-row{grid-template-columns:1fr 1fr 90px 110px repeat(3,56px)}.category-row select,.category-row .danger{grid-column:auto / span 3}.notice-compose-row{grid-template-columns:34px 1fr 1fr 100px}.notice-compose-row button{grid-row:2}}
@media(max-width:760px){.site-content-head,.content-panel>header{align-items:flex-start;flex-direction:column}.media-grid{grid-template-columns:1fr}.compose-row{grid-template-columns:1fr}.compose-order{flex-direction:row}.compose-media{grid-column:auto}.compose-fields,.section-copy-grid{grid-template-columns:1fr}.compose-fields .wide{grid-column:auto}.notice-compose-row,.new-category,.category-row{grid-template-columns:1fr}.notice-compose-row button,.category-row select,.category-row .danger{grid-column:auto;grid-row:auto}.panel-actions{justify-content:flex-start}}
</style>
