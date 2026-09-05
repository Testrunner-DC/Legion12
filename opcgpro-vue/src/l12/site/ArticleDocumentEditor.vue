<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import type { SiteMedia } from '@/l12/platform'
import ArticleContentRenderer from './ArticleContentRenderer.vue'
import MediaUploadField from './MediaUploadField.vue'
import { articleBlockId, articleInlineRuns, articleListItems, newArticleTextBlock, normalizeArticleBlockIds, parseArticleBody, safeArticleHref, serializeArticleBody, type ArticleBlock, type ArticleBodyDocument, type ArticleMark, type ArticleMarkType, type ArticleTextAlign, type ArticleTextBlock } from './articleBlocks'

const props = withDefaults(defineProps<{ modelValue?: string; media?: SiteMedia[] }>(), { modelValue: '', media: () => [] })
const emit = defineEmits<{ 'update:modelValue': [value: string]; 'media-uploaded': [value: SiteMedia]; notice: [value: string] }>()
const canvas = ref<HTMLDivElement | null>(null)
const articleDocument = ref(parseArticleBody(props.modelValue))
const preview = ref(false)
const history = ref<string[]>([serializeArticleBody(articleDocument.value)])
const historyIndex = ref(0)
const historyPending = ref(false)
const linkPanel = ref(false)
const linkHref = ref('')
const linkError = ref('')
const imagePanel = ref(false)
const imageAssetId = ref('')
const imageAlt = ref('')
const imageCaption = ref('')
const editingFigure = ref<HTMLElement | null>(null)
const recentMedia = ref<SiteMedia[]>([])
const HISTORY_DEBOUNCE_MS = 700
let historyTimer: ReturnType<typeof setTimeout> | undefined
let savedRange: Range | null = null
let rendering = false

const articleMedia = computed(() => [...new Map([...recentMedia.value, ...props.media].filter(item => item.kind === 'article').map(item => [item.id, item])).values()])
const mediaFor = (id: string) => articleMedia.value.find(item => item.id === id)
const canUndo = computed(() => historyIndex.value > 0 || historyPending.value)
const canRedo = computed(() => !historyPending.value && historyIndex.value < history.value.length - 1)
const characterCount = computed(() => articleDocument.value.blocks.reduce((sum, block) => sum + (block.type === 'image' || block.type === 'divider' ? 0 : block.text.length), 0))
const imageCount = computed(() => articleDocument.value.blocks.filter(block => block.type === 'image').length)

function escapeHtml(value: string) { return value.replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character]!)) }
function inlineHtml(block: ArticleTextBlock, from = 0, to = block.text.length) {
  return articleInlineRuns(block.text, block.marks, from, to).map(run => {
    let content = escapeHtml(run.text).replace(/\n/g, '<br>')
    if (run.bold) content = `<strong>${content}</strong>`
    if (run.italic) content = `<em>${content}</em>`
    if (run.underline) content = `<u>${content}</u>`
    if (run.strikethrough) content = `<s>${content}</s>`
    if (run.href) content = `<a href="${escapeHtml(run.href)}">${content}</a>`
    return content
  }).join('') || '<br>'
}
function blockHtml(block: ArticleBlock) {
  const id = escapeHtml(block.id)
  if (block.type === 'divider') return `<hr data-block-id="${id}">`
  if (block.type === 'image') {
    const media = mediaFor(block.mediaAssetId)
    if (!media) return ''
    return `<figure data-block-id="${id}" data-media-id="${escapeHtml(block.mediaAssetId)}" data-alt="${escapeHtml(block.alt)}" data-caption="${escapeHtml(block.caption)}" contenteditable="false"><img src="${escapeHtml(media.desktopUrl)}" alt="${escapeHtml(block.alt || media.altText)}">${block.caption ? `<figcaption>${escapeHtml(block.caption)}</figcaption>` : ''}<button type="button" data-edit-image="true" tabindex="-1">编辑图片</button></figure>`
  }
  const align = ` data-align="${block.align}" style="text-align:${block.align}"`
  if (block.type === 'bulletList' || block.type === 'orderedList') {
    const tag = block.type === 'bulletList' ? 'ul' : 'ol'
    const items = articleListItems(block.text)
    return `<${tag} data-block-id="${id}"${align}>${(items.length ? items : [{ from: 0, to: 0 }]).map(item => `<li>${inlineHtml(block, item.from, item.to)}</li>`).join('')}</${tag}>`
  }
  const tag = block.type === 'paragraph' ? 'p' : block.type
  return `<${tag} data-block-id="${id}"${align}>${inlineHtml(block)}</${tag}>`
}
function documentHtml(value: ArticleBodyDocument) { return value.blocks.map(blockHtml).join('') || `<p data-block-id="${articleBlockId()}" data-align="left"><br></p>` }
function renderCanvas() {
  if (!canvas.value || preview.value) return
  rendering = true; canvas.value.innerHTML = documentHtml(articleDocument.value); rendering = false
}

