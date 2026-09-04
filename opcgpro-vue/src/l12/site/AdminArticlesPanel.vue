<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { adminApi, hasPermission, type Article, type ArticleDraft, type ArticleRevision } from '@/l12/platform'

type EditableArticle = Partial<Article> & ArticleDraft

const categories = ['官方公告', '规则勘误', '赛季更新', '赛事信息']
const statuses = [
  { id: '', name: '全部状态' }, { id: 'draft', name: '草稿' }, { id: 'published', name: '已发布' },
  { id: 'scheduled', name: '定时发布' }, { id: 'withdrawn', name: '已撤回' }, { id: 'archived', name: '已归档' },
]
const articles = ref<Article[]>([])
const selected = ref<EditableArticle | null>(null)
const revisions = ref<ArticleRevision[]>([])
const search = ref('')
const status = ref('')
const category = ref('')
const busy = ref(false)
const notice = ref('')
const preview = ref(false)

const statusLabel = (value?: string) => statuses.find(item => item.id === value)?.name || '未保存'
const selectedIsSaved = computed(() => Boolean(selected.value?.id))

function emptyArticle(): EditableArticle {
  return { title: '', summary: '', body: '', category: '官方公告', coverUrl: '', link: '', slug: '', pinned: false, publishAt: '' }
}
function dateTimeLocal(value?: string) {
  if (!value) return ''
  const date = new Date(value)
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}
function selectArticle(article: Article) {
  selected.value = { ...article, publishAt: dateTimeLocal(article.publishAt) }
  void loadRevisions(article.id)
}
function createArticle() { selected.value = emptyArticle(); revisions.value = []; preview.value = false }
async function load() {
  busy.value = true; notice.value = ''
  try {
    articles.value = await adminApi.articles({ status: status.value, category: category.value, search: search.value.trim() })
    if (selected.value?.id) {
      const fresh = articles.value.find(item => item.id === selected.value?.id)
      if (fresh) selectArticle(fresh)
    }
  } catch (error) { notice.value = error instanceof Error ? error.message : '稿件列表加载失败' }
  finally { busy.value = false }
}
async function save() {
  if (!selected.value) return
  busy.value = true; notice.value = ''
  try {
    const saved = await adminApi.saveArticle({ ...selected.value, publishAt: selected.value.publishAt ? new Date(selected.value.publishAt).toISOString() : undefined })
    selected.value = { ...saved, publishAt: dateTimeLocal(saved.publishAt) }
    notice.value = '草稿已保存，线上文章未改变'
    await load(); await loadRevisions(saved.id)
  } catch (error) { notice.value = error instanceof Error ? error.message : '稿件保存失败' }
  finally { busy.value = false }
}
async function mutate(action: 'publish' | 'withdraw' | 'archive' | 'restore') {
  if (!selected.value?.id) { notice.value = '请先保存稿件'; return }
  if (action === 'archive' && !window.confirm('归档后文章不再公开展示，确认继续？')) return
  busy.value = true; notice.value = ''
  try {
    const id = selected.value.id
    const updated = action === 'publish' ? await adminApi.publishArticle(id)
      : action === 'withdraw' ? await adminApi.withdrawArticle(id)
        : action === 'archive' ? await adminApi.archiveArticle(id) : await adminApi.restoreArticle(id)
    selected.value = { ...updated, publishAt: dateTimeLocal(updated.publishAt) }
    notice.value = action === 'publish' ? (updated.status === 'scheduled' ? '稿件已安排定时发布' : '稿件已正式发布')
      : action === 'withdraw' ? '文章已撤回' : action === 'archive' ? '稿件已归档' : '稿件已恢复为草稿'
    await load(); await loadRevisions(id)
  } catch (error) { notice.value = error instanceof Error ? error.message : '稿件状态更新失败' }
  finally { busy.value = false }
}
async function loadRevisions(id: string) {
  try { revisions.value = await adminApi.articleRevisions(id) }
  catch { revisions.value = [] }
}
async function restoreRevision(revision: number) {
  if (!selected.value?.id || !window.confirm(`将版本 ${revision} 恢复为新的草稿？`)) return
  busy.value = true
  try {
    const restored = await adminApi.restoreArticleRevision(selected.value.id, revision)
    selected.value = { ...restored, publishAt: dateTimeLocal(restored.publishAt) }
    notice.value = `版本 ${revision} 已恢复为草稿，尚未影响线上文章`
    await load(); await loadRevisions(restored.id)
  } catch (error) { notice.value = error instanceof Error ? error.message : '历史版本恢复失败' }
  finally { busy.value = false }
}
onMounted(load)
</script>

