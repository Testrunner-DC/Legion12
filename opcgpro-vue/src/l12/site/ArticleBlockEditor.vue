<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch, type ComponentPublicInstance } from 'vue'
import type { SiteMedia } from '@/l12/platform'
import ArticleContentRenderer from './ArticleContentRenderer.vue'
import MediaUploadField from './MediaUploadField.vue'
import {
  newArticleImageBlock, newArticleTextBlock, parseArticleBody, safeArticleHref, serializeArticleBody,
  type ArticleBlock, type ArticleMark, type ArticleMarkType, type ArticleTextBlock, type ArticleTextBlockType,
} from './articleBlocks'

const props = withDefaults(defineProps<{ modelValue?: string; media?: SiteMedia[] }>(), { modelValue: '', media: () => [] })
const emit = defineEmits<{
  'update:modelValue': [value: string]
  'media-uploaded': [value: SiteMedia]
  notice: [value: string]
}>()

interface TextSelection { from: number; to: number }
interface LinkPanelState extends TextSelection { blockId: string; selectedText: string; hadLink: boolean }

const document = ref(parseArticleBody(props.modelValue))
const preview = ref(false)
const activeBlockId = ref('')
const textareas = new Map<string, HTMLTextAreaElement>()
const selections = ref<Record<string, TextSelection>>({})
const history = ref<string[]>([serializeArticleBody(document.value)])
const historyIndex = ref(0)
const historyPending = ref(false)
const linkPanel = ref<LinkPanelState | null>(null)
const linkHref = ref('')
const linkError = ref('')
const linkUrlInput = ref<HTMLInputElement | null>(null)
const HISTORY_DEBOUNCE_MS = 700
let historyTimer: ReturnType<typeof setTimeout> | undefined

const textBlockTypes: { id: ArticleTextBlockType; label: string }[] = [
  { id: 'paragraph', label: '正文段落' }, { id: 'h2', label: '二级标题 H2' }, { id: 'h3', label: '三级标题 H3' },
  { id: 'bulletList', label: '项目列表' }, { id: 'orderedList', label: '编号列表' }, { id: 'quote', label: '引用' },
]
const markLabels: Record<ArticleMarkType, string> = { bold: '粗体', italic: '斜体', underline: '下划线', strikethrough: '删除线', link: '链接' }
const articleMedia = computed(() => props.media.filter(item => item.kind === 'article'))
const mediaFor = (id: string) => articleMedia.value.find(item => item.id === id)
const canUndo = computed(() => historyIndex.value > 0 || historyPending.value)
const canRedo = computed(() => !historyPending.value && historyIndex.value < history.value.length - 1)

function currentBody() { return serializeArticleBody(document.value) }
function emitBody() { emit('update:modelValue', currentBody()) }
function clearHistoryTimer() {
  if (historyTimer !== undefined) clearTimeout(historyTimer)
  historyTimer = undefined
}
function pushHistory(value = currentBody()) {
  if (history.value[historyIndex.value] === value) return
  const next = [...history.value.slice(0, historyIndex.value + 1), value]
  history.value = next.length > 100 ? next.slice(next.length - 100) : next
  historyIndex.value = history.value.length - 1
}
function flushPendingHistory() {
  clearHistoryTimer()
  if (!historyPending.value) return
  historyPending.value = false
  pushHistory()
}
function scheduleHistory() {
  emitBody()
  historyPending.value = true
  clearHistoryTimer()
  historyTimer = setTimeout(flushPendingHistory, HISTORY_DEBOUNCE_MS)
}
function recordHistory() {
  clearHistoryTimer()
  historyPending.value = false
  pushHistory()
  emitBody()
}
function mutateStructure(change: () => void) {
  flushPendingHistory()
  change()
  recordHistory()
}
function restoreHistory(index: number) {
  if (index < 0 || index >= history.value.length) return
  clearHistoryTimer()
  historyPending.value = false
  historyIndex.value = index
  document.value = parseArticleBody(history.value[index])
  selections.value = {}
  linkPanel.value = null
  emitBody()
}
function undo() {
  flushPendingHistory()
  restoreHistory(historyIndex.value - 1)
}
function redo() {
  flushPendingHistory()
  restoreHistory(historyIndex.value + 1)
}

