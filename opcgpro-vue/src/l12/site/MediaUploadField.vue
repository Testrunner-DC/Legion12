<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { adminApi, type SiteMedia, type SiteMediaKind, type SiteMediaPolicy } from '@/l12/platform'

const props = withDefaults(defineProps<{
  kind: SiteMediaKind
  modelValue?: string
  previewUrl?: string
  initialAlt?: string
}>(), { modelValue: '', previewUrl: '', initialAlt: '' })
const emit = defineEmits<{
  'update:modelValue': [value: string]
  uploaded: [value: SiteMedia]
  notice: [value: string]
}>()

const file = ref<File | null>(null)
const altText = ref(props.initialAlt)
const focalX = ref(50)
const focalY = ref(50)
const busy = ref(false)
const policies = ref<SiteMediaPolicy[]>([])
const policy = computed(() => policies.value.find(item => item.kind === props.kind))

onMounted(async () => {
  try { policies.value = await adminApi.siteMediaPolicies() }
  catch (error) { emit('notice', error instanceof Error ? error.message : '素材规格读取失败') }
})

function choose(event: Event) {
  const selected = (event.target as HTMLInputElement).files?.[0] || null
  file.value = selected
  if (selected && !altText.value.trim()) altText.value = selected.name.replace(/\.[^.]+$/, '')
}

function canvasBlob(canvas: HTMLCanvasElement, type: string, quality: number) {
  return new Promise<Blob>((resolve, reject) => canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('浏览器无法生成 WebP')), type, quality))
}

async function renderVariant(bitmap: ImageBitmap, width: number, height: number, quality: number) {
  const canvas = document.createElement('canvas')
  canvas.width = width; canvas.height = height
  const context = canvas.getContext('2d', { alpha: false })
  if (!context) throw new Error('浏览器图片处理不可用')
  const targetRatio = width / height
  const sourceRatio = bitmap.width / bitmap.height
  let sourceWidth = bitmap.width; let sourceHeight = bitmap.height
  if (sourceRatio > targetRatio) sourceWidth = sourceHeight * targetRatio
  else sourceHeight = sourceWidth / targetRatio
  const sourceX = Math.max(0, Math.min(bitmap.width - sourceWidth, (bitmap.width - sourceWidth) * focalX.value / 100))
  const sourceY = Math.max(0, Math.min(bitmap.height - sourceHeight, (bitmap.height - sourceHeight) * focalY.value / 100))
  context.imageSmoothingEnabled = true
  context.imageSmoothingQuality = 'high'
  context.drawImage(bitmap, sourceX, sourceY, sourceWidth, sourceHeight, 0, 0, width, height)
  return canvasBlob(canvas, 'image/webp', quality)
}

async function upload() {
  if (!file.value || !policy.value) { emit('notice', '请选择图片并等待规格读取完成'); return }
  if (!['image/jpeg', 'image/png', 'image/webp', 'image/avif'].includes(file.value.type)) {
    emit('notice', '只允许 JPEG、PNG、WebP 或 AVIF；SVG 不会被接受')
    return
  }
  if (file.value.size > 16 * 1024 * 1024) { emit('notice', '原图不能超过 16MB'); return }
  busy.value = true
  let bitmap: ImageBitmap | null = null
  try {
    bitmap = await createImageBitmap(file.value)
    const current = policy.value
    const [desktop, mobile, thumbnail] = await Promise.all([
      renderVariant(bitmap, current.desktopWidth, current.desktopHeight, .87),
      renderVariant(bitmap, current.mobileWidth, current.mobileHeight, .86),
      renderVariant(bitmap, current.thumbnailWidth, current.thumbnailHeight, .8),
    ])
    const form = new FormData()
    form.append('kind', props.kind)
    form.append('altText', altText.value.trim())
    form.append('focalX', String(focalX.value / 100))
    form.append('focalY', String(focalY.value / 100))
    form.append('original', file.value, file.value.name)
    form.append('desktop', desktop, 'desktop.webp')
    form.append('mobile', mobile, 'mobile.webp')
    form.append('thumbnail', thumbnail, 'thumbnail.webp')
    const media = await adminApi.uploadSiteMedia(form)
    emit('update:modelValue', media.id)
    emit('uploaded', media)
    emit('notice', `素材已上传：${media.contentHash.slice(0, 12)}…；原图仅归档，公开端使用去元数据 WebP`)
    file.value = null
  } catch (error) {
    emit('notice', error instanceof Error ? error.message : '素材上传失败')
  } finally {
    bitmap?.close()
    busy.value = false
  }
}
</script>

<template>
  <div class="media-upload-field">
    <img v-if="previewUrl" :src="previewUrl" :alt="altText || '当前素材预览'">
    <div class="media-upload-copy">
      <label>上传原图<input type="file" accept="image/jpeg,image/png,image/webp,image/avif" @change="choose"></label>
      <label>替代文字<input v-model="altText" maxlength="180" placeholder="描述图片内容，供无障碍与图片异常时使用"></label>
      <div class="focal-controls">
        <label>水平焦点 {{ focalX }}%<input v-model.number="focalX" type="range" min="0" max="100"></label>
        <label>垂直焦点 {{ focalY }}%<input v-model.number="focalY" type="range" min="0" max="100"></label>
      </div>
      <p v-if="policy"><b>{{ policy.label }}</b>：桌面 {{ policy.desktopWidth }}×{{ policy.desktopHeight }}，移动 {{ policy.mobileWidth }}×{{ policy.mobileHeight }}，缩略图 {{ policy.thumbnailWidth }}×{{ policy.thumbnailHeight }}。{{ policy.safeArea }}</p>
      <p>接受 JPEG / PNG / WebP / AVIF 原图；浏览器按焦点裁切，服务端复核真实签名和精确像素，按内容哈希保存。</p>
      <button :disabled="busy || !file || !policy" @click="upload">{{ busy ? '正在生成并上传…' : '生成 WebP 三规格并上传' }}</button>
    </div>
  </div>
</template>

<style scoped>
.media-upload-field{display:grid;grid-template-columns:150px minmax(0,1fr);gap:14px;padding:13px;border:1px solid #34434b;background:#091016}.media-upload-field>img{width:150px;height:110px;object-fit:cover;border:1px solid #52616a}.media-upload-copy{display:grid;gap:9px}.media-upload-copy label{display:grid;gap:5px;color:#aeb8bb;font-size:9px;font-weight:900}.media-upload-copy input{box-sizing:border-box;width:100%;padding:8px;border:1px solid #46545d;background:#050a0e;color:#fff}.media-upload-copy input[type="range"]{padding:0}.focal-controls{display:grid;grid-template-columns:1fr 1fr;gap:10px}.media-upload-copy p{margin:0;color:#78868d;font-size:9px;line-height:1.65}.media-upload-copy p b{color:#d9bd69}.media-upload-copy button{justify-self:start;padding:9px 13px;border:1px solid #b99b45;background:#2c240e;color:#f0d477;font-weight:900}.media-upload-copy button:disabled{opacity:.45}@media(max-width:720px){.media-upload-field{grid-template-columns:1fr}.media-upload-field>img{width:100%;height:180px}.focal-controls{grid-template-columns:1fr}}
</style>
