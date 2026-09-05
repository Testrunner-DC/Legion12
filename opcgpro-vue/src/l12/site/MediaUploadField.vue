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
const ORIGINAL_MAX_BYTES = 16 * 1024 * 1024
const DESKTOP_MAX_BYTES = 5 * 1024 * 1024
const MOBILE_MAX_BYTES = 5 * 1024 * 1024
const THUMBNAIL_MAX_BYTES = 2 * 1024 * 1024
const REQUEST_MAX_BYTES = 32 * 1024 * 1024

function greatestCommonDivisor(left: number, right: number): number {
  return right ? greatestCommonDivisor(right, left % right) : left
}
function ratio(width: number, height: number) {
  const divisor = greatestCommonDivisor(width, height)
  return `${width / divisor}:${height / divisor}`
}
const minimumWidth = computed(() => policy.value ? Math.max(policy.value.desktopWidth, policy.value.mobileWidth) : 0)
const minimumHeight = computed(() => policy.value ? Math.max(policy.value.desktopHeight, policy.value.mobileHeight) : 0)
const desktopRatio = computed(() => policy.value ? ratio(policy.value.desktopWidth, policy.value.desktopHeight) : '')
const mobileRatio = computed(() => policy.value ? ratio(policy.value.mobileWidth, policy.value.mobileHeight) : '')

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
  if (file.value.size > ORIGINAL_MAX_BYTES) { emit('notice', '原图不能超过 16MB'); return }
  busy.value = true
  let bitmap: ImageBitmap | null = null
  try {
    bitmap = await createImageBitmap(file.value)
    const current = policy.value
    if (bitmap.width < minimumWidth.value || bitmap.height < minimumHeight.value) {
      throw new Error(`原图至少建议 ${minimumWidth.value}×${minimumHeight.value}px；当前仅 ${bitmap.width}×${bitmap.height}px，继续放大会明显失真`)
    }
    const [desktop, mobile, thumbnail] = await Promise.all([
      renderVariant(bitmap, current.desktopWidth, current.desktopHeight, .87),
      renderVariant(bitmap, current.mobileWidth, current.mobileHeight, .86),
      renderVariant(bitmap, current.thumbnailWidth, current.thumbnailHeight, .8),
    ])
    if (desktop.size > DESKTOP_MAX_BYTES || mobile.size > MOBILE_MAX_BYTES || thumbnail.size > THUMBNAIL_MAX_BYTES) {
      throw new Error('浏览器生成的 WebP 超出单规格限制（桌面/移动 5MB，缩略图 2MB），请先压缩原图')
    }
    const estimatedRequestBytes = file.value.size + desktop.size + mobile.size + thumbnail.size + 64 * 1024
    if (estimatedRequestBytes > REQUEST_MAX_BYTES) {
      throw new Error('图片上传总量超过 32MB，请压缩原图后重试')
    }
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
      <aside v-if="policy" class="media-spec" aria-label="图片上传尺寸参考">
        <b>{{ policy.label }} · 上传尺寸参考</b>
        <span>用途：{{ policy.label }}；桌面推荐 {{ desktopRatio }}（{{ policy.desktopWidth }}×{{ policy.desktopHeight }}px），移动推荐 {{ mobileRatio }}（{{ policy.mobileWidth }}×{{ policy.mobileHeight }}px）。</span>
        <span>缩略图：{{ policy.thumbnailWidth }}×{{ policy.thumbnailHeight }}px；原图最低建议 {{ minimumWidth }}×{{ minimumHeight }}px。</span>
        <span>安全区与裁切：{{ policy.safeArea }}。系统会依据下方焦点分别居中裁切，不会拉伸。</span>
      </aside>
      <p>接受 JPEG / PNG / WebP / AVIF；原图 ≤16MB，整次请求 ≤32MB。服务端复核真实签名、精确像素与权限，按内容哈希保存，SVG/HTML 会被拒绝。</p>
      <button :disabled="busy || !file || !policy" @click="upload">{{ busy ? '正在生成并上传…' : '生成 WebP 三规格并上传' }}</button>
    </div>
  </div>
</template>

<style scoped>
.media-upload-field{display:grid;grid-template-columns:190px minmax(0,1fr);gap:22px;padding:20px;border:1px solid #34434b;background:#091016}.media-upload-field>img{width:190px;height:142px;object-fit:cover;border:1px solid #52616a}.media-upload-copy{display:grid;gap:14px}.media-upload-copy label{display:grid;gap:7px;color:#c0c8ca;font-size:14px;font-weight:800;line-height:1.45}.media-upload-copy input{box-sizing:border-box;width:100%;min-height:42px;padding:9px 11px;border:1px solid #46545d;background:#050a0e;color:#fff;font-size:14px}.media-upload-copy input[type="range"]{min-height:28px;padding:0}.focal-controls{display:grid;grid-template-columns:1fr 1fr;gap:18px}.media-upload-copy p{margin:0;color:#96a2a7;font-size:12px;line-height:1.7}.media-spec{display:grid;gap:6px;padding:13px 15px;border-left:3px solid #d9bd69;background:#141a1d;color:#aeb8bb;font-size:12px;line-height:1.6}.media-spec b{color:#ead07a;font-size:14px}.media-upload-copy button{justify-self:start;min-height:42px;padding:10px 16px;border:1px solid #b99b45;background:#2c240e;color:#f0d477;font-size:14px;font-weight:900}.media-upload-copy button:disabled{opacity:.45}@media(max-width:720px){.media-upload-field{grid-template-columns:1fr;padding:16px}.media-upload-field>img{width:100%;height:180px}.focal-controls{grid-template-columns:1fr}}
</style>