watch(() => props.modelValue, value => {
  const incoming = value || ''
  if (incoming === currentBody()) return
  clearHistoryTimer()
  historyPending.value = false
  document.value = parseArticleBody(incoming)
  const normalized = currentBody()
  history.value = [normalized]
  historyIndex.value = 0
  selections.value = {}
  linkPanel.value = null
})
onBeforeUnmount(clearHistoryTimer)

function bindTextarea(id: string, element: Element | ComponentPublicInstance | null) {
  if (element instanceof HTMLTextAreaElement) textareas.set(id, element)
  else textareas.delete(id)
}
function bindLinkInput(element: Element | ComponentPublicInstance | null) {
  linkUrlInput.value = element instanceof HTMLInputElement ? element : null
}
function captureSelection(block: ArticleTextBlock, event?: Event) {
  const currentTarget = event?.currentTarget
  const textarea = currentTarget instanceof HTMLTextAreaElement ? currentTarget : textareas.get(block.id)
  if (!textarea) return
  selections.value = {
    ...selections.value,
    [block.id]: { from: Math.min(textarea.selectionStart, block.text.length), to: Math.min(textarea.selectionEnd, block.text.length) },
  }
}
function selectionFor(block: ArticleTextBlock): TextSelection {
  const textarea = textareas.get(block.id)
  const saved = selections.value[block.id]
  const from = Math.max(0, Math.min(textarea?.selectionStart ?? saved?.from ?? 0, block.text.length))
  const to = Math.max(from, Math.min(textarea?.selectionEnd ?? saved?.to ?? from, block.text.length))
  return { from, to }
}
function selectionHasMark(block: ArticleTextBlock, type: ArticleMarkType) {
  const { from, to } = selectionFor(block)
  if (to <= from) return false
  let cursor = from
  const ranges = block.marks.filter(mark => mark.type === type && mark.to > from && mark.from < to)
    .sort((left, right) => left.from - right.from || left.to - right.to)
  for (const range of ranges) {
    if (range.from > cursor) return false
    cursor = Math.max(cursor, range.to)
    if (cursor >= to) return true
  }
  return false
}
function formatFeedback(block: ArticleTextBlock) {
  const { from, to } = selectionFor(block)
  if (to <= from) return '尚未选择文字；选择后可应用粗体、斜体或链接。'
  const active = (Object.keys(markLabels) as ArticleMarkType[]).filter(type => selectionHasMark(block, type)).map(type => markLabels[type])
  return `已选择 ${to - from} 个字符 · ${active.length ? `当前格式：${active.join('、')}` : '当前无行内格式'}`
}
function insertionIndex() {
  const active = document.value.blocks.findIndex(block => block.id === activeBlockId.value)
  return active < 0 ? document.value.blocks.length : active + 1
}
function addText(type: ArticleTextBlockType = 'paragraph') {
  mutateStructure(() => {
    const block = newArticleTextBlock(type)
    document.value.blocks.splice(insertionIndex(), 0, block)
    activeBlockId.value = block.id
  })
}
function addImage() {
  mutateStructure(() => {
    const block = newArticleImageBlock()
    document.value.blocks.splice(insertionIndex(), 0, block)
    activeBlockId.value = block.id
  })
}
function moveBlock(block: ArticleBlock, direction: -1 | 1) {
  const index = document.value.blocks.indexOf(block)
  const target = index + direction
  if (index < 0 || target < 0 || target >= document.value.blocks.length) return
  mutateStructure(() => {
    document.value.blocks.splice(index, 1)
    document.value.blocks.splice(target, 0, block)
  })
}
function removeBlock(block: ArticleBlock) {
  const index = document.value.blocks.indexOf(block)
  if (index < 0) return
  mutateStructure(() => {
    document.value.blocks.splice(index, 1)
    if (!document.value.blocks.length) document.value.blocks.push(newArticleTextBlock())
    if (linkPanel.value?.blockId === block.id) linkPanel.value = null
  })
}
function changeBlockType(block: ArticleTextBlock, event: Event) {
  const type = (event.target as HTMLSelectElement).value as ArticleTextBlockType
  mutateStructure(() => { block.type = type })
}
function updateText(block: ArticleTextBlock, event: Event) {
  const textarea = event.target as HTMLTextAreaElement
  const text = textarea.value.slice(0, 20_000)
  const previous = block.text
  let prefix = 0
  while (prefix < previous.length && prefix < text.length && previous[prefix] === text[prefix]) prefix++
  let suffix = 0
  while (suffix < previous.length - prefix && suffix < text.length - prefix && previous[previous.length - 1 - suffix] === text[text.length - 1 - suffix]) suffix++
  const removedEnd = previous.length - suffix
  const delta = text.length - previous.length
  block.marks = block.marks.flatMap(mark => {
    if (mark.to <= prefix) return [mark]
    if (mark.from >= removedEnd) return [{ ...mark, from: mark.from + delta, to: mark.to + delta }]
    const from = Math.min(mark.from, prefix)
    const to = Math.min(text.length, Math.max(prefix, mark.to + delta))
    return to > from ? [{ ...mark, from, to }] : []
  })
  block.text = text
  if (linkPanel.value?.blockId === block.id) linkPanel.value = null
  captureSelection(block, event)
  scheduleHistory()
}
function updateImageTextField(block: Extract<ArticleBlock, { type: 'image' }>, field: 'alt' | 'caption', value: string) {
  block[field] = value.slice(0, field === 'caption' ? 500 : 180)
  scheduleHistory()
}
function selectImageAsset(block: Extract<ArticleBlock, { type: 'image' }>, value: string) {
  mutateStructure(() => { block.mediaAssetId = value.slice(0, 100) })
}
function imageUploaded(block: Extract<ArticleBlock, { type: 'image' }>, media: SiteMedia) {
  mutateStructure(() => {
    block.mediaAssetId = media.id
    if (!block.alt.trim()) block.alt = media.altText
  })
  emit('media-uploaded', media)
}