function nodeText(node: Node, marks: ArticleMark[], inherited: { type: ArticleMarkType; href?: string }[] = []): string {
  if (node.nodeType === Node.TEXT_NODE) return node.textContent || ''
  if (!(node instanceof HTMLElement)) return ''
  if (node.tagName === 'BR') return '\n'
  const active = [...inherited]
  if (['B', 'STRONG'].includes(node.tagName)) active.push({ type: 'bold' })
  if (['I', 'EM'].includes(node.tagName)) active.push({ type: 'italic' })
  if (node.tagName === 'U') active.push({ type: 'underline' })
  if (['S', 'STRIKE', 'DEL'].includes(node.tagName)) active.push({ type: 'strikethrough' })
  if (node.tagName === 'A') { const href = safeArticleHref(node.getAttribute('href') || ''); if (href) active.push({ type: 'link', href }) }
  let text = ''
  for (const child of node.childNodes) {
    const offset = text.length
    const childMarks: ArticleMark[] = []
    const childText = nodeText(child, childMarks, active)
    text += childText
    marks.push(...childMarks.map(mark => ({ ...mark, from: mark.from + offset, to: mark.to + offset })))
  }
  if (text) for (const mark of active) marks.push({ type: mark.type, from: 0, to: text.length, ...(mark.href ? { href: mark.href } : {}) })
  return text
}
function normalizeMarks(marks: ArticleMark[], length: number) {
  const sorted = marks.filter(mark => mark.from >= 0 && mark.to > mark.from && mark.to <= length).sort((a, b) => a.type.localeCompare(b.type) || (a.href || '').localeCompare(b.href || '') || a.from - b.from || a.to - b.to)
  const merged: ArticleMark[] = []
  for (const mark of sorted) {
    const previous = merged.at(-1)
    if (previous && previous.type === mark.type && previous.href === mark.href && mark.from <= previous.to) previous.to = Math.max(previous.to, mark.to)
    else merged.push({ ...mark })
  }
  return merged.slice(0, 200)
}
function elementAlign(element: HTMLElement): ArticleTextAlign {
  const value = (element.dataset.align || element.style.textAlign || '').toLowerCase()
  return value === 'center' || value === 'right' || value === 'justify' ? value : 'left'
}
function textBlock(element: HTMLElement, type: ArticleTextBlock['type']): ArticleTextBlock {
  const marks: ArticleMark[] = []
  const text = nodeText(element, marks).replace(/\n$/, '').slice(0, 20_000)
  return { id: element.dataset.blockId || '', type, text, marks: normalizeMarks(marks, text.length), align: elementAlign(element) }
}
function listBlock(element: HTMLElement, type: 'bulletList' | 'orderedList'): ArticleTextBlock {
  const marks: ArticleMark[] = []; let text = ''
  ;([...element.children].filter(child => child.tagName === 'LI') as HTMLElement[]).forEach((item, index) => {
    if (index) text += '\n'
    const offset = text.length; const itemMarks: ArticleMark[] = []
    text += nodeText(item, itemMarks).replace(/\n/g, ' ').slice(0, 20_000 - text.length)
    marks.push(...itemMarks.map(mark => ({ ...mark, from: mark.from + offset, to: mark.to + offset })))
  })
  return { id: element.dataset.blockId || '', type, text, marks: normalizeMarks(marks, text.length), align: elementAlign(element) }
}
function parseCanvas(): ArticleBodyDocument {
  const blocks: ArticleBlock[] = []
  const elements: { element: HTMLElement; blockIndex: number }[] = []
  for (const node of canvas.value?.childNodes || []) {
    if (node.nodeType === Node.TEXT_NODE) { const text = (node.textContent || '').trim(); if (text) blocks.push(newArticleTextBlock('paragraph', text)); continue }
    if (!(node instanceof HTMLElement)) continue
    const tag = node.tagName
    const blockIndex = blocks.length
    if (tag === 'FIGURE') blocks.push({ id: node.dataset.blockId || '', type: 'image', mediaAssetId: (node.dataset.mediaId || '').slice(0, 100), alt: (node.dataset.alt || '').slice(0, 180), caption: (node.dataset.caption || '').slice(0, 500) })
    else if (tag === 'HR') blocks.push({ id: node.dataset.blockId || '', type: 'divider' })
    else if (tag === 'UL' || tag === 'OL') blocks.push(listBlock(node, tag === 'UL' ? 'bulletList' : 'orderedList'))
    else if (['H2', 'H3', 'BLOCKQUOTE', 'P', 'DIV'].includes(tag)) blocks.push(textBlock(node, tag === 'H2' ? 'h2' : tag === 'H3' ? 'h3' : tag === 'BLOCKQUOTE' ? 'quote' : 'paragraph'))
    if (blocks.length > blockIndex) elements.push({ element: node, blockIndex })
  }
  const normalized = normalizeArticleBlockIds(blocks.slice(0, 200))
  for (const { element, blockIndex } of elements) {
    const block = normalized[blockIndex]
    if (block) element.dataset.blockId = block.id
  }
  return { format: 'l12-blocks', version: 1, blocks: normalized.length ? normalized : [newArticleTextBlock()] }
}

