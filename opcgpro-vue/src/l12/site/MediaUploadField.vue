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
  heroFiles[spec.key] && heroAltTexts[spec.key].trim() && heroStatuses[spec.key].startsWith('已选择')))

function greatestCommonDivisor(left: number, right: number): number {
  return right ? greatestCommonDivisor(right, left % right) : left
}
function ratio(width: number, height: number) {
  if (width <= 0 || height <= 0) return '不限比例'
  const divisor = greatestCommonDivisor(width, height)
  return `${width / divisor}:${height / divisor}`
}
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
  heroStatuses[key] = selected ? '正在读取图片…' : '尚未选择'
  if (!selected) return
  heroPreviews[key] = URL.createObjectURL(selected)
  if (!heroAltTexts[key].trim()) heroAltTexts[key] = selected.name.replace(/\.[^.]+$/, '')
  let bitmap: ImageBitmap | null = null
  try {
    assertAcceptedSource(selected)
    bitmap = await createOrientedBitmap(selected)
    if (heroFiles[key] !== selected) return
    heroStatuses[key] = `已选择 · ${bitmap.width}×${bitmap.height}px`
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
async function renderIndependentVariant(bitmap: ImageBitmap, spec: HeroVariantSpec) {
  return renderFlexibleVariant(bitmap, Math.max(spec.width, spec.height), spec.key === 'thumbnail' ? .8 : .87)
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
  emit('notice', `素材已上传：${media.contentHash.slice(0, 12)}…`)
  return media
}

async function upload() {
  if (!file.value || !policy.value) { emit('notice', '请选择图片并等待规格读取完成'); return }
  busy.value = true
  let bitmap: ImageBitmap | null = null
  try {
    assertAcceptedSource(file.value)
    bitmap = await createOrientedBitmap(file.value)
    const [desktop, mobile, thumbnail] = await Promise.all([
      renderFlexibleVariant(bitmap, 2400, .87), renderFlexibleVariant(bitmap, 1280, .86), renderFlexibleVariant(bitmap, 600, .8),
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
  if (!policy.value || !heroReady.value) { emit('notice', '请选择三个轮播版本并填写各自替代文字'); return }
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
    <p v-if="policy" class="media-ratio-hint">建议比例：桌面 {{ desktopRatio }} · 移动 {{ mobileRatio }} · 缩略 {{ ratio(policy.thumbnailWidth, policy.thumbnailHeight) }}</p>
    <div class="hero-variant-grid">
      <section v-for="spec in heroSpecs" :key="spec.key" class="hero-variant">
        <header><b>{{ spec.label }}</b></header>
        <div class="variant-preview" :style="{ aspectRatio: `${spec.width}/${spec.height}` }">
          <img v-if="heroPreviews[spec.key]" :src="heroPreviews[spec.key]" :alt="heroAltTexts[spec.key] || `${spec.label}待上传预览`">
          <span v-else>等待选择{{ spec.label }}</span>
        </div>
        <label>选择{{ spec.label }}原图<input :key="`${spec.key}-${heroInputVersion}`" type="file" accept="image/jpeg,image/png,image/webp,image/avif" @change="chooseHero(spec.key, $event)"></label>
        <label>{{ spec.label }}替代文字<input v-model="heroAltTexts[spec.key]" maxlength="180" placeholder="描述此版本画面内容"></label>
        <p :class="{ valid: heroStatuses[spec.key].startsWith('已选择'), invalid: heroStatuses[spec.key].startsWith('未通过') }">{{ heroStatuses[spec.key] }}</p>
      </section>
    </div>
    <button class="upload-button" :disabled="busy || !heroReady" @click="uploadHeroGroup">{{ busy ? '正在生成并提交三版本…' : '生成并原子提交轮播素材组' }}</button>
  </div>

  <div v-else class="media-upload-field">
    <img v-if="previewUrl" :class="{ contain: isFlexible }" :src="previewUrl" :alt="altText || '当前素材预览'">
    <div class="media-upload-copy">
      <label>上传原图<input type="file" accept="image/jpeg,image/png,image/webp,image/avif" @change="choose"></label>
      <label>替代文字<input v-model="altText" maxlength="180" placeholder="描述图片内容，供无障碍与图片异常时使用"></label>
      <p v-if="policy" class="media-ratio-hint">建议比例 {{ desktopRatio || '不限' }}</p>
      <button :disabled="busy || !file || !policy" @click="upload">{{ busy ? '正在生成并上传…' : '上传图片' }}</button>
    </div>
  </div>
</template>

<style scoped>
.media-upload-field{display:grid;grid-template-columns:190px minmax(0,1fr);gap:22px;padding:20px;border:1px solid var(--l12-ui-line,#34434b);background:var(--l12-ui-panel,#091016)}.media-upload-field>img{width:190px;height:142px;object-fit:cover;border:1px solid var(--l12-ui-line-strong,#52616a)}.media-upload-copy{display:grid;gap:14px}.media-upload-copy label,.hero-upload-field label{display:grid;gap:7px;color:#c0c8ca;font-size:14px;font-weight:800;line-height:1.45}.media-upload-copy input,.hero-upload-field input{box-sizing:border-box;width:100%;min-height:42px;padding:9px 11px;border:1px solid var(--l12-ui-line-strong,#46545d);background:var(--l12-ui-control,#050a0e);color:#fff;font-size:14px}.media-ratio-hint{margin:0;color:var(--l12-ui-text-muted,#96a2a7);font-size:13px;line-height:1.5}.media-upload-copy button,.upload-button{justify-self:start;min-height:42px;padding:10px 16px;border:1px solid #b99b45;background:#2c240e;color:#f0d477;font-size:14px;font-weight:900}.media-upload-copy button:disabled,.upload-button:disabled{opacity:.45}.hero-upload-field{display:grid;gap:18px;padding:20px;border:1px solid var(--l12-ui-line,#34434b);background:var(--l12-ui-panel,#091016)}.current-hero-preview{display:grid;grid-template-columns:180px minmax(0,1fr);align-items:center;gap:14px;padding:12px;border:1px solid var(--l12-ui-line,#39474e)}.current-hero-preview span{color:#b8c1c4;font-size:14px;font-weight:800}.current-hero-preview img{grid-column:1;width:180px;aspect-ratio:600/351;object-fit:cover}.hero-variant-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:16px}.hero-variant{display:grid;align-content:start;gap:12px;min-width:0;padding:15px;border:1px solid var(--l12-ui-line,#3d4b53);background:var(--l12-ui-panel-raised,#0e171e)}.hero-variant header{display:flex;align-items:baseline;justify-content:space-between}.hero-variant header b{color:#e9cf78;font-size:14px}.variant-preview{display:grid;place-items:center;overflow:hidden;width:100%;background:#05090c;color:#7f8c91;font-size:14px}.variant-preview img{display:block;width:100%;height:100%;object-fit:contain}.hero-variant p{margin:0;color:#98a4a9;font-size:14px;line-height:1.55}.hero-variant p.valid{color:#76d5a0}.hero-variant p.invalid{color:#f29aa4}@media(max-width:980px){.hero-variant-grid{grid-template-columns:1fr 1fr}.hero-variant:last-child{grid-column:1/-1}}@media(max-width:720px){.media-upload-field{grid-template-columns:1fr;padding:16px}.media-upload-field>img{width:100%;height:180px}.hero-variant-grid{grid-template-columns:1fr}.hero-variant:last-child{grid-column:auto}.hero-upload-field{padding:16px}.current-hero-preview{grid-template-columns:1fr}.current-hero-preview img{grid-column:auto;width:100%}}
.media-upload-field>img.contain{object-fit:contain;background:#03070a}
</style>
