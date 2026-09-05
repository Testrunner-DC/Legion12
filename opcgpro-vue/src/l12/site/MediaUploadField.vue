<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
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

type HeroVariantKey = 'desktop' | 'mobile' | 'thumbnail'
interface HeroVariantSpec { key: HeroVariantKey; label: string; width: number; height: number; outputMaxBytes: number }

const file = ref<File | null>(null)
const altText = ref(props.initialAlt)
const focalX = ref(50)
const focalY = ref(50)
const busy = ref(false)
const policies = ref<SiteMediaPolicy[]>([])
const heroFiles = reactive<Record<HeroVariantKey, File | null>>({ desktop: null, mobile: null, thumbnail: null })
const heroAltTexts = reactive<Record<HeroVariantKey, string>>({
  desktop: props.initialAlt, mobile: props.initialAlt, thumbnail: props.initialAlt,
})
const heroPreviews = reactive<Record<HeroVariantKey, string>>({ desktop: '', mobile: '', thumbnail: '' })
const heroStatuses = reactive<Record<HeroVariantKey, string>>({ desktop: '尚未选择', mobile: '尚未选择', thumbnail: '尚未选择' })
const heroInputVersion = ref(0)
const policy = computed(() => policies.value.find(item => item.kind === props.kind))
const isHero = computed(() => props.kind === 'hero')
const isFlexible = computed(() => Boolean(policy.value?.flexibleDimensions))
const ORIGINAL_MAX_BYTES = 16 * 1024 * 1024
const DESKTOP_MAX_BYTES = 5 * 1024 * 1024
const MOBILE_MAX_BYTES = 5 * 1024 * 1024
const THUMBNAIL_MAX_BYTES = 2 * 1024 * 1024
const REQUEST_MAX_BYTES = 32 * 1024 * 1024
const acceptedTypes = ['image/jpeg', 'image/png', 'image/webp', 'image/avif']
const heroKeys: HeroVariantKey[] = ['desktop', 'mobile', 'thumbnail']

const heroSpecs = computed<HeroVariantSpec[]>(() => policy.value ? [
  { key: 'desktop', label: '桌面版', width: policy.value.desktopWidth, height: policy.value.desktopHeight, outputMaxBytes: DESKTOP_MAX_BYTES },
  { key: 'mobile', label: '移动版', width: policy.value.mobileWidth, height: policy.value.mobileHeight, outputMaxBytes: MOBILE_MAX_BYTES },
  { key: 'thumbnail', label: '缩略预览版', width: policy.value.thumbnailWidth, height: policy.value.thumbnailHeight, outputMaxBytes: THUMBNAIL_MAX_BYTES },
] : [])
const heroReady = computed(() => heroSpecs.value.length === 3 && heroSpecs.value.every(spec =>
  heroFiles[spec.key] && heroAltTexts[spec.key].trim() && heroStatuses[spec.key].startsWith('已通过')))