function currentBody() { return serializeArticleBody(articleDocument.value) }
function syncCanvas() { if (!rendering) { articleDocument.value = parseCanvas(); emit('update:modelValue', currentBody()) } }
function clearTimer() { if (historyTimer !== undefined) clearTimeout(historyTimer); historyTimer = undefined }
function pushHistory(value = currentBody()) {
  if (history.value[historyIndex.value] === value) return
  history.value = [...history.value.slice(0, historyIndex.value + 1), value].slice(-100); historyIndex.value = history.value.length - 1
}
function flushHistory() { clearTimer(); if (historyPending.value) { syncCanvas(); historyPending.value = false; pushHistory() } }
function scheduleHistory() { syncCanvas(); historyPending.value = true; clearTimer(); historyTimer = setTimeout(flushHistory, HISTORY_DEBOUNCE_MS) }
function restoreHistory(index: number) {
  if (index < 0 || index >= history.value.length) return
  clearTimer(); historyPending.value = false; historyIndex.value = index; articleDocument.value = parseArticleBody(history.value[index]); emit('update:modelValue', currentBody()); nextTick(renderCanvas)
}
function undo() { flushHistory(); restoreHistory(historyIndex.value - 1) }
function redo() { flushHistory(); restoreHistory(historyIndex.value + 1) }