function removeMarkRange(mark: ArticleMark, from: number, to: number): ArticleMark[] {
  if (mark.to <= from || mark.from >= to) return [mark]
  const result: ArticleMark[] = []
  if (mark.from < from) result.push({ ...mark, to: from })
  if (mark.to > to) result.push({ ...mark, from: to })
  return result
}
function toggleInlineMark(block: ArticleTextBlock, type: Exclude<ArticleMarkType, 'link'>) {
  const { from, to } = selectionFor(block)
  if (to <= from) {
    emit('notice', '请先在当前文字块中选择需要格式化的文字')
    return
  }
  const remove = selectionHasMark(block, type)
  mutateStructure(() => {
    if (remove) block.marks = block.marks.flatMap(mark => mark.type === type ? removeMarkRange(mark, from, to) : [mark])
    else block.marks.push({ type, from, to })
  })
  selections.value = { ...selections.value, [block.id]: { from, to } }
}
function restoreTextSelection(blockId: string, from: number, to: number) {
  nextTick(() => {
    const textarea = textareas.get(blockId)
    if (!textarea) return
    textarea.focus()
    textarea.setSelectionRange(from, to)
    const block = document.value.blocks.find(item => item.id === blockId)
    if (block && block.type !== 'image' && block.type !== 'divider') captureSelection(block)
  })
}
function openLinkPanel(block: ArticleTextBlock) {
  const { from, to } = selectionFor(block)
  if (to <= from) {
    emit('notice', '请先选择需要添加或编辑链接的文字')
    return
  }
  flushPendingHistory()
  const existing = block.marks.find(mark => mark.type === 'link' && mark.from < to && mark.to > from)
  linkPanel.value = { blockId: block.id, from, to, selectedText: block.text.slice(from, to), hadLink: Boolean(existing) }
  linkHref.value = existing?.href || ''
  linkError.value = ''
  nextTick(() => {
    linkUrlInput.value?.focus()
    linkUrlInput.value?.select()
  })
}
function closeLinkPanel(restoreSelection = true) {
  const panel = linkPanel.value
  linkPanel.value = null
  linkError.value = ''
  if (panel && restoreSelection) restoreTextSelection(panel.blockId, panel.from, panel.to)
}
function applyLink() {
  const panel = linkPanel.value
  if (!panel) return
  const href = safeArticleHref(linkHref.value)
  if (!href) {
    linkError.value = '请输入站内 /path 或以 http://、https:// 开头的有效地址。'
    return
  }
  const block = document.value.blocks.find(item => item.id === panel.blockId)
  if (!block || block.type === 'image' || block.type === 'divider' || panel.to > block.text.length || block.text.slice(panel.from, panel.to) !== panel.selectedText) {
    linkError.value = '所选文字已经变化，请取消后重新选择。'
    return
  }
  mutateStructure(() => {
    block.marks = block.marks.flatMap(mark => mark.type === 'link' ? removeMarkRange(mark, panel.from, panel.to) : [mark])
    block.marks.push({ type: 'link', from: panel.from, to: panel.to, href })
  })
  closeLinkPanel()
  emit('notice', '链接已应用到所选文字')
}
function removeLink() {
  const panel = linkPanel.value
  if (!panel) return
  const block = document.value.blocks.find(item => item.id === panel.blockId)
  if (!block || block.type === 'image' || block.type === 'divider') return
  mutateStructure(() => {
    block.marks = block.marks.flatMap(mark => mark.type === 'link' ? removeMarkRange(mark, panel.from, panel.to) : [mark])
  })
  closeLinkPanel()
  emit('notice', '所选文字的链接已移除')
}
function applyMark(block: ArticleTextBlock, type: ArticleMarkType) {
  if (type === 'link') openLinkPanel(block)
  else toggleInlineMark(block, type)
}
function handleLinkInputKeyboard(event: KeyboardEvent) {
  event.stopPropagation()
  if (event.key === 'Enter') { event.preventDefault(); applyLink() }
  else if (event.key === 'Escape') { event.preventDefault(); closeLinkPanel() }
}
function handleKeyboard(event: KeyboardEvent) {
  if (!(event.ctrlKey || event.metaKey)) return
  const key = event.key.toLowerCase()
  const target = event.target
  const textarea = target instanceof HTMLTextAreaElement ? target : null
  const block = textarea
    ? document.value.blocks.find(item => item.id === textarea.dataset.blockId && item.type !== 'image' && item.type !== 'divider') as ArticleTextBlock | undefined
    : undefined
  if (block && (key === 'b' || key === 'i' || key === 'k')) {
    event.preventDefault()
    captureSelection(block, event)
    applyMark(block, key === 'b' ? 'bold' : key === 'i' ? 'italic' : 'link')
    return
  }
  if (key === 'z') { event.preventDefault(); event.shiftKey ? redo() : undo() }
  else if (key === 'y') { event.preventDefault(); redo() }
}
</script>