function greatestCommonDivisor(left: number, right: number): number {
  return right ? greatestCommonDivisor(right, left % right) : left
}
function ratio(width: number, height: number) {
  if (width <= 0 || height <= 0) return '不限比例'
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
onBeforeUnmount(() => heroKeys.forEach(revokeHeroPreview))
watch(() => props.initialAlt, value => {
  if (!altText.value.trim()) altText.value = value
  heroKeys.forEach(key => { if (!heroAltTexts[key].trim()) heroAltTexts[key] = value })
})

function assertAcceptedSource(selected: File) {
  if (!acceptedTypes.includes(selected.type)) throw new Error('只允许 JPEG、PNG、WebP 或 AVIF；SVG 不会被接受')
  if (selected.size > ORIGINAL_MAX_BYTES) throw new Error('单份原图不能超过 16MB')
}
function choose(event: Event) {
  const selected = (event.target as HTMLInputElement).files?.[0] || null
  file.value = selected
  if (selected && !altText.value.trim()) altText.value = selected.name.replace(/\.[^.]+$/, '')
}
function revokeHeroPreview(key: HeroVariantKey) {
  if (heroPreviews[key]) URL.revokeObjectURL(heroPreviews[key])
  heroPreviews[key] = ''
}
async function chooseHero(key: HeroVariantKey, event: Event) {
  const selected = (event.target as HTMLInputElement).files?.[0] || null
  revokeHeroPreview(key)
  heroFiles[key] = selected
  heroStatuses[key] = selected ? '正在检查格式与方向…' : '尚未选择'
  if (!selected) return
  heroPreviews[key] = URL.createObjectURL(selected)
  if (!heroAltTexts[key].trim()) heroAltTexts[key] = selected.name.replace(/\.[^.]+$/, '')
  let bitmap: ImageBitmap | null = null
  try {
    assertAcceptedSource(selected)
    bitmap = await createOrientedBitmap(selected)
    if (heroFiles[key] !== selected) return
    const spec = heroSpecs.value.find(item => item.key === key)
    if (!spec) throw new Error('轮播规格尚未加载')
    validateIndependentSource(bitmap, spec)
    heroStatuses[key] = `已通过 · 方向归一后 ${bitmap.width}×${bitmap.height}px`
  } catch (error) {
    if (heroFiles[key] === selected) heroStatuses[key] = `未通过 · ${error instanceof Error ? error.message : '图片检查失败'}`
  } finally {
    bitmap?.close()
  }
}

function canvasBlob(canvas: HTMLCanvasElement, type: string, quality: number) {
  return new Promise<Blob>((resolve, reject) => canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('浏览器无法生成 WebP')), type, quality))
}
function createOrientedBitmap(source: Blob) {
  return createImageBitmap(source, { imageOrientation: 'from-image' })
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
async function renderFlexibleVariant(bitmap: ImageBitmap, maxEdge: number, quality: number) {
  const scale = Math.min(1, maxEdge / Math.max(bitmap.width, bitmap.height))
  const width = Math.max(1, Math.round(bitmap.width * scale))
  const height = Math.max(1, Math.round(bitmap.height * scale))
  const canvas = document.createElement('canvas')
  canvas.width = width; canvas.height = height
  const context = canvas.getContext('2d', { alpha: false })
  if (!context) throw new Error('浏览器图片处理不可用')
  context.imageSmoothingEnabled = true
  context.imageSmoothingQuality = 'high'
  context.drawImage(bitmap, 0, 0, width, height)
  return canvasBlob(canvas, 'image/webp', quality)
}
function validateIndependentSource(bitmap: ImageBitmap, spec: HeroVariantSpec) {
  if (bitmap.width < spec.width || bitmap.height < spec.height) {
    throw new Error(`${spec.label}方向归一后至少 ${spec.width}×${spec.height}px`)
  }
  const ratioError = Math.abs(bitmap.width / bitmap.height - spec.width / spec.height) / (spec.width / spec.height)
  if (ratioError > .001) throw new Error(`${spec.label}必须使用 ${ratio(spec.width, spec.height)} 构图，不会自动裁切`)
}
async function renderIndependentVariant(bitmap: ImageBitmap, spec: HeroVariantSpec) {
  validateIndependentSource(bitmap, spec)
  const canvas = document.createElement('canvas')
  canvas.width = spec.width; canvas.height = spec.height
  const context = canvas.getContext('2d', { alpha: false })
  if (!context) throw new Error('浏览器图片处理不可用')
  context.imageSmoothingEnabled = true
  context.imageSmoothingQuality = 'high'
  context.drawImage(bitmap, 0, 0, spec.width, spec.height)
  return canvasBlob(canvas, 'image/webp', spec.key === 'thumbnail' ? .8 : .87)
}
function validateOutputSizes(desktop: Blob, mobile: Blob, thumbnail: Blob) {
  if (desktop.size > DESKTOP_MAX_BYTES || mobile.size > MOBILE_MAX_BYTES || thumbnail.size > THUMBNAIL_MAX_BYTES) {
    throw new Error('浏览器生成的 WebP 超出单规格限制（桌面/移动 5MB，缩略图 2MB），请先压缩原图')
  }
}
async function submitForm(form: FormData) {
  const media = await adminApi.uploadSiteMedia(form)
  emit('update:modelValue', media.id)
  emit('uploaded', media)
  emit('notice', `素材组已上传：${media.contentHash.slice(0, 12)}…；服务端已按 RIFF 区块剥离交付图元数据`)
  return media
}

async function upload() {
  if (!file.value || !policy.value) { emit('notice', '请选择图片并等待规格读取完成'); return }
  busy.value = true
  let bitmap: ImageBitmap | null = null
  try {
    assertAcceptedSource(file.value)
    bitmap = await createOrientedBitmap(file.value)
    const current = policy.value
    if (!current.flexibleDimensions && (bitmap.width < minimumWidth.value || bitmap.height < minimumHeight.value)) {
      throw new Error(`原图方向归一后至少建议 ${minimumWidth.value}×${minimumHeight.value}px；当前仅 ${bitmap.width}×${bitmap.height}px，继续放大会明显失真`)
    }
    const [desktop, mobile, thumbnail] = current.flexibleDimensions
      ? await Promise.all([
        renderFlexibleVariant(bitmap, 2400, .87), renderFlexibleVariant(bitmap, 1280, .86), renderFlexibleVariant(bitmap, 600, .8),
      ])
      : await Promise.all([
        renderVariant(bitmap, current.desktopWidth, current.desktopHeight, .87),
        renderVariant(bitmap, current.mobileWidth, current.mobileHeight, .86),
        renderVariant(bitmap, current.thumbnailWidth, current.thumbnailHeight, .8),
      ])
    validateOutputSizes(desktop, mobile, thumbnail)
    const estimatedRequestBytes = file.value.size + desktop.size + mobile.size + thumbnail.size + 64 * 1024
    if (estimatedRequestBytes > REQUEST_MAX_BYTES) throw new Error('图片上传总量超过 32MB，请压缩原图后重试')
    const normalizedAlt = altText.value.trim()
    const form = new FormData()
    form.append('kind', props.kind)
    form.append('altText', normalizedAlt)
    form.append('desktopAltText', normalizedAlt)
    form.append('mobileAltText', normalizedAlt)
    form.append('thumbnailAltText', normalizedAlt)
    form.append('independentVariants', 'false')
    form.append('focalX', String(focalX.value / 100))
    form.append('focalY', String(focalY.value / 100))
    form.append('original', file.value, file.value.name)
    form.append('desktop', desktop, 'desktop.webp')
    form.append('mobile', mobile, 'mobile.webp')
    form.append('thumbnail', thumbnail, 'thumbnail.webp')
    await submitForm(form)
    file.value = null
  } catch (error) {
    emit('notice', error instanceof Error ? error.message : '素材上传失败')
  } finally {
    bitmap?.close()
    busy.value = false
  }
}

async function uploadHeroGroup() {
  if (!policy.value || !heroReady.value) { emit('notice', '请先补齐并通过三个轮播版本及各自替代文字检查'); return }
  busy.value = true
  try {
    const blobs: Partial<Record<HeroVariantKey, Blob>> = {}
    for (const spec of heroSpecs.value) {
      const source = heroFiles[spec.key]
      if (!source) throw new Error(`${spec.label}尚未选择`)
      assertAcceptedSource(source)
      const bitmap = await createOrientedBitmap(source)
      try { blobs[spec.key] = await renderIndependentVariant(bitmap, spec) }
      finally { bitmap.close() }
    }
    const desktop = blobs.desktop!; const mobile = blobs.mobile!; const thumbnail = blobs.thumbnail!
    validateOutputSizes(desktop, mobile, thumbnail)
    const desktopSource = heroFiles.desktop!
    const estimatedRequestBytes = desktopSource.size + desktop.size + mobile.size + thumbnail.size + 96 * 1024
    if (estimatedRequestBytes > REQUEST_MAX_BYTES) throw new Error('轮播素材组上传总量超过 32MB，请压缩桌面原图后重试')
    const form = new FormData()
    form.append('kind', 'hero')
    form.append('altText', heroAltTexts.desktop.trim())
    form.append('desktopAltText', heroAltTexts.desktop.trim())
    form.append('mobileAltText', heroAltTexts.mobile.trim())
    form.append('thumbnailAltText', heroAltTexts.thumbnail.trim())
    form.append('independentVariants', 'true')
    form.append('focalX', '0.5')
    form.append('focalY', '0.5')
    form.append('original', desktopSource, desktopSource.name)
    form.append('desktop', desktop, 'desktop.webp')
    form.append('mobile', mobile, 'mobile.webp')
    form.append('thumbnail', thumbnail, 'thumbnail.webp')
    await submitForm(form)
    heroKeys.forEach(key => {
      revokeHeroPreview(key)
      heroFiles[key] = null
      heroStatuses[key] = '尚未选择'
      heroAltTexts[key] = props.initialAlt
    })
    heroInputVersion.value++
  } catch (error) {
    emit('notice', error instanceof Error ? error.message : '轮播素材组上传失败')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div v-if="isHero" class="hero-upload-field">
    <div v-if="previewUrl" class="current-hero-preview"><span>当前已绑定素材组</span><img :src="previewUrl" :alt="initialAlt || '当前轮播素材预览'"></div>
    <aside v-if="policy" class="media-spec" aria-label="轮播三版本上传尺寸参考">
      <b>{{ policy.label }} · 三版本同时上传</b>
      <span>桌面 {{ desktopRatio }}（{{ policy.desktopWidth }}×{{ policy.desktopHeight }}px）；移动 {{ mobileRatio }}（{{ policy.mobileWidth }}×{{ policy.mobileHeight }}px）；缩略 {{ ratio(policy.thumbnailWidth, policy.thumbnailHeight) }}（{{ policy.thumbnailWidth }}×{{ policy.thumbnailHeight }}px）。</span>
      <span>三个版本均为必填：缩略图承担后台预览与列表快速加载，缺少任一版本都不会提交素材组。</span>
      <span>每份原图可使用更高的同等比例尺寸；系统按 EXIF Orientation 纠正像素后仅缩放、不裁切，桌面与移动构图互不派生。</span>
      <span>安全区：{{ policy.safeArea }}。</span>
    </aside>
    <div class="hero-variant-grid">
      <section v-for="spec in heroSpecs" :key="spec.key" class="hero-variant">
        <header><b>{{ spec.label }}</b><span>{{ ratio(spec.width, spec.height) }} · {{ spec.width }}×{{ spec.height }}px</span></header>
        <div class="variant-preview" :style="{ aspectRatio: `${spec.width}/${spec.height}` }">
          <img v-if="heroPreviews[spec.key]" :src="heroPreviews[spec.key]" :alt="heroAltTexts[spec.key] || `${spec.label}待上传预览`">
          <span v-else>等待选择{{ spec.label }}</span>
        </div>
        <label>选择{{ spec.label }}原图<input :key="`${spec.key}-${heroInputVersion}`" type="file" accept="image/jpeg,image/png,image/webp,image/avif" @change="chooseHero(spec.key, $event)"></label>
        <label>{{ spec.label }}替代文字<input v-model="heroAltTexts[spec.key]" maxlength="180" placeholder="描述此版本画面内容"></label>
        <p :class="{ valid: heroStatuses[spec.key].startsWith('已通过'), invalid: heroStatuses[spec.key].startsWith('未通过') }">{{ heroStatuses[spec.key] }}</p>
      </section>
    </div>
    <p class="upload-security">接受 JPEG / PNG / WebP / AVIF，每份原图 ≤16MB。浏览器按方向归一并生成三份WebP；服务端按RIFF区块权威剥离 ICC、EXIF、XMP 后，再以一个内容哈希素材组原子保存。</p>
    <button class="upload-button" :disabled="busy || !heroReady" @click="uploadHeroGroup">{{ busy ? '正在生成并提交三版本…' : '生成并原子提交轮播素材组' }}</button>
  </div>

  <div v-else class="media-upload-field">
    <img v-if="previewUrl" :class="{ contain: isFlexible }" :src="previewUrl" :alt="altText || '当前素材预览'">
    <div class="media-upload-copy">
      <label>上传原图<input type="file" accept="image/jpeg,image/png,image/webp,image/avif" @change="choose"></label>
      <label>替代文字<input v-model="altText" maxlength="180" placeholder="描述图片内容，供无障碍与图片异常时使用"></label>
      <div v-if="!isFlexible" class="focal-controls">
        <label>水平焦点 {{ focalX }}%<input v-model.number="focalX" type="range" min="0" max="100"></label>
        <label>垂直焦点 {{ focalY }}%<input v-model.number="focalY" type="range" min="0" max="100"></label>
      </div>
      <aside v-if="policy" class="media-spec" aria-label="图片上传尺寸参考">
        <b>{{ policy.label }} · 上传尺寸参考</b>
        <template v-if="isFlexible">
          <span>正文插图不限制像素尺寸和长宽比，横图、竖图与长图都可直接上传。</span>
          <span>系统完整保留原图构图，仅等比例生成网页交付版本，不裁切、不拉伸；桌面最长边最多 2400px，移动 1280px，缩略图 600px。</span>
        </template>
        <template v-else>
          <span>用途：{{ policy.label }}；桌面推荐 {{ desktopRatio }}（{{ policy.desktopWidth }}×{{ policy.desktopHeight }}px），移动推荐 {{ mobileRatio }}（{{ policy.mobileWidth }}×{{ policy.mobileHeight }}px）。</span>
          <span>缩略图：{{ policy.thumbnailWidth }}×{{ policy.thumbnailHeight }}px；原图最低建议 {{ minimumWidth }}×{{ minimumHeight }}px。</span>
          <span>安全区与裁切：{{ policy.safeArea }}。系统按原图方向纠正像素后依据下方焦点分别裁切，不会拉伸。</span>
        </template>
      </aside>
      <p>接受 JPEG / PNG / WebP / AVIF；原图可保留常见 EXIF、ICC 与 Orientation，≤16MB，整次请求 ≤32MB。服务端复核真实签名、精确像素与权限，并权威剥离交付WebP元数据。</p>
      <button :disabled="busy || !file || !policy" @click="upload">{{ busy ? '正在生成并上传…' : isFlexible ? '生成等比例 WebP 并上传' : '生成 WebP 三规格并上传' }}</button>
    </div>
  </div>
</template>

<style scoped>
.media-upload-field{display:grid;grid-template-columns:190px minmax(0,1fr);gap:22px;padding:20px;border:1px solid #34434b;background:#091016}.media-upload-field>img{width:190px;height:142px;object-fit:cover;border:1px solid #52616a}.media-upload-copy{display:grid;gap:14px}.media-upload-copy label,.hero-upload-field label{display:grid;gap:7px;color:#c0c8ca;font-size:14px;font-weight:800;line-height:1.45}.media-upload-copy input,.hero-upload-field input{box-sizing:border-box;width:100%;min-height:42px;padding:9px 11px;border:1px solid #46545d;background:#050a0e;color:#fff;font-size:14px}.media-upload-copy input[type="range"]{min-height:28px;padding:0}.focal-controls{display:grid;grid-template-columns:1fr 1fr;gap:18px}.media-upload-copy p,.upload-security{margin:0;color:#96a2a7;font-size:12px;line-height:1.7}.media-spec{display:grid;gap:6px;padding:13px 15px;border-left:3px solid #d9bd69;background:#141a1d;color:#aeb8bb;font-size:12px;line-height:1.6}.media-spec b{color:#ead07a;font-size:14px}.media-upload-copy button,.upload-button{justify-self:start;min-height:42px;padding:10px 16px;border:1px solid #b99b45;background:#2c240e;color:#f0d477;font-size:14px;font-weight:900}.media-upload-copy button:disabled,.upload-button:disabled{opacity:.45}.hero-upload-field{display:grid;gap:18px;padding:20px;border:1px solid #34434b;background:#091016}.current-hero-preview{display:grid;grid-template-columns:180px minmax(0,1fr);align-items:center;gap:14px;padding:12px;border:1px solid #39474e}.current-hero-preview span{color:#b8c1c4;font-size:13px;font-weight:800}.current-hero-preview img{grid-column:1;width:180px;aspect-ratio:600/351;object-fit:cover}.hero-variant-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:16px}.hero-variant{display:grid;align-content:start;gap:12px;min-width:0;padding:15px;border:1px solid #3d4b53;background:#0e171e}.hero-variant header{display:flex;flex-wrap:wrap;align-items:baseline;justify-content:space-between;gap:8px}.hero-variant header b{color:#e9cf78;font-size:14px}.hero-variant header span{color:#9fa9ad;font-size:12px}.variant-preview{display:grid;place-items:center;overflow:hidden;width:100%;background:#05090c;color:#7f8c91;font-size:12px}.variant-preview img{display:block;width:100%;height:100%;object-fit:contain}.hero-variant p{margin:0;color:#98a4a9;font-size:12px;line-height:1.55}.hero-variant p.valid{color:#76d5a0}.hero-variant p.invalid{color:#f29aa4}@media(max-width:980px){.hero-variant-grid{grid-template-columns:1fr 1fr}.hero-variant:last-child{grid-column:1/-1}}@media(max-width:720px){.media-upload-field{grid-template-columns:1fr;padding:16px}.media-upload-field>img{width:100%;height:180px}.focal-controls,.hero-variant-grid{grid-template-columns:1fr}.hero-variant:last-child{grid-column:auto}.hero-upload-field{padding:16px}.current-hero-preview{grid-template-columns:1fr}.current-hero-preview img{grid-column:auto;width:100%}}
.media-upload-field>img.contain{object-fit:contain;background:#03070a}
</style>