function insideCanvas(selection = window.getSelection()) { return Boolean(selection?.rangeCount && canvas.value?.contains(selection.anchorNode)) }
function saveSelection() { const selection = window.getSelection(); if (insideCanvas(selection)) savedRange = selection!.getRangeAt(0).cloneRange() }
function restoreSelection() {
  if (!savedRange || !canvas.value?.contains(savedRange.commonAncestorContainer)) return false
  const selection = window.getSelection(); selection?.removeAllRanges(); selection?.addRange(savedRange); return true
}
function command(name: string, value?: string) { canvas.value?.focus(); restoreSelection(); window.document.execCommand(name, false, value); saveSelection(); scheduleHistory() }
function setBlockStyle(event: Event) { command('formatBlock', (event.target as HTMLSelectElement).value); (event.target as HTMLSelectElement).value = 'p' }
function setAlignment(value: ArticleTextAlign) {
  command(({ left: 'justifyLeft', center: 'justifyCenter', right: 'justifyRight', justify: 'justifyFull' } as const)[value])
  const selection = window.getSelection(); let element = selection?.anchorNode instanceof HTMLElement ? selection.anchorNode : selection?.anchorNode?.parentElement
  while (element && element.parentElement !== canvas.value) element = element.parentElement
  if (element) { element.dataset.align = value; element.style.textAlign = value; scheduleHistory() }
}
function insertBlockAfterCursor(html: string) {
  const editor = canvas.value
  if (!editor) return
  let current: Node | null | undefined = savedRange?.startContainer
  if (current?.nodeType === Node.TEXT_NODE) current = current.parentNode
  while (current?.parentNode && current.parentNode !== editor) current = current.parentNode
  if (current instanceof HTMLElement && current.parentNode === editor) current.insertAdjacentHTML('afterend', html)
  else editor.insertAdjacentHTML('beforeend', html)
  scheduleHistory()
}
function insertDivider() { insertBlockAfterCursor(`<hr data-block-id="${articleBlockId()}">`) }
function pastePlainText(event: ClipboardEvent) { event.preventDefault(); command('insertText', event.clipboardData?.getData('text/plain') || '') }
function handleKeyboard(event: KeyboardEvent) {
  if (!(event.ctrlKey || event.metaKey)) return
  const key = event.key.toLowerCase()
  if (key === 'z') { event.preventDefault(); event.shiftKey ? redo() : undo() }
  else if (key === 'y') { event.preventDefault(); redo() }
  else if (key === 'k') { event.preventDefault(); openLink() }
}
function openLink() {
  saveSelection(); const selection = window.getSelection()
  if (!insideCanvas(selection) || selection?.isCollapsed) { emit('notice', '请先在正文中选择需要添加链接的文字'); return }
  const anchor = selection?.anchorNode instanceof HTMLElement ? selection.anchorNode : selection?.anchorNode?.parentElement
  linkHref.value = anchor?.closest('a')?.getAttribute('href') || ''; linkError.value = ''; linkPanel.value = true
}
function applyLink() { const href = safeArticleHref(linkHref.value); if (!href) { linkError.value = '请输入站内 /path 或 http(s) 地址'; return }; command('createLink', href); linkPanel.value = false }
function removeLink() { command('unlink'); linkPanel.value = false }

function openImage(event?: MouseEvent) {
  saveSelection(); editingFigure.value = null; imageAssetId.value = ''; imageAlt.value = ''; imageCaption.value = ''
  const figure = event?.target instanceof Element ? event.target.closest('figure') as HTMLElement | null : null
  if (figure) { editingFigure.value = figure; imageAssetId.value = figure.dataset.mediaId || ''; imageAlt.value = figure.dataset.alt || ''; imageCaption.value = figure.dataset.caption || '' }
  imagePanel.value = true
}
function figureHtml(media: SiteMedia, alt: string, caption: string, id: string = articleBlockId()) {
  return `<figure data-block-id="${escapeHtml(id)}" data-media-id="${escapeHtml(media.id)}" data-alt="${escapeHtml(alt)}" data-caption="${escapeHtml(caption)}" contenteditable="false"><img src="${escapeHtml(media.desktopUrl)}" alt="${escapeHtml(alt || media.altText)}">${caption ? `<figcaption>${escapeHtml(caption)}</figcaption>` : ''}<button type="button" data-edit-image="true" tabindex="-1">编辑图片</button></figure>`
}
function saveImage() {
  const media = mediaFor(imageAssetId.value); if (!media) { emit('notice', '请先选择或上传一张正文图片'); return }
  const alt = (imageAlt.value.trim() || media.altText).slice(0, 180); if (!alt) { emit('notice', '请填写图片替代文字'); return }
  const caption = imageCaption.value.trim().slice(0, 500)
  if (editingFigure.value?.isConnected) editingFigure.value.outerHTML = figureHtml(media, alt, caption, editingFigure.value.dataset.blockId || articleBlockId())
  else insertBlockAfterCursor(`${figureHtml(media, alt, caption)}<p data-block-id="${articleBlockId()}" data-align="left"><br></p>`)
  imagePanel.value = false; editingFigure.value = null; scheduleHistory()
}
function removeImage() { if (editingFigure.value?.isConnected) editingFigure.value.remove(); imagePanel.value = false; editingFigure.value = null; scheduleHistory() }
function uploaded(media: SiteMedia) { recentMedia.value = [media, ...recentMedia.value.filter(item => item.id !== media.id)]; imageAssetId.value = media.id; if (!imageAlt.value.trim()) imageAlt.value = media.altText; emit('media-uploaded', media) }
function canvasClick(event: MouseEvent) { if (event.target instanceof Element && event.target.closest('figure')) openImage(event); else saveSelection() }