<template>
  <section class="block-editor" @keydown="handleKeyboard">
    <header class="block-editor-head">
      <div><b>结构化正文编辑器</b><span>内容按白名单块保存，不接受任意 HTML；列表每行一项。支持 Ctrl/Cmd+B、I、K 与撤销重做。</span></div>
      <nav aria-label="正文编辑操作">
        <button type="button" :disabled="!canUndo" title="撤销 Ctrl/Cmd+Z" @click="undo">↶ 撤销</button>
        <button type="button" :disabled="!canRedo" title="重做 Ctrl/Cmd+Y" @click="redo">↷ 重做</button>
        <button type="button" :class="{ active: preview }" @click="preview = !preview">{{ preview ? '返回编辑' : '正文预览' }}</button>
      </nav>
    </header>
    <div class="insert-toolbar">
      <span>插入：</span><button type="button" @click="addText('paragraph')">段落</button><button type="button" @click="addText('h2')">H2</button><button type="button" @click="addText('h3')">H3</button><button type="button" @click="addText('bulletList')">项目列表</button><button type="button" @click="addText('orderedList')">编号列表</button><button type="button" @click="addText('quote')">引用</button><button type="button" class="image-button" @click="addImage">图片</button>
    </div>

    <ArticleContentRenderer v-if="preview" class="editor-preview" :body="currentBody()" :media="articleMedia"/>
    <div v-else class="block-list">
      <article v-for="(block, index) in document.blocks" :key="block.id" class="editor-block" :class="{ focused: activeBlockId === block.id }" @focusin="activeBlockId = block.id">
        <header>
          <select v-if="block.type !== 'image' && block.type !== 'divider'" :value="block.type" aria-label="内容块类型" @change="changeBlockType(block, $event)"><option v-for="item in textBlockTypes" :key="item.id" :value="item.id">{{ item.label }}</option></select>
          <b v-else>{{ block.type === 'divider' ? '分隔线' : '正文图片' }}</b>
          <nav><button type="button" :disabled="index === 0" aria-label="上移内容块" @click="moveBlock(block, -1)">↑ 上移</button><button type="button" :disabled="index === document.blocks.length - 1" aria-label="下移内容块" @click="moveBlock(block, 1)">↓ 下移</button><button type="button" class="delete" aria-label="删除内容块" @click="removeBlock(block)">删除</button></nav>
        </header>
        <template v-if="block.type !== 'image' && block.type !== 'divider'">
          <div class="format-toolbar">
            <span>选中文字后：</span>
            <button type="button" title="粗体 Ctrl/Cmd+B" :class="{ active: selectionHasMark(block, 'bold') }" :aria-pressed="selectionHasMark(block, 'bold')" @mousedown.prevent @click="applyMark(block, 'bold')"><strong>B</strong> 粗体</button>
            <button type="button" title="斜体 Ctrl/Cmd+I" :class="{ active: selectionHasMark(block, 'italic') }" :aria-pressed="selectionHasMark(block, 'italic')" @mousedown.prevent @click="applyMark(block, 'italic')"><em>I</em> 斜体</button>
            <button type="button" title="链接 Ctrl/Cmd+K" :class="{ active: selectionHasMark(block, 'link') }" :aria-pressed="selectionHasMark(block, 'link')" @mousedown.prevent @click="applyMark(block, 'link')">🔗 链接</button>
          </div>
          <p class="format-feedback" aria-live="polite">{{ formatFeedback(block) }}</p>
          <div v-if="linkPanel?.blockId === block.id" class="link-panel" role="group" aria-label="所选文字链接工具">
            <header><strong>添加或编辑链接</strong><button type="button" aria-label="取消链接编辑" @click="closeLinkPanel()">×</button></header>
            <p>已选择：<q>{{ linkPanel.selectedText }}</q></p>
            <label>链接地址<input :ref="bindLinkInput" v-model="linkHref" type="url" inputmode="url" autocomplete="url" placeholder="/news/example 或 https://example.com" @input="linkError = ''" @keydown="handleLinkInputKeyboard"></label>
            <small>仅允许站内 /path 或 http(s) 地址；不会接受脚本、data URL 或任意 HTML。</small>
            <p v-if="linkError" class="link-error" role="alert">{{ linkError }}</p>
            <footer><button type="button" class="apply" @click="applyLink">应用链接</button><button type="button" :disabled="!linkPanel.hadLink" @click="removeLink">移除链接</button><button type="button" @click="closeLinkPanel()">取消</button></footer>
          </div>
          <textarea :ref="element => bindTextarea(block.id, element)" :data-block-id="block.id" :value="block.text" :rows="block.type.includes('List') ? 6 : block.type === 'paragraph' ? 7 : 3" maxlength="20000" :placeholder="block.type.includes('List') ? '每行填写一个列表项' : '输入正文；粘贴内容将作为纯文本处理'" @focus="captureSelection(block, $event)" @select="captureSelection(block, $event)" @keyup="captureSelection(block, $event)" @mouseup="captureSelection(block, $event)" @input="updateText(block, $event)" @blur="flushPendingHistory"/>
        </template>
        <template v-else-if="block.type === 'image'">
          <figure v-if="mediaFor(block.mediaAssetId)" class="selected-image-preview">
            <img :src="mediaFor(block.mediaAssetId)?.thumbnailUrl" :alt="block.alt || mediaFor(block.mediaAssetId)?.altText || '已选正文图片预览'">
            <figcaption><b>已选正文图片</b><span>{{ block.alt || mediaFor(block.mediaAssetId)?.altText || '尚未填写替代文字' }}</span></figcaption>
          </figure>
          <p v-else class="empty-image-preview">尚未选择正文图片；可从素材库选择或在下方上传。</p>
          <div class="image-fields">
            <label>选择已上传正文图片<select :value="block.mediaAssetId" @change="selectImageAsset(block, ($event.target as HTMLSelectElement).value)"><option value="">请选择</option><option v-for="item in articleMedia" :key="item.id" :value="item.id">{{ item.altText || item.contentHash.slice(0, 12) }}</option></select></label>
            <label>替代文字（发布必填）<input :value="block.alt" maxlength="180" placeholder="准确描述图片中的关键信息" @input="updateImageTextField(block, 'alt', ($event.target as HTMLInputElement).value)" @blur="flushPendingHistory"></label>
            <label>图片说明（可选）<input :value="block.caption" maxlength="500" placeholder="显示在图片下方" @input="updateImageTextField(block, 'caption', ($event.target as HTMLInputElement).value)" @blur="flushPendingHistory"></label>
          </div>
          <details class="inline-upload"><summary>上传一张新的资讯正文图片</summary><MediaUploadField kind="article" :preview-url="mediaFor(block.mediaAssetId)?.thumbnailUrl" :initial-alt="block.alt" @uploaded="imageUploaded(block, $event)" @notice="emit('notice', $event)"/></details>
        </template>
      </article>
    </div>
  </section>