<template>
  <section class="article-workbench">
    <header class="article-page-head">
      <div><small>EDITORIAL DESK</small><h2>资讯发布</h2><p>逐篇管理稿件、封面、链接、发布状态和历史版本；与官网固定文案完全分开。</p></div>
      <button v-if="hasPermission('admin.content.draft')" class="new-button" @click="createArticle">＋ 新建稿件</button>
    </header>
    <div class="article-layout">
      <aside class="article-list">
        <div class="article-filters">
          <input v-model="search" placeholder="搜索标题、摘要或正文" @keyup.enter="load">
          <select v-model="status" @change="load"><option v-for="item in statuses" :key="item.id" :value="item.id">{{ item.name }}</option></select>
          <select v-model="category" @change="load"><option value="">全部分类</option><option v-for="item in categories" :key="item">{{ item }}</option></select>
          <button :disabled="busy" @click="load">{{ busy ? '读取中' : '查询' }}</button>
        </div>
        <div class="article-scroll">
          <button v-for="article in articles" :key="article.id" class="article-item" :class="{ active: selected?.id === article.id }" @click="selectArticle(article)">
            <img v-if="article.coverUrl" :src="article.coverUrl" alt="">
            <span v-else class="cover-empty">NEWS</span>
            <span class="article-item-copy"><b>{{ article.title || '未命名稿件' }}</b><small>{{ article.category }} · {{ statusLabel(article.status) }}</small><em>{{ new Date(article.updatedAt).toLocaleString() }}<template v-if="article.hasUnpublishedChanges"> · 有未发布修改</template></em></span>
          </button>
          <div v-if="!articles.length" class="article-empty">当前筛选下没有稿件</div>
        </div>
      </aside>

      <main v-if="selected" class="article-editor">
        <header><div><small>{{ selected.id ? `稿件 ${selected.id.slice(0, 10)}` : 'NEW ARTICLE' }}</small><h3>{{ selected.title || '新建稿件' }}</h3></div><span class="status-chip" :data-status="selected.status || 'draft'">{{ statusLabel(selected.status) }}</span></header>
        <div class="editor-grid">
          <label class="wide">标题<input v-model="selected.title" maxlength="180" placeholder="输入资讯标题"></label>
          <label>分类<select v-model="selected.category"><option v-for="item in categories" :key="item">{{ item }}</option></select></label>
          <label>发布时间<input v-model="selected.publishAt" type="datetime-local"></label>
          <label class="wide">摘要<textarea v-model="selected.summary" maxlength="600" rows="3" placeholder="用于首页和资讯列表展示"></textarea></label>
          <label>封面图片地址<input v-model="selected.coverUrl" placeholder="https://… 或 /assets/…"></label>
          <label>文章链接<input v-model="selected.link" placeholder="可选：站内路径或 https://…"></label>
          <label class="wide">链接标识<input v-model="selected.slug" maxlength="100" placeholder="留空时由系统生成"></label>
          <label class="wide body-field">正文<textarea v-model="selected.body" maxlength="100000" rows="18" placeholder="输入完整正文；保留换行"></textarea></label>
        </div>
        <div class="article-options"><label><input v-model="selected.pinned" type="checkbox"> 置顶文章</label><span>{{ selected.body.length.toLocaleString() }} 字符</span></div>
        <figure v-if="selected.coverUrl" class="cover-preview"><img :src="selected.coverUrl" alt="封面预览"><figcaption>封面预览</figcaption></figure>
        <footer class="editor-actions">
          <button @click="preview = !preview">{{ preview ? '关闭预览' : '预览稿件' }}</button>
          <button v-if="hasPermission('admin.content.draft')" :disabled="busy" @click="save">保存草稿</button>
          <button v-if="selectedIsSaved && hasPermission('admin.content.publish') && selected.status !== 'withdrawn' && selected.status !== 'archived'" class="publish" :disabled="busy" @click="mutate('publish')">{{ selected.publishAt && new Date(selected.publishAt) > new Date() ? '安排发布' : '正式发布' }}</button>
          <button v-if="selectedIsSaved && hasPermission('admin.content.publish') && (selected.status === 'published' || selected.status === 'scheduled')" class="warning" @click="mutate('withdraw')">撤回</button>
          <button v-if="selectedIsSaved && selected.status !== 'archived'" class="danger" @click="mutate('archive')">归档</button>
          <button v-if="selectedIsSaved && selected.status === 'archived'" @click="mutate('restore')">恢复为草稿</button>
        </footer>

        <article v-if="preview" class="article-preview">
          <img v-if="selected.coverUrl" :src="selected.coverUrl" alt="">
          <small>{{ selected.category }}</small><h2>{{ selected.title || '未填写标题' }}</h2><p class="summary">{{ selected.summary }}</p><p class="body">{{ selected.body || '尚未填写正文' }}</p>
        </article>

        <details v-if="selectedIsSaved" class="revision-history">
          <summary>历史版本（{{ revisions.length }}）</summary>
          <div v-for="item in revisions" :key="item.revision"><span><b>版本 {{ item.revision }} · {{ item.action }}</b><small>{{ item.actor }} · {{ new Date(item.createdAt).toLocaleString() }}</small></span><button @click="restoreRevision(item.revision)">恢复为草稿</button></div>
        </details>
      </main>
      <main v-else class="article-editor article-welcome"><b>选择一篇稿件开始编辑</b><span>也可以新建稿件。发布与撤回均会写入后台审计记录。</span></main>
    </div>
    <p v-if="notice" class="article-notice">{{ notice }}</p>
  </section>