watch(() => props.modelValue, value => {
  if ((value || '') === currentBody()) return
  clearTimer(); historyPending.value = false; articleDocument.value = parseArticleBody(value); history.value = [currentBody()]; historyIndex.value = 0; savedRange = null; nextTick(renderCanvas)
})
watch(preview, value => { if (value) flushHistory(); else nextTick(renderCanvas) })
watch(canvas, () => nextTick(renderCanvas), { immediate: true })
onBeforeUnmount(clearTimer)
</script>

<template>
  <section class="document-editor" @keydown="handleKeyboard">
    <header class="document-editor-head"><div><b>文章正文</b><span>标题、正文、格式和插图都在下方同一张稿纸中编辑。</span></div><nav><button :disabled="!canUndo" @mousedown.prevent @click="undo">↶ 撤销</button><button :disabled="!canRedo" @mousedown.prevent @click="redo">↷ 重做</button><button :class="{ active: preview }" @click="preview = !preview">{{ preview ? '返回编辑' : '预览文章' }}</button></nav></header>
    <div v-if="!preview" class="rich-toolbar" role="toolbar" aria-label="文章排版工具栏">
      <select aria-label="标题与正文样式" @mousedown="saveSelection" @change="setBlockStyle"><option value="p">正文</option><option value="h2">大标题</option><option value="h3">小标题</option><option value="blockquote">引用</option></select>
      <span><button title="加粗" @mousedown.prevent @click="command('bold')"><strong>B</strong></button><button title="斜体" @mousedown.prevent @click="command('italic')"><em>I</em></button><button title="下划线" @mousedown.prevent @click="command('underline')"><u>U</u></button><button title="删除线" @mousedown.prevent @click="command('strikeThrough')"><s>S</s></button></span>
      <span><button title="左对齐" @mousedown.prevent @click="setAlignment('left')">≡</button><button title="居中" @mousedown.prevent @click="setAlignment('center')">≣</button><button title="右对齐" class="align-right" @mousedown.prevent @click="setAlignment('right')">≡</button><button title="两端对齐" @mousedown.prevent @click="setAlignment('justify')">☷</button></span>
      <span><button @mousedown.prevent @click="command('insertUnorderedList')">• 列表</button><button @mousedown.prevent @click="command('insertOrderedList')">1. 列表</button><button @mousedown.prevent @click="insertDivider">— 分隔线</button></span>
      <span><button @mousedown.prevent @click="openLink">🔗 链接</button><button class="image-tool" @mousedown.prevent @click="openImage()">＋ 图片</button></span>
    </div>
    <ArticleContentRenderer v-if="preview" class="editor-preview" :body="currentBody()" :media="articleMedia"/>
    <div v-else class="canvas-wrap"><div ref="canvas" class="editor-canvas" contenteditable="true" spellcheck="true" data-placeholder="从这里开始撰写文章……" @input="scheduleHistory" @paste="pastePlainText" @mouseup="saveSelection" @keyup="saveSelection" @click="canvasClick" @blur="flushHistory"/><footer><span>{{ characterCount }} 字 · {{ imageCount }} 张图</span><span>正文图片不限尺寸与长宽比；点击图片可再次编辑</span></footer></div>
    <div v-if="linkPanel" class="dialog-mask" @mousedown.self="linkPanel = false"><section class="editor-dialog"><header><b>添加文字链接</b><button @click="linkPanel = false">×</button></header><label>链接地址<input v-model="linkHref" autofocus placeholder="/news/文章ID 或 https://example.com" @input="linkError = ''" @keydown.enter.prevent="applyLink"></label><small>仅允许站内路径或 http(s) 地址。</small><p v-if="linkError">{{ linkError }}</p><footer><button class="primary" @click="applyLink">应用链接</button><button @click="removeLink">移除链接</button><button @click="linkPanel = false">取消</button></footer></section></div>
    <div v-if="imagePanel" class="dialog-mask" @mousedown.self="imagePanel = false"><section class="editor-dialog image-dialog"><header><b>{{ editingFigure ? '编辑正文图片' : '插入正文图片' }}</b><button @click="imagePanel = false">×</button></header><label>已上传图片<select v-model="imageAssetId"><option value="">请选择</option><option v-for="item in articleMedia" :key="item.id" :value="item.id">{{ item.altText || item.contentHash.slice(0, 12) }}</option></select></label><img v-if="mediaFor(imageAssetId)" :src="mediaFor(imageAssetId)?.thumbnailUrl" :alt="imageAlt || mediaFor(imageAssetId)?.altText"><label>替代文字<input v-model="imageAlt" maxlength="180" placeholder="描述图片中的关键信息"></label><label>图片说明（可空）<input v-model="imageCaption" maxlength="500" placeholder="显示在图片下方"></label><details><summary>上传新图片</summary><MediaUploadField kind="article" :initial-alt="imageAlt" @uploaded="uploaded" @notice="emit('notice', $event)"/></details><footer><button class="primary" @click="saveImage">{{ editingFigure ? '保存图片' : '插入到光标处' }}</button><button v-if="editingFigure" class="danger" @click="removeImage">删除图片</button><button @click="imagePanel = false">取消</button></footer></section></div>
  </section>