</template>

<style scoped>
.block-editor{grid-column:1/-1;border:1px solid #46545d;background:#0b1218;color:#d6dcde}.block-editor-head{display:flex;align-items:center;justify-content:space-between;gap:18px;padding:18px 20px;border-bottom:1px solid #3a474e;background:#111b22}.block-editor-head>div{display:grid;gap:5px}.block-editor-head b{font-size:16px}.block-editor-head span{color:#98a4a9;font-size:12px;line-height:1.55}.block-editor nav,.insert-toolbar,.format-toolbar{display:flex;flex-wrap:wrap;align-items:center;gap:8px}.block-editor button,.block-editor select,.block-editor input,.block-editor textarea{border:1px solid #53616a;background:#070d12;color:#f3f5f4;font:inherit}.block-editor button{min-height:38px;padding:8px 12px;font-size:13px;font-weight:800}.block-editor button:hover:not(:disabled),.block-editor button.active{border-color:#d1b35e;color:#efd47f}.block-editor button:disabled{cursor:not-allowed;opacity:.38}.insert-toolbar{padding:14px 20px;border-bottom:1px solid #344149}.insert-toolbar>span,.format-toolbar>span{color:#9ca7ab;font-size:12px}.insert-toolbar .image-button{border-color:#3c8b91;color:#71d3d8}.block-list{display:grid;gap:16px;padding:20px}.editor-block{border:1px solid #3b4850;background:#0e171e}.editor-block.focused{border-color:#74858e;box-shadow:0 0 0 1px rgba(209,179,94,.35)}.editor-block>header{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:11px 13px;border-bottom:1px solid #303d44;background:#141e25}.editor-block>header select{min-width:180px;padding:8px 10px;font-size:13px}.editor-block>header>b{font-size:14px}.editor-block>header button{min-height:34px;padding:6px 10px;font-size:12px}.editor-block .delete{border-color:#74414a;color:#ef9aa4}.format-toolbar{padding:10px 13px 4px}.format-toolbar button{min-height:34px;padding:6px 10px}.format-feedback{margin:0;padding:0 13px 10px;color:#aeb8bb;font-size:12px;line-height:1.55}.editor-block>textarea{box-sizing:border-box;width:calc(100% - 26px);margin:0 13px 14px;padding:13px 14px;resize:vertical;font-size:14px;line-height:1.75}.link-panel{display:grid;gap:10px;margin:4px 13px 14px;padding:14px 16px;border:1px solid #957d3f;background:#17180f;box-shadow:0 12px 28px rgba(0,0,0,.28)}.link-panel>header{display:flex;align-items:center;justify-content:space-between;gap:12px}.link-panel>header strong{color:#efd47f;font-size:14px}.link-panel>header button{min-width:34px;min-height:32px;padding:4px 9px}.link-panel p{margin:0;color:#c2cbcd;font-size:13px;line-height:1.55}.link-panel q{color:#fff;word-break:break-word}.link-panel label{display:grid;gap:7px;color:#cbd2d4;font-size:13px;font-weight:800}.link-panel input{box-sizing:border-box;width:100%;min-height:42px;padding:9px 11px;font-size:14px}.link-panel small{color:#98a4a9;font-size:12px;line-height:1.55}.link-panel .link-error{color:#ff9ea8}.link-panel footer{display:flex;flex-wrap:wrap;gap:8px}.link-panel .apply{border-color:#b99b45;color:#efd47f}.selected-image-preview{display:grid;grid-template-columns:minmax(180px,320px) minmax(0,1fr);align-items:center;gap:16px;margin:16px;padding:14px;border:1px solid #3f5059;background:#091116}.selected-image-preview img{display:block;width:100%;max-height:220px;object-fit:contain;background:#04080b}.selected-image-preview figcaption{display:grid;gap:7px;min-width:0}.selected-image-preview figcaption b{color:#71d3d8;font-size:14px}.selected-image-preview figcaption span{overflow-wrap:anywhere;color:#bac4c7;font-size:13px;line-height:1.6}.empty-image-preview{margin:16px;padding:16px;border:1px dashed #4a5961;color:#aeb8bb;font-size:13px;line-height:1.6}.image-fields{display:grid;grid-template-columns:1fr 1fr;gap:14px;padding:16px}.image-fields label{display:grid;gap:7px;color:#bcc5c8;font-size:13px;font-weight:800}.image-fields label:first-child{grid-column:1/-1}.image-fields input,.image-fields select{box-sizing:border-box;width:100%;min-height:42px;padding:9px 11px;font-size:14px}.inline-upload{margin:0 16px 16px}.inline-upload summary{cursor:pointer;padding:12px;border:1px solid #3c8b91;color:#75d3d7;font-size:13px;font-weight:900}.inline-upload[open] summary{margin-bottom:12px}.editor-preview{margin:20px;padding:clamp(20px,4vw,48px);background:#f5f0df;color:#20282b}@media(max-width:780px){.block-editor-head{align-items:flex-start;flex-direction:column}.block-list{padding:12px}.editor-block>header{align-items:flex-start;flex-direction:column}.selected-image-preview{grid-template-columns:1fr}.image-fields{grid-template-columns:1fr}.image-fields label:first-child{grid-column:auto}}
</style>