</template>

<style scoped>
.article-workbench{min-width:0}.article-page-head{display:flex;align-items:flex-end;justify-content:space-between;gap:20px;padding:20px;border:1px solid #35424a;background:#101821}.article-page-head small,.article-editor header small{color:#d5b85e;font:900 9px monospace;letter-spacing:.16em}.article-page-head h2{margin:5px 0}.article-page-head p{margin:0;color:#7d898e;font-size:11px}.new-button,.article-workbench button,.article-workbench input,.article-workbench select,.article-workbench textarea{box-sizing:border-box;border:1px solid #4c5961;background:#080e13;color:#fff;font:700 11px 'Microsoft YaHei';padding:9px}.new-button{border-color:#d3b65e!important;background:#d3b65e!important;color:#101214!important}.article-layout{display:grid;grid-template-columns:minmax(280px,.72fr) minmax(520px,1.5fr);gap:12px;margin-top:12px}.article-list,.article-editor{min-width:0;border:1px solid #35424a;background:#101821}.article-filters{display:grid;grid-template-columns:1fr 1fr;gap:7px;padding:12px;border-bottom:1px solid #35424a}.article-filters input{grid-column:1/-1}.article-scroll{max-height:calc(100vh - 280px);min-height:620px;overflow-y:auto}.article-item{display:grid;width:100%;grid-template-columns:62px 1fr;align-items:center;gap:10px;padding:10px!important;border-width:0 0 1px!important;border-color:#2e3a42!important;background:#0b1218!important;text-align:left}.article-item.active{background:#222012!important;box-shadow:inset 3px 0 #d5b85e}.article-item img,.cover-empty{display:grid;width:62px;height:44px;place-items:center;object-fit:cover;border:1px solid #46535a;background:#151d23;color:#7e8a8f;font:900 9px monospace}.article-item-copy{display:flex;min-width:0;flex-direction:column;gap:4px}.article-item-copy b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.article-item-copy small{color:#d3b65e}.article-item-copy em{color:#738188;font-size:8px;font-style:normal}.article-empty,.article-welcome{display:grid;min-height:260px;place-content:center;color:#75838a;text-align:center}.article-welcome span{margin-top:7px;font-size:10px}.article-editor{padding:18px}.article-editor>header{display:flex;align-items:flex-start;justify-content:space-between;padding-bottom:12px;border-bottom:1px solid #35424a}.article-editor h3{margin:4px 0 0;font-size:20px}.status-chip{padding:5px 8px;border:1px solid #705b2d;background:#251d0d;color:#e2c56e;font-size:9px;font-weight:900}.status-chip[data-status="published"]{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.status-chip[data-status="scheduled"]{border-color:#346b80;background:#0b2028;color:#77cfe6}.status-chip[data-status="withdrawn"],.status-chip[data-status="archived"]{border-color:#7d4149;background:#281217;color:#e8a3aa}.editor-grid{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-top:14px}.editor-grid label{color:#b8c0c1;font-size:10px;font-weight:900}.editor-grid label.wide{grid-column:1/-1}.editor-grid input,.editor-grid select,.editor-grid textarea{display:block;width:100%;margin-top:6px;resize:vertical}.body-field textarea{line-height:1.75}.article-options{display:flex;align-items:center;justify-content:space-between;margin-top:12px;color:#87949a;font-size:10px}.article-options label{color:#d6c58c;font-weight:900}.article-options input{width:auto;margin-right:5px}.cover-preview{margin:14px 0 0}.cover-preview img{display:block;width:min(520px,100%);max-height:240px;object-fit:cover;border:1px solid #46535a}.cover-preview figcaption{margin-top:5px;color:#738188;font-size:9px}.editor-actions{display:flex;flex-wrap:wrap;gap:8px;margin-top:16px;padding-top:14px;border-top:1px solid #35424a}.editor-actions .publish{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.editor-actions .warning{border-color:#8a6b32;background:#20190d;color:#e6cb7b}.editor-actions .danger{margin-left:auto;border-color:#7e3c45;background:#2b1116;color:#eab5bb}.article-preview{margin-top:16px;padding:22px;border:1px solid #4b5960;background:#081016}.article-preview>img{width:100%;max-height:280px;object-fit:cover}.article-preview>small{display:block;margin-top:14px;color:#d4b75f}.article-preview h2{margin:7px 0;font-size:28px}.article-preview .summary{color:#a8b2b4;font-weight:900}.article-preview .body{color:#c5cdca;line-height:1.9;white-space:pre-wrap}.revision-history{margin-top:16px;border-top:1px solid #35424a;padding-top:12px}.revision-history summary{cursor:pointer;color:#d6bd70;font-size:10px;font-weight:900}.revision-history>div{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:9px 0;border-bottom:1px solid #2e3940}.revision-history span,.revision-history small{display:block}.revision-history small{margin-top:3px;color:#748188}.article-notice{position:sticky;z-index:5;bottom:12px;padding:11px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584;font-size:10px}
@media(max-width:1100px){.article-layout{grid-template-columns:1fr}.article-scroll{min-height:0;max-height:320px}.article-editor{min-height:520px}}@media(max-width:650px){.article-page-head{align-items:stretch;flex-direction:column}.editor-grid,.article-filters{grid-template-columns:1fr}.editor-grid label.wide,.article-filters input{grid-column:auto}.editor-actions .danger{margin-left:0}.article-item{grid-template-columns:52px 1fr}.article-item img,.cover-empty{width:52px;height:40px}}
</style>