</template>

<style scoped>
.document-editor{grid-column:1/-1;border:1px solid #46545d;background:#0b1218;color:#d6dcde}.document-editor-head{display:flex;align-items:center;justify-content:space-between;gap:20px;padding:18px 22px;border-bottom:1px solid #344149;background:#111b22}.document-editor-head>div{display:grid;gap:5px}.document-editor-head b{font-size:17px}.document-editor-head span{color:#9ca7ab;font-size:13px}.document-editor nav,.rich-toolbar,.rich-toolbar>span,.editor-dialog footer{display:flex;align-items:center;flex-wrap:wrap;gap:8px}.document-editor button,.document-editor select,.document-editor input{box-sizing:border-box;min-height:38px;padding:8px 11px;border:1px solid #526069;background:#070d12;color:#f2f4f3;font:inherit;font-size:13px}.document-editor button{font-weight:800}.document-editor button:hover:not(:disabled),.document-editor button.active{border-color:#d3b65f;color:#efd37a}.document-editor button:disabled{opacity:.38}.rich-toolbar{position:sticky;top:0;z-index:4;padding:10px 16px;border-bottom:1px solid #3a474e;background:#152028;box-shadow:0 6px 18px rgba(0,0,0,.2)}.rich-toolbar select{min-width:120px}.rich-toolbar>span{gap:4px;padding-left:8px;border-left:1px solid #45525a}.rich-toolbar button{min-width:40px}.rich-toolbar .image-tool{border-color:#36838a;color:#72d4da}.rich-toolbar .align-right{transform:scaleX(-1)}.canvas-wrap{padding:24px;background:#080d11}.editor-canvas{box-sizing:border-box;min-height:650px;width:min(860px,100%);margin:auto;padding:clamp(34px,6vw,76px);outline:0;background:#f4f0e5;color:#202628;font-family:'Microsoft YaHei','微软雅黑',sans-serif;font-size:16px;line-height:1.9;box-shadow:0 20px 60px rgba(0,0,0,.34);caret-color:#9b2632}.editor-canvas:empty:before{content:attr(data-placeholder);color:#9aa0a0}.editor-canvas :deep(p){margin:1.1em 0;white-space:pre-wrap}.editor-canvas :deep(h2){margin:1.8em 0 .7em;font-size:1.75em;line-height:1.35}.editor-canvas :deep(h3){margin:1.5em 0 .6em;font-size:1.35em}.editor-canvas :deep(blockquote){margin:1.35em 0;padding:.8em 1.2em;border-left:4px solid #b8963e;background:#e7e0cb}.editor-canvas :deep(ul),.editor-canvas :deep(ol){padding-left:1.8em}.editor-canvas :deep(a){color:#147d89;text-decoration:underline}.editor-canvas :deep(hr){margin:2em 0;border:0;border-top:1px solid #aaa28c}.editor-canvas :deep(figure){position:relative;margin:1.8em 0;padding:10px;border:1px solid transparent;cursor:pointer}.editor-canvas :deep(figure:hover){border-color:#c19f49;background:#ece5d2}.editor-canvas :deep(figure img){display:block;max-width:100%;height:auto;margin:auto}.editor-canvas :deep(figcaption){margin-top:9px;color:#747b7c;font-size:13px;text-align:center}.editor-canvas :deep([data-edit-image]){position:absolute;right:18px;top:18px;border-color:#d3b65f;background:rgba(7,13,18,.85);color:#efd37a}.canvas-wrap>footer{display:flex;justify-content:space-between;gap:16px;width:min(860px,100%);margin:12px auto 0;color:#8f9ba0;font-size:12px}.editor-preview{margin:24px auto;padding:clamp(32px,6vw,72px);width:min(860px,calc(100% - 48px));background:#f4f0e5;color:#202628;box-shadow:0 20px 60px rgba(0,0,0,.34)}.dialog-mask{position:fixed;z-index:1200;inset:0;display:grid;place-items:center;padding:24px;background:rgba(0,0,0,.72)}.editor-dialog{display:grid;gap:15px;width:min(560px,100%);max-height:calc(100vh - 48px);overflow:auto;padding:22px;border:1px solid #6d7a81;background:#111b22;box-shadow:0 28px 80px rgba(0,0,0,.55)}.editor-dialog>header{display:flex;align-items:center;justify-content:space-between}.editor-dialog>header b{font-size:18px}.editor-dialog label{display:grid;gap:7px;color:#c8d0d2;font-size:13px;font-weight:900}.editor-dialog input,.editor-dialog select{width:100%;min-height:44px}.editor-dialog small{color:#94a0a5}.editor-dialog>p{margin:0;color:#f2a0aa}.editor-dialog .primary{border-color:#b99b45;color:#efd47f}.editor-dialog .danger{border-color:#82414a;color:#ef929e}.image-dialog{width:min(780px,100%)}.image-dialog>img{display:block;max-width:100%;max-height:300px;margin:auto;object-fit:contain}.image-dialog details{border:1px solid #3b4950}.image-dialog summary{cursor:pointer;padding:12px;color:#70d1d7;font-weight:900}.image-dialog details>:not(summary){margin:12px}.image-dialog :deep(.media-upload-field){grid-template-columns:140px minmax(0,1fr)}.image-dialog :deep(.media-upload-field>img){width:140px}.editor-dialog footer{justify-content:flex-end}@media(max-width:760px){.document-editor-head{align-items:flex-start;flex-direction:column}.rich-toolbar{position:static}.canvas-wrap{padding:10px}.editor-canvas{min-height:500px;padding:24px 18px}.canvas-wrap>footer{align-items:flex-start;flex-direction:column}.editor-preview{width:calc(100% - 20px);margin:10px;padding:22px 18px}.rich-toolbar>span{padding-left:4px}.dialog-mask{padding:10px}.image-dialog :deep(.media-upload-field){grid-template-columns:1fr}}
</style>
