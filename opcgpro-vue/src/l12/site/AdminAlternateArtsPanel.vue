<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { adminApi, hasPermission, platformState, type AlternateArt, type AlternateArtAwardRule, type AlternateArtGrant, type AlternateArtProduct, type AlternateArtRarity, type AlternateArtRankedParticipantDispatchPreview, type AlternateArtSearchPage, type PlatformAccount, type SiteCategory, type SiteMedia } from '@/l12/platform'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import MediaUploadField from './MediaUploadField.vue'
import SiteMediaPicker from './SiteMediaPicker.vue'
import SingleCardPicker, { type SingleCardPickerItem } from '@/l12/SingleCardPicker.vue'
import CardImage from '@/l12/CardImage.vue'
import CatalogCardDetails from '@/l12/CatalogCardDetails.vue'
import AdminRiskActionDialog from './AdminRiskActionDialog.vue'
import AdminAccountPicker from './AdminAccountPicker.vue'
import { useAdminRiskAction } from './useAdminRiskAction'
import { sharedGalleryAdminPath, usesSharedGallery } from '@/l12/deploymentBase'

const emit = defineEmits<{ notice: [value: string] }>()
const arts = ref<AlternateArt[]>([])
const grants = ref<AlternateArtGrant[]>([])
const awardRules = ref<AlternateArtAwardRule[]>([])
const catalog = ref<DeckCard[]>([])
const media = ref<SiteMedia[]>([])
const products = ref<AlternateArtProduct[]>([])
const rarities = ref<AlternateArtRarity[]>([])
const busy = ref(false)
const workspace = ref<'regular' | 'gallery' | 'rights'>(usesSharedGallery ? 'rights' : 'gallery')
const editorOpen = ref(false)
const regularEditorOpen = ref(false)
const rightsLoaded = ref(false)
const productDialogOpen = ref(false)
const productMergeDialogOpen = ref(false)
const productFilterDialogOpen = ref(false)
const productFilterPending = ref<Record<string, { request: number; value: boolean }>>({})
const productOrderDrafts = ref<Record<string, number>>({})
const detailCard = ref<(DeckCard & { galleryStandalone?: boolean; artist?: string }) | null>(null)
let productFilterRequest = 0
const productSaving = ref(false)
const pendingProductId = ref('')
const editorSession = ref(0)
const productDialogContext = ref(0)
let productSaveRequest = 0
const productCategories = ref<SiteCategory[]>([])
const productForm = reactive({ title: '', categoryId: '' })
const managedProductForm = reactive({ id: '', name: '', active: true })
const managedProductSaving = ref(false)
const productMergeForm = reactive({ targetProductId: '', sourceProductIds: [] as string[] })
const rarityManagerOpen = ref(false)
const imageDialogOpen = ref(false)
const imageTarget = ref<AlternateArt | null>(null)
const imageMediaAssetId = ref('')
const artsLoaded = ref(false)
const registryBusy = ref(false)
const registryFilters = reactive({ name: '', artCode: '', baseCard: '' })
const registry = ref<AlternateArtSearchPage>({ items: [], total: 0, page: 1, pageSize: 20 })
const registryPages = computed(() => Math.max(1, Math.ceil(registry.value.total / registry.value.pageSize)))
const regularRegistryBusy = ref(false)
const regularRegistryLoaded = ref(false)
const regularRegistryFilters = reactive({ name: '', cardCode: '', productId: '' })
const regularRegistry = ref<AlternateArtSearchPage>({ items: [], total: 0, page: 1, pageSize: 20 })
const regularRegistryPages = computed(() => Math.max(1, Math.ceil(regularRegistry.value.total / regularRegistry.value.pageSize)))
const grantForm = reactive({ alternateArtId: '', accountId: '', sourceKind: 'manual' as AlternateArtGrant['sourceKind'], sourceReference: '' })
const artForm = reactive({ id: '', artCode: '', baseCardId: '', displayName: '', mediaAssetId: '', active: true,
  kind: 'alternate-art' as 'alternate-art' | 'gallery-card', effect: '', artist: '', collaboration: '', productIds: [] as string[], rarityIds: [] as string[], builtIn: false })
const regularForm = reactive({ cardId: '', cardCode: '', displayName: '', mediaAssetId: '', artist: '', collaboration: '',
  productIds: [] as string[], rarityIds: [] as string[] })
const rarityForm = reactive({ id: '', code: '', name: '', active: true })
const ruleForm = reactive({ id: '', alternateArtId: '', kind: 'rank-reached' as AlternateArtAwardRule['kind'], seasonId: '', eventId: '', masterId: '', minimumTierIndex: 0, active: true })
const eventAccountId = ref('')
const eventAccounts = ref<PlatformAccount[]>([])
const betaGrantForm = reactive({ alternateArtId: '', seasonId: '' })
const betaGrantPreview = ref<AlternateArtRankedParticipantDispatchPreview | null>(null)
const rankedParticipantScope = ref<'current' | 'specified'>('current')
const artPickerTarget = ref<'base' | 'grant' | 'participants' | 'rule' | null>(null)
const canWrite = computed(() => hasPermission('admin.content.draft'))
const { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction } = useAdminRiskAction()
const cardById = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const selectedArt = computed(() => arts.value.find(item => item.id === grantForm.alternateArtId))
const selectedParticipantArt = computed(() => arts.value.find(item => item.id === betaGrantForm.alternateArtId))
const selectedRuleArt = computed(() => arts.value.find(item => item.id === ruleForm.alternateArtId))
const basePickerItems = computed<SingleCardPickerItem[]>(() => catalog.value.map(card => ({ id: card.id, cardId: card.id,
  number: card.number, name: card.nameZh, nameZh: card.nameZh, cardImageId: card.id, cardType: card.cardType,
  faction: card.faction, product: card.product, cost: card.cost, disasterLevel: card.disasterLevel,
  effect: card.effect, detailCard: card })))
const artPickerItems = computed<SingleCardPickerItem[]>(() => arts.value.filter(art => art.active && art.kind !== 'gallery-card').map(art => {
  const card = cardById.value.get(art.baseCardId)
  return { id: art.id, cardId: art.baseCardId, number: art.artCode, name: art.displayName, nameZh: art.displayName,
    imageUrl: art.thumbnailUrl || art.imageUrl || undefined, cardImageId: art.builtIn ? art.cardImageId : undefined,
    cardType: card?.cardType || 'alternate-art', faction: card?.faction, product: art.productName || card?.product,
    cost: card?.cost, disasterLevel: card?.disasterLevel, effect: card?.effect, detailCard: card,
    subtitle: `${art.artCode} · ${card?.nameZh || art.baseCardId}` }
}))
const selectedBaseCard = computed(() => cardById.value.get(artForm.baseCardId))
const replacementMedia = computed(() => media.value.find(item => item.id === imageMediaAssetId.value))
function notice(value: string) { emit('notice', value) }
function showCardDetails(art: AlternateArt) {
  const base = cardById.value.get(art.baseCardId)
  const standalone = art.kind === 'gallery-card'
  detailCard.value = {
    ...base,
    id: art.cardImageId || (art.builtIn ? base?.id : '') || `GALLERY-${art.id}`,
    number: art.artCode, nameZh: art.displayName || base?.nameZh || art.baseCardId,
    cardType: standalone ? 'gallery-card' : base?.cardType || 'unknown',
    faction: standalone ? '' : base?.faction || '', product: base?.product || '',
    imageUrl: art.imageUrl || base?.imageUrl, effect: standalone ? art.effect : base?.effect,
    products: art.products?.map(item => item.name) ?? [],
    rarity: art.rarities?.map(item => item.name).join(' / ') || undefined,
    artist: art.artist || undefined, collaboration: art.collaboration || undefined,
    galleryStandalone: standalone,
  }
}
async function ensureArtsLoaded() {
  if (artsLoaded.value) return
  arts.value = await adminApi.alternateArts()
  artsLoaded.value = true
}
async function openArtPicker(target: 'base' | 'grant' | 'participants' | 'rule') {
  if (target !== 'base') {
    try { await ensureArtsLoaded() }
    catch (error) { notice(error instanceof Error ? error.message : '异画资料读取失败'); return }
  }
  artPickerTarget.value = target
}
async function searchRegistry(page = 1) {
  registryBusy.value = true
  try {
    registry.value = await adminApi.searchAlternateArts({ ...registryFilters, page, pageSize: registry.value.pageSize, includeInactive: true })
  } catch (error) { notice(error instanceof Error ? error.message : '已登记异画查询失败') }
  finally { registryBusy.value = false }
}
function resetRegistryFilters() { Object.assign(registryFilters, { name: '', artCode: '', baseCard: '' }); void searchRegistry(1) }
async function searchRegularRegistry(page = 1) {
  regularRegistryBusy.value = true
  try {
    regularRegistry.value = await adminApi.searchRegularGalleryCards({
      ...regularRegistryFilters, page, pageSize: regularRegistry.value.pageSize,
    })
    regularRegistryLoaded.value = true
  } catch (error) { notice(error instanceof Error ? error.message : '常规卡画廊资料查询失败') }
  finally { regularRegistryBusy.value = false }
}
function resetRegularRegistryFilters() {
  Object.assign(regularRegistryFilters, { name: '', cardCode: '', productId: '' })
  void searchRegularRegistry(1)
}
function choosePickerItem(item: SingleCardPickerItem) {
  if (artPickerTarget.value === 'base') {
    artForm.baseCardId = item.cardId; artForm.displayName = item.name; artPickerTarget.value = null; return
  }
  const art = arts.value.find(row => row.id === item.id)
  if (!art) return
  if (artPickerTarget.value === 'grant') grantForm.alternateArtId = art.id
  else if (artPickerTarget.value === 'participants') betaGrantForm.alternateArtId = art.id
  else if (artPickerTarget.value === 'rule') ruleForm.alternateArtId = art.id
  betaGrantPreview.value = null
  artPickerTarget.value = null
}
function resetArt() { Object.assign(artForm, { id: '', artCode: '', baseCardId: '', displayName: '', mediaAssetId: '', active: true,
  kind: 'alternate-art', effect: '', artist: '', collaboration: '', productIds: [], rarityIds: [], builtIn: false }) }
function closeEditor() { editorSession.value += 1; editorOpen.value = false }
function closeRegularEditor() { editorSession.value += 1; regularEditorOpen.value = false }
function openNewArt() { editorSession.value += 1; resetArt(); editorOpen.value = true }
function editRegistryArt(art: AlternateArt) {
  editorSession.value += 1
  Object.assign(artForm, { id: art.id, artCode: art.artCode, baseCardId: art.baseCardId, displayName: art.displayName,
    mediaAssetId: art.mediaAssetId, active: art.active, kind: art.kind || 'alternate-art', effect: art.effect || '', artist: art.artist || '', collaboration: art.collaboration || '',
    productIds: art.products?.map(item => item.id) ?? (art.productId ? [art.productId] : []),
    rarityIds: art.rarities?.map(item => item.id) ?? [], builtIn: Boolean(art.builtIn) })
  editorOpen.value = true
}
function editRegularCard(card: AlternateArt) {
  editorSession.value += 1
  const catalogCard = cardById.value.get(card.baseCardId || card.id)
  const catalogRarity = catalogCard?.rarity
  const defaultRarity = catalogRarity
    ? rarities.value.find(item => item.code.toLocaleUpperCase() === catalogRarity.toLocaleUpperCase())
    : undefined
  Object.assign(regularForm, {
    cardId: card.baseCardId || card.id,
    cardCode: card.artCode || catalogCard?.number || card.id,
    displayName: card.displayName || catalogCard?.nameZh || card.id,
    mediaAssetId: card.mediaAssetId || '',
    artist: card.artist || '',
    collaboration: card.collaboration || '',
    productIds: card.products?.map(item => item.id) ?? [],
    rarityIds: card.rarities?.map(item => item.id) ?? (defaultRarity ? [defaultRarity.id] : []),
  })
  regularEditorOpen.value = true
}
function resetRarity() { Object.assign(rarityForm, { id: '', code: '', name: '', active: true }) }
function editRarity(rarity: AlternateArtRarity) { Object.assign(rarityForm, rarity) }
function uploaded(item: SiteMedia) { media.value = [item, ...media.value.filter(row => row.id !== item.id)]; artForm.mediaAssetId = item.id }
function replacementUploaded(item: SiteMedia) {
  media.value = [item, ...media.value.filter(row => row.id !== item.id)]
  imageMediaAssetId.value = item.id
}
function openImageEditor(art: AlternateArt) {
  imageTarget.value = art
  imageMediaAssetId.value = ''
  imageDialogOpen.value = true
}
function closeImageEditor() {
  imageDialogOpen.value = false
  imageTarget.value = null
  imageMediaAssetId.value = ''
}
async function performImageSave(mediaAssetId: string) {
  const target = imageTarget.value
  if (!target) return
  try {
    const regular = target.kind === 'regular-card'
    const saved = regular
      ? await adminApi.saveRegularGalleryCardImage({ cardId: target.baseCardId || target.id, mediaAssetId })
      : await adminApi.saveAlternateArtImage({ alternateArtId: target.id, mediaAssetId })
    if (regular) {
      regularRegistry.value = { ...regularRegistry.value,
        items: regularRegistry.value.items.map(item => item.id === saved.id ? saved : item) }
    } else {
      registry.value = { ...registry.value, items: registry.value.items.map(item => item.id === saved.id ? saved : item) }
      arts.value = arts.value.map(item => item.id === saved.id ? saved : item)
    }
    notice(mediaAssetId
      ? regular ? '常规卡画廊卡图已更换；对战规则与卡牌身份保持不变' : '卡图已更换；卡牌身份、产品、罕贵与权益保持不变'
      : '已恢复系统卡图')
    closeImageEditor()
  } catch (error) { notice(error instanceof Error ? error.message : '卡图更换失败'); throw error }
}
function saveReplacementImage() {
  const target = imageTarget.value
  if (!target || !imageMediaAssetId.value) { notice('请先上传或选择新的卡图素材'); return }
  requestRiskAction({ title: '确认更换卡图', target: `${target.artCode} · ${target.displayName}`,
    impact: target.kind === 'regular-card'
      ? '仅替换图鉴中的常规卡展示图；卡牌编号、卡效、产品、罕贵和对战规则保持不变。'
      : '仅替换画廊与牌库展示图片；原卡身份、异画编号、卡效、产品、罕贵、构筑和权益记录保持不变。',
    confirmLabel: '确认更换卡图', severity: 'warning', run: () => performImageSave(imageMediaAssetId.value) })
}
function restoreCatalogImage() {
  const target = imageTarget.value
  if (!target?.builtIn || !target.mediaAssetId) return
  requestRiskAction({ title: '确认恢复系统卡图', target: `${target.artCode} · ${target.displayName}`,
    impact: '移除当前上传的覆盖图，恢复卡牌图库中的系统原图；身份与所有归属保持不变。',
    confirmLabel: '恢复系统卡图', severity: 'warning', run: () => performImageSave('') })
}
function resetRule() { Object.assign(ruleForm, { id: '', alternateArtId: '', kind: 'rank-reached', seasonId: '', eventId: '', masterId: '', minimumTierIndex: 0, active: true }) }
function editRule(rule: AlternateArtAwardRule) { Object.assign(ruleForm, rule) }
async function load() {
  busy.value = true
  try {
    if (usesSharedGallery) {
      catalog.value = await loadDeckCatalog()
      rightsLoaded.value = false
      artsLoaded.value = false
      await loadRights()
      return
    }
    const [nextCatalog, nextProducts, nextRarities] = await Promise.all([
      loadDeckCatalog(), adminApi.alternateArtProducts(), adminApi.alternateArtRarities(),
    ])
    const nextCategories = await adminApi.siteCategories('product')
    catalog.value = nextCatalog; products.value = nextProducts; rarities.value = nextRarities
    productCategories.value = nextCategories.filter(item => item.active)
    if (!productCategories.value.some(item => item.id === productForm.categoryId))
      productForm.categoryId = productCategories.value[0]?.id || ''
    await searchRegistry(1)
  } catch (error) { notice(error instanceof Error ? error.message : '异画资料读取失败') }
  finally { busy.value = false }
}
async function loadRights() {
  if (rightsLoaded.value) return
  busy.value = true
  try {
    const [nextGrants, nextRules] = await Promise.all([adminApi.alternateArtGrants(), adminApi.alternateArtAwardRules()])
    grants.value = nextGrants; awardRules.value = nextRules
    await ensureArtsLoaded()
    rightsLoaded.value = true
  } catch (error) { notice(error instanceof Error ? error.message : '权益与派发资料读取失败') }
  finally { busy.value = false }
}
watch(workspace, value => {
  closeEditor()
  closeRegularEditor()
  if (value === 'rights') void loadRights()
  else if (value === 'regular' && !regularRegistryLoaded.value) void searchRegularRegistry(1)
})
function openProductDialog() {
  if (!pendingProductId.value)
    Object.assign(productForm, { title: '', categoryId: productCategories.value[0]?.id || '' })
  productDialogContext.value = editorSession.value
  productDialogOpen.value = true
}
function openProductMergeDialog() {
  Object.assign(productMergeForm, { targetProductId: '', sourceProductIds: [] })
  productMergeDialogOpen.value = true
}
async function performProductMerge() {
  const targetId = productMergeForm.targetProductId
  const sourceIds = [...productMergeForm.sourceProductIds]
  const result = await adminApi.mergeAlternateArtProducts({ targetProductId: targetId, sourceProductIds: sourceIds })
  products.value = await adminApi.alternateArtProducts()
  artForm.productIds = [...new Set(artForm.productIds.map(id => sourceIds.includes(id) ? targetId : id))]
  regularForm.productIds = [...new Set(regularForm.productIds.map(id => sourceIds.includes(id) ? targetId : id))]
  artsLoaded.value = false
  if (workspace.value === 'regular') await searchRegularRegistry(regularRegistry.value.page)
  else await searchRegistry(registry.value.page)
  productMergeDialogOpen.value = false
  notice(`产品已合并：迁移 ${result.migratedMetadataRows} 条异画归属，归档 ${result.archivedSiteProducts} 个重复产品草稿`)
}
function mergeProducts() {
  const target = products.value.find(item => item.id === productMergeForm.targetProductId)
  const sources = products.value.filter(item => productMergeForm.sourceProductIds.includes(item.id))
  if (!target || !sources.length) { notice('请选择一个保留产品和至少一个重复来源'); return }
  if (sources.some(item => item.id === target.id)) { notice('保留产品不能同时作为重复来源'); return }
  requestRiskAction({
    title: '确认合并重复产品', target: target.name, targetLabel: '保留产品',
    impact: `“${sources.map(item => item.name).join('”、“')}”的异画归属会迁移到保留产品；重复的产品草稿会归档，目录来源会隐藏。此操作会写入审计记录。`,
    confirmLabel: '确认合并', severity: 'warning', run: performProductMerge,
  })
}
async function selectCreatedProduct(expectedContext = productDialogContext.value) {
  if (!pendingProductId.value) return
  try {
    products.value = await adminApi.alternateArtProducts()
    const product = products.value.find(item => item.id === pendingProductId.value)
    if (!product) throw new Error('产品草稿已保存，但产品列表暂未读到该条目，请重试回选')
    if ((!editorOpen.value && !regularEditorOpen.value) || !productDialogOpen.value || expectedContext !== editorSession.value) {
      notice('产品草稿已保存；当前编辑窗口已变化，请重新打开“新建产品”后回选该草稿')
      return
    }
    const selectedProductIds = regularEditorOpen.value ? regularForm.productIds : artForm.productIds
    if (!selectedProductIds.includes(product.id)) selectedProductIds.push(product.id)
    pendingProductId.value = ''
    productDialogOpen.value = false
    notice('产品草稿已保存并选用；公开画廊会在产品中心正式发布后显示该归属')
  } catch (error) { notice(error instanceof Error ? error.message : '产品列表刷新失败') }
}
async function saveProductDraft() {
  if (pendingProductId.value) { await selectCreatedProduct(); return }
  const category = productCategories.value.find(item => item.id === productForm.categoryId)
  if (!productForm.title.trim() || !category) { notice('请填写产品名称并选择有效产品分类'); return }
  productSaving.value = true
  const request = ++productSaveRequest
  const expectedContext = productDialogContext.value
  try {
    const saved = await adminApi.saveArticle({ title: productForm.title.trim(), summary: '', body: '',
      category: category.name, categoryId: category.id, coverUrl: '', link: '', slug: '', pinned: false,
      kind: 'product', mediaAssetId: '', sortOrder: 0 })
    pendingProductId.value = `site:${saved.id}`
    if (request !== productSaveRequest || (!editorOpen.value && !regularEditorOpen.value) || !productDialogOpen.value
      || expectedContext !== editorSession.value) {
      notice('产品草稿已保存；当前编辑窗口已关闭或变化，没有自动改写其他卡片')
      return
    }
    await selectCreatedProduct(expectedContext)
  } catch (error) { notice(error instanceof Error ? error.message : '产品草稿保存失败') }
  finally { productSaving.value = false }
}
async function saveRarity() {
  try {
    const saved = await adminApi.saveAlternateArtRarity({ ...rarityForm, id: rarityForm.id || undefined })
    rarities.value = [saved, ...rarities.value.filter(item => item.id !== saved.id)]
    const selectedRarityIds = regularEditorOpen.value ? regularForm.rarityIds : artForm.rarityIds
    if (!selectedRarityIds.includes(saved.id)) selectedRarityIds.push(saved.id)
    resetRarity()
    notice('罕贵定义已保存，可重复用于多张异画或联动卡')
  } catch (error) { notice(error instanceof Error ? error.message : '罕贵定义保存失败') }
}
async function performSaveArt() {
  try {
    const base = artForm.kind === 'alternate-art' ? cardById.value.get(artForm.baseCardId) : undefined
    if (artForm.kind === 'alternate-art' && !base) { notice('请先通过单卡筛选选择原卡'); return }
    if (artForm.kind === 'gallery-card' && !artForm.displayName.trim()) { notice('请填写联动卡名称'); return }
    if (base) artForm.displayName = base.nameZh
    let saved = artForm.builtIn ? registry.value.items.find(item => item.id === artForm.id) : undefined
    if (!saved) {
      saved = await adminApi.saveAlternateArt({ id: artForm.id || undefined, artCode: artForm.artCode,
        baseCardId: artForm.kind === 'gallery-card' ? '' : artForm.baseCardId, displayName: artForm.displayName,
        mediaAssetId: artForm.mediaAssetId, active: artForm.active, kind: artForm.kind, effect: artForm.effect })
      // 主体保存与元数据保存是两个请求。后者失败时保留服务端已生成的稳定 ID，
      // 让风险对话框内的再次确认更新同一条记录，同时保留用户当前多选内容。
      artForm.id = saved.id
    }
    saved = await adminApi.saveAlternateArtMetadata({ alternateArtId: saved.id,
      productIds: [...artForm.productIds], rarityIds: [...artForm.rarityIds], artist: artForm.artist, collaboration: artForm.collaboration })
    arts.value = [saved, ...arts.value.filter(item => item.id !== saved.id)]
    if (saved.kind !== 'gallery-card') grantForm.alternateArtId ||= saved.id
    notice(saved.kind === 'gallery-card' ? '联动卡已保存到画廊；不会进入卡池、构筑、拥有或派发系统'
      : saved.active ? '异画及其产品、罕贵归属已保存并同步画廊' : '异画已保存为停用状态，不会在画廊展示')
    resetArt()
    closeEditor()
    await searchRegistry(registry.value.page)
  } catch (error) { notice(error instanceof Error ? error.message : '异画保存失败'); throw error }
}
function saveArt() {
  if (!canWrite.value) return
  const base = artForm.kind === 'alternate-art' ? cardById.value.get(artForm.baseCardId) : undefined
  if (artForm.kind === 'alternate-art' && !base) { notice('请先通过单卡筛选选择原卡'); return }
  if (artForm.kind === 'gallery-card' && !artForm.displayName.trim()) { notice('请填写联动卡名称'); return }
  if (!artForm.active) { void performSaveArt().catch(() => {}); return }
  const targetName = artForm.kind === 'gallery-card' ? artForm.displayName : base?.nameZh || artForm.displayName
  requestRiskAction({ title: artForm.kind === 'gallery-card' ? '确认发布联动卡到画廊' : '确认启用异画并同步画廊', target: `${artForm.artCode || '未编号'} · ${targetName}`,
    impact: artForm.kind === 'gallery-card' ? '保存后会进入画廊并展示卡效文字，但不会接入卡效结算，也不产生拥有权或构筑身份。' : '保存后会立即进入玩家画廊；已有权益的玩家可在构筑中选用。',
    confirmLabel: '保存并同步画廊', severity: 'warning', run: performSaveArt })
}
async function performSaveRegularCard() {
  try {
    const saved = await adminApi.saveRegularGalleryCardMetadata({
      cardId: regularForm.cardId,
      productIds: [...regularForm.productIds],
      rarityIds: [...regularForm.rarityIds],
      artist: regularForm.artist,
      collaboration: regularForm.collaboration,
    })
    regularRegistry.value = { ...regularRegistry.value,
      items: regularRegistry.value.items.map(item => item.id === saved.id ? saved : item) }
    closeRegularEditor()
    notice('常规卡画廊资料已保存；卡牌规则与对战身份保持不变')
  } catch (error) { notice(error instanceof Error ? error.message : '常规卡画廊资料保存失败'); throw error }
}
function saveRegularCard() {
  if (!canWrite.value || !regularForm.cardId) return
  requestRiskAction({
    title: '确认更新常规卡画廊资料', target: `${regularForm.cardCode} · ${regularForm.displayName}`,
    impact: '保存后会更新图鉴中的产品、罕贵、画师与联动资料；卡牌编号、卡效、构筑和对战规则不变。',
    confirmLabel: '保存画廊资料', severity: 'warning', run: performSaveRegularCard,
  })
}
function productFilterChecked(product: AlternateArtProduct) {
  return productFilterPending.value[product.id]?.value ?? product.showInGalleryFilter !== false
}
function clearProductFilterPending(productId: string, request: number) {
  if (productFilterPending.value[productId]?.request !== request) return
  const next = { ...productFilterPending.value }
  delete next[productId]
  productFilterPending.value = next
}
async function setProductFilterVisibility(product: AlternateArtProduct, showInGalleryFilter: boolean, releaseOrder?: number) {
  if (!canWrite.value) return 'failed' as const
  const request = ++productFilterRequest
  productFilterPending.value = {
    ...productFilterPending.value,
    [product.id]: { request, value: showInGalleryFilter },
  }
  try {
    const saved = await adminApi.saveGalleryProductFilterVisibility({ productId: product.id, showInGalleryFilter, releaseOrder })
    if (productFilterPending.value[product.id]?.request !== request) return 'stale' as const
    products.value = products.value.map(item => item.id === saved.id ? { ...item,
      showInGalleryFilter: saved.showInGalleryFilter, releaseOrder: saved.releaseOrder } : item)
    products.value.sort((a, b) => (a.releaseOrder ?? 0) - (b.releaseOrder ?? 0)
      || (a.categoryName || '').localeCompare(b.categoryName || '', 'zh-CN') || a.name.localeCompare(b.name, 'zh-CN'))
    if (releaseOrder !== undefined) delete productOrderDrafts.value[product.id]
    clearProductFilterPending(product.id, request)
    notice(releaseOrder !== undefined ? `“${saved.name}”的发布顺序已保存`
      : showInGalleryFilter ? `“${saved.name}”已进入玩家端产品筛选` : `“${saved.name}”已从玩家端产品筛选隐藏`)
    return 'saved' as const
  } catch (error) {
    if (productFilterPending.value[product.id]?.request !== request) return 'stale' as const
    clearProductFilterPending(product.id, request)
    notice(error instanceof Error ? error.message : '产品筛选设置保存失败')
    return 'failed' as const
  }
}
async function onProductFilterChange(product: AlternateArtProduct, event: Event) {
  const input = event.target as HTMLInputElement
  if (await setProductFilterVisibility(product, input.checked) === 'failed')
    input.checked = product.showInGalleryFilter !== false
}
function saveProductOrder(product: AlternateArtProduct) {
  const value = productOrderDrafts.value[product.id] ?? product.releaseOrder ?? 0
  if (!Number.isSafeInteger(value) || value < 0 || value > 100000) { notice('发布顺序须为0至100000的整数'); return }
  void setProductFilterVisibility(product, product.showInGalleryFilter !== false, value)
}
function editManagedProduct(product?: AlternateArtProduct) {
  if (managedProductSaving.value) return
  Object.assign(managedProductForm, { id: product?.id || '', name: product?.name || '', active: product?.active ?? true })
}
async function saveManagedProduct() {
  if (!canWrite.value || managedProductSaving.value) return
  if (!managedProductForm.name.trim()) { notice('请填写产品名称'); return }
  managedProductSaving.value = true
  const draft = { ...managedProductForm, id: managedProductForm.id || undefined }
  try {
    const saved = await adminApi.saveAlternateArtProduct(draft)
    const existing = products.value.some(item => item.id === saved.id)
    products.value = existing ? products.value.map(item => item.id === saved.id
      ? { ...item, name: saved.name, active: saved.active, updatedAt: saved.updatedAt } : item) : [...products.value, saved]
    products.value.sort((a, b) => (a.releaseOrder ?? 0) - (b.releaseOrder ?? 0) || a.name.localeCompare(b.name, 'zh-CN'))
    Object.assign(managedProductForm, { id: '', name: '', active: true })
    notice('收录产品已保存')
    artsLoaded.value = false
    if (workspace.value === 'regular') await searchRegularRegistry(regularRegistry.value.page)
    else if (workspace.value === 'gallery') await searchRegistry(registry.value.page)
  } catch (error) { notice(error instanceof Error ? error.message : '收录产品保存失败') }
  finally { managedProductSaving.value = false }
}
async function performGrant() {
  if (!grantForm.alternateArtId || !grantForm.accountId) { notice('请选择异画和玩家账号'); return }
  try { const saved = await adminApi.grantAlternateArt(grantForm); grants.value = [saved, ...grants.value.filter(item => item.id !== saved.id)]; notice('异画权益已派发，玩家下次刷新构筑即可选用') }
  catch (error) { notice(error instanceof Error ? error.message : '异画权益派发失败'); throw error }
}
function grant() {
  if (!canWrite.value) return
  if (!grantForm.alternateArtId || !grantForm.accountId) { notice('请选择异画和玩家账号'); return }
  requestRiskAction({ title: '确认直接派发异画权益', target: grantForm.accountId, impact: '该玩家刷新构筑后即可选用所选异画；操作会写入权益与审计记录。', confirmLabel: '派发权益', severity: 'warning', run: performGrant })
}
async function performRevoke(grant: AlternateArtGrant) {
  try { await adminApi.revokeAlternateArtGrant(grant.id); grants.value = grants.value.map(item => item.id === grant.id ? { ...item, revokedAt: new Date().toISOString() } : item); notice('异画权益已撤回') }
  catch (error) { notice(error instanceof Error ? error.message : '撤回失败'); throw error }
}
function revoke(grant: AlternateArtGrant) {
  if (!canWrite.value) return
  requestRiskAction({ title: '确认撤回异画权益', target: grant.username, impact: '该玩家现有构筑中的异画会自动回退为原卡图。', confirmLabel: '撤回权益', severity: 'danger', run: () => performRevoke(grant) })
}
async function performSaveRule() {
  if (!ruleForm.alternateArtId) { notice('请先选择要派发的异画'); return }
  try {
    const saved = await adminApi.saveAlternateArtAwardRule({ ...ruleForm, id: ruleForm.id || undefined })
    awardRules.value = [saved, ...awardRules.value.filter(rule => rule.id !== saved.id)]
    notice(saved.kind === 'event' ? '活动异画规则已保存，可在下方粘贴玩家名单执行派发' : '赛季异画规则已保存，将由结算自动派发')
    resetRule()
  } catch (error) { notice(error instanceof Error ? error.message : '异画规则保存失败'); throw error }
}
function saveRule() {
  if (!canWrite.value || !ruleForm.alternateArtId) { if (!ruleForm.alternateArtId) notice('请先选择要派发的异画'); return }
  requestRiskAction({ title: '确认保存自动派发规则', target: selectedRuleArt.value?.displayName || ruleForm.alternateArtId, impact: ruleForm.active ? '规则启用后会在对应赛季、结算或活动流程中自动授予玩家权益。' : '规则将以停用状态保存，不会自动派发。', confirmLabel: '保存规则', severity: ruleForm.active ? 'danger' : 'warning', run: performSaveRule })
}
function addEventAccount(account: PlatformAccount) {
  if (!eventAccounts.value.some(item => item.id === account.id)) eventAccounts.value.push(account)
}
function removeEventAccount(accountId: string) {
  eventAccounts.value = eventAccounts.value.filter(item => item.id !== accountId)
  if (eventAccountId.value === accountId) eventAccountId.value = ''
}
watch(() => platformState.account?.id, (next, previous) => {
  if (previous && next !== previous) eventAccounts.value = []
})
async function performDispatchEvent(rule: AlternateArtAwardRule, accountIds: string[]) {
  try {
    const created = await adminApi.dispatchAlternateArtEvent(rule.id, accountIds)
    grants.value = [...created, ...grants.value.filter(item => !created.some(saved => saved.id === item.id))]
    eventAccountId.value = ''
    eventAccounts.value = []
    notice(`已完成活动派发：${created.length} 名玩家`)
  } catch (error) { notice(error instanceof Error ? error.message : '活动异画派发失败'); throw error }
}
function dispatchEvent(rule: AlternateArtAwardRule) {
  if (!canWrite.value) return
  const accountIds = eventAccounts.value.map(account => account.id)
  if (!accountIds.length) { notice('请先从账号候选中加入活动获奖玩家'); return }
  requestRiskAction({ title: '确认执行活动名单派发', target: `${rule.eventId || '活动'} · ${accountIds.length} 名玩家`, impact: '系统会按唯一玩家ID逐名校验、授予异画权益并写入派发记录。', confirmLabel: '执行活动派发', severity: 'danger', run: () => performDispatchEvent(rule, accountIds) })
}
async function previewBetaGrant() {
  if (!betaGrantForm.alternateArtId) { notice('请先选择要派发的异画'); return }
  if (rankedParticipantScope.value === 'specified' && !betaGrantForm.seasonId.trim()) { notice('请填写指定赛季编号'); return }
  try { betaGrantPreview.value = await adminApi.previewAlternateArtRankedParticipants({
    alternateArtId: betaGrantForm.alternateArtId,
    seasonId: rankedParticipantScope.value === 'specified' ? betaGrantForm.seasonId.trim() : undefined,
  }) }
  catch (error) { notice(error instanceof Error ? error.message : '排位参与者预览失败') }
}
async function performDispatchBetaGrant() {
  if (!betaGrantPreview.value || !betaGrantForm.alternateArtId) { notice('请先执行预览并核对人数'); return }
  try {
    const saved = await adminApi.dispatchAlternateArtRankedParticipants({
      alternateArtId: betaGrantForm.alternateArtId,
      seasonId: rankedParticipantScope.value === 'specified' ? betaGrantForm.seasonId.trim() : undefined,
    })
    grants.value = [...saved, ...grants.value.filter(item => !saved.some(row => row.id === item.id))]
    notice(`已完成 ${betaGrantPreview.value.seasonId} 赛季排位参与者派发：${betaGrantPreview.value.toGrant} 名新增权益`)
    betaGrantPreview.value = null
  } catch (error) { notice(error instanceof Error ? error.message : '排位参与者派发失败'); throw error }
}
function dispatchBetaGrant() {
  if (!canWrite.value || !betaGrantPreview.value || !betaGrantForm.alternateArtId) return
  const preview = betaGrantPreview.value
  requestRiskAction({ title: '确认向排位参与者派发异画', target: `${preview.seasonId} · ${preview.toGrant} 名玩家`, impact: `将新增 ${preview.toGrant} 条权益；已拥有同来源权益的 ${preview.alreadyGranted} 人不会重复派发。`, confirmLabel: '确认派发', severity: 'danger', run: performDispatchBetaGrant })
}
function onWindowKeydown(event: KeyboardEvent) {
  if (event.key !== 'Escape' || riskAction.value || detailCard.value) return
  if (productFilterDialogOpen.value) productFilterDialogOpen.value = false
  else if (productMergeDialogOpen.value) productMergeDialogOpen.value = false
  else if (productDialogOpen.value) productDialogOpen.value = false
  else if (imageDialogOpen.value) closeImageEditor()
  else if (regularEditorOpen.value) closeRegularEditor()
  else if (editorOpen.value) closeEditor()
}
onMounted(() => { window.addEventListener('keydown', onWindowKeydown); void load() })
onBeforeUnmount(() => window.removeEventListener('keydown', onWindowKeydown))
</script>

<template>
  <section class="alternate-admin">
    <CatalogCardDetails v-if="detailCard" :card="detailCard" within-picker @close="detailCard = null"/>
    <AdminRiskActionDialog v-if="riskAction" :title="riskAction.title" :target="riskAction.target" :impact="riskAction.impact" :confirm-label="riskAction.confirmLabel" :severity="riskAction.severity" :busy="riskBusy" :error="riskError" @cancel="cancelRiskAction" @confirm="confirmRiskAction"/>
    <header><div><small>GALLERY WORKBENCH</small><h3>画廊卡片后台</h3><p v-if="usesSharedGallery">卡图与图鉴统一管理，测试服权益单独维护。</p><p v-else>上传新卡、替换已有卡图并维护必要资料。联动卡填写正式卡效文本，但不接入对局结算、牌池、构筑或权益。</p></div><button @click="load">{{ busy ? '读取中…' : '刷新当前资料' }}</button></header>
    <section v-if="usesSharedGallery" class="task-strip shared-gallery-source"><div><h4>共用卡图与图鉴资料</h4><p>两服使用同一份已发布资料。上传卡图、维护卡片与产品请进入正式资料库；修改会同时影响两服，并使用正式服账号校验权限。</p><p>下方权益与派发仅属于测试服，不会影响正式玩家。</p></div><a :href="sharedGalleryAdminPath" target="_blank" rel="noopener noreferrer">打开资料库管理</a></section>
    <nav v-else class="workspace-tabs" aria-label="画廊后台工作区"><button :class="{ active: workspace === 'regular' }" @click="workspace = 'regular'">常规卡</button><button :class="{ active: workspace === 'gallery' }" @click="workspace = 'gallery'">异画 / 联动卡</button><button :class="{ active: workspace === 'rights' }" @click="workspace = 'rights'">权益与派发</button></nav>
    <div v-if="!usesSharedGallery && workspace === 'regular'" class="gallery-workspace regular-gallery-workspace">
      <section class="task-strip"><div><small>REGULAR CARDS</small><h4>常规卡画廊资料</h4><p>管理官方常规卡在图鉴中使用的产品、罕贵、画师、联动和展示卡图，不修改卡效、构筑或对战身份。</p></div><div class="task-actions"><button v-if="canWrite" type="button" @click="productFilterDialogOpen = true">管理收录产品</button><button v-if="canWrite" type="button" @click="openProductMergeDialog">合并重复产品</button></div></section>
      <form class="gallery-filters" @submit.prevent="searchRegularRegistry(1)"><label>卡名<input v-model.trim="regularRegistryFilters.name" placeholder="常规卡名称"></label><label>卡号<input v-model.trim="regularRegistryFilters.cardCode" placeholder="官方卡号"></label><label>收录产品<select v-model="regularRegistryFilters.productId"><option value="">全部产品</option><option v-for="item in products" :key="item.id" :value="item.id">{{ item.name }} · {{ item.categoryName || '未分类' }}</option></select></label><div class="filter-actions"><button type="button" @click="resetRegularRegistryFilters">重置</button><button class="primary" type="submit">查询</button></div></form>
      <section class="gallery-list" :aria-busy="regularRegistryBusy"><header><div><h4>常规卡列表</h4><p>当前结果 {{ regularRegistry.total }} 项；此处资料只控制画廊展示。</p></div></header><article v-for="card in regularRegistry.items" :key="card.id"><button class="gallery-card-open" type="button" :aria-label="`查看${card.displayName}详情`" @click="showCardDetails(card)"><CardImage :card-id="card.cardImageId || card.baseCardId || card.id" :legacy-url="card.thumbnailUrl || card.imageUrl" :alt="card.displayName" intent="thumb"/></button><div class="art-summary"><b>{{ card.displayName }}</b><span>{{ card.artCode }} · 官方常规卡</span><div class="chips"><em v-for="item in card.products || []" :key="item.id">{{ item.name }}<small v-if="item.source === 'site' && item.status !== 'published'">草稿</small></em><em v-for="item in card.rarities || []" :key="item.id">{{ item.code }}</em><em v-if="card.artist">画师：{{ card.artist }}</em><em v-if="card.collaboration">联动：{{ card.collaboration }}</em></div></div><div class="row-actions"><button type="button" @click="editRegularCard(card)">编辑资料</button><button class="primary" type="button" @click="openImageEditor(card)">更换画廊卡图</button></div></article><p v-if="!regularRegistryBusy && !regularRegistry.items.length">没有符合条件的常规卡。</p><footer><span>第 {{ regularRegistry.page }} / {{ regularRegistryPages }} 页</span><div><button :disabled="regularRegistryBusy || regularRegistry.page <= 1" @click="searchRegularRegistry(regularRegistry.page - 1)">上一页</button><button :disabled="regularRegistryBusy || regularRegistry.page >= regularRegistryPages" @click="searchRegularRegistry(regularRegistry.page + 1)">下一页</button></div></footer></section>
      <section v-if="regularEditorOpen" class="art-editor ui-state-scope" role="dialog" aria-modal="true" aria-labelledby="regular-gallery-editor-title"><header class="editor-heading"><div><small>REGULAR CARD</small><h4 id="regular-gallery-editor-title">编辑常规卡画廊资料</h4></div><button type="button" @click="closeRegularEditor">关闭</button></header><div class="form-grid"><label>卡号<input :value="regularForm.cardCode" disabled></label><label>卡名<input :value="regularForm.displayName" disabled></label><label>画师<input v-model.trim="regularForm.artist" maxlength="120" placeholder="例如：画师姓名或署名"></label><label>联动<input v-model.trim="regularForm.collaboration" maxlength="120" placeholder="联动作品或合作方（可选）"></label><fieldset class="metadata-choice"><legend>收录产品（可多选） <button v-if="canWrite" type="button" @click="openProductDialog">新建产品</button></legend><label v-for="item in products.filter(product => product.active || regularForm.productIds.includes(product.id))" :key="item.id" class="check"><input v-model="regularForm.productIds" type="checkbox" :value="item.id"><span>{{ item.name }}<small>{{ item.categoryName || '未分类' }} · {{ item.source === 'site' ? item.status === 'published' ? '已发布' : '草稿' : item.source === 'catalog' ? '卡牌产品' : '自定义产品' }}</small></span></label></fieldset><fieldset class="metadata-choice"><legend>罕贵（可多选） <button v-if="canWrite" type="button" @click="rarityManagerOpen = !rarityManagerOpen">管理罕贵</button></legend><label v-for="item in rarities.filter(rarity => rarity.active || regularForm.rarityIds.includes(rarity.id))" :key="item.id" class="check"><input v-model="regularForm.rarityIds" type="checkbox" :value="item.id">{{ item.code }} · {{ item.name }}</label><p v-if="!rarities.length">尚未建立罕贵定义。</p></fieldset></div><section v-if="rarityManagerOpen" class="inline-manager"><h5>可复用罕贵</h5><div class="form-grid"><label>罕贵代码<input v-model.trim="rarityForm.code" maxlength="40" placeholder="例如：UR"></label><label>显示名称<input v-model.trim="rarityForm.name" maxlength="80" placeholder="例如：终极罕贵"></label><label class="check"><input v-model="rarityForm.active" type="checkbox">可继续选用</label></div><div class="actions"><button @click="resetRarity">清空</button><button class="primary" @click="saveRarity">保存罕贵定义</button></div><article v-for="rarity in rarities" :key="rarity.id"><span><b>{{ rarity.code }}</b> · {{ rarity.name }} · {{ rarity.active ? '可用' : '已停用' }}</span><button @click="editRarity(rarity)">编辑</button></article></section><div class="actions"><button @click="closeRegularEditor">取消</button><button v-if="canWrite" class="primary" @click="saveRegularCard">保存画廊资料</button></div></section>
    </div>
    <div v-else-if="!usesSharedGallery && workspace === 'gallery'" class="gallery-workspace">
      <section class="task-strip"><div><small>ALTERNATE &amp; COLLAB</small><h4>上传与维护异画 / 联动卡</h4><p>从独立列表编辑资料或更换卡图；权益派发保留在单独页签。</p></div><div class="task-actions"><button v-if="canWrite" type="button" @click="productFilterDialogOpen = true">管理收录产品</button><button v-if="canWrite" type="button" @click="openProductMergeDialog">合并重复产品</button><button v-if="canWrite" class="primary" type="button" @click="openNewArt">上传新卡 / 异画</button></div></section>
      <form class="gallery-filters" @submit.prevent="searchRegistry(1)"><label>名称<input v-model.trim="registryFilters.name" placeholder="异画或联动卡名称"></label><label>编号<input v-model.trim="registryFilters.artCode" placeholder="异画或联动编号"></label><label>绑定原卡<input v-model.trim="registryFilters.baseCard" placeholder="原卡名或编号"></label><div class="filter-actions"><button type="button" @click="resetRegistryFilters">重置</button><button class="primary" type="submit">查询</button></div></form>
      <section class="gallery-list" :aria-busy="registryBusy"><header><div><h4>卡片列表</h4><p>当前结果 {{ registry.total }} 项；产品草稿带状态标记，只有发布后才会出现在公开画廊。</p></div></header><article v-for="art in registry.items" :key="art.id"><button class="gallery-card-open" type="button" :aria-label="`查看${art.displayName}详情`" @click="showCardDetails(art)"><CardImage v-if="art.builtIn" :card-id="art.cardImageId" :legacy-url="art.thumbnailUrl || art.imageUrl" :alt="art.displayName" intent="thumb"/><img v-else :src="art.thumbnailUrl || art.imageUrl" :alt="art.displayName"></button><div class="art-summary"><b>{{ art.displayName }}</b><span>{{ art.artCode || '待补编号' }} · {{ art.kind === 'gallery-card' ? '独立联动展示卡' : `${art.baseCardId} · ${art.baseCardName || cardById.get(art.baseCardId)?.nameZh || '原卡资料加载中'}` }}</span><div class="chips"><em v-for="item in art.products" :key="item.id">{{ item.name }}<small v-if="item.source === 'site' && item.status !== 'published'">草稿</small></em><em v-for="item in art.rarities" :key="item.id">{{ item.code }}</em><em v-if="art.artist">画师：{{ art.artist }}</em><em v-if="art.collaboration">联动：{{ art.collaboration }}</em><em>{{ art.active ? '展示中' : '已停用' }}</em></div></div><div class="row-actions"><button type="button" @click="editRegistryArt(art)">编辑资料</button><button class="primary" type="button" @click="openImageEditor(art)">更换卡图</button></div></article><p v-if="!registryBusy && !registry.items.length">没有符合条件的画廊卡片。</p><footer><span>第 {{ registry.page }} / {{ registryPages }} 页</span><div><button :disabled="registryBusy || registry.page <= 1" @click="searchRegistry(registry.page - 1)">上一页</button><button :disabled="registryBusy || registry.page >= registryPages" @click="searchRegistry(registry.page + 1)">下一页</button></div></footer></section>
      <section v-if="editorOpen" class="art-editor ui-state-scope" role="dialog" aria-modal="true" aria-labelledby="alternate-art-editor-title"><header class="editor-heading"><div><small>{{ artForm.id ? 'EDIT ENTRY' : 'CREATE ENTRY' }}</small><h4 id="alternate-art-editor-title">{{ artForm.id ? '编辑卡片资料' : '上传新卡 / 异画' }}</h4></div><button type="button" @click="closeEditor">关闭</button></header><div class="form-grid"><label>条目类型<select v-model="artForm.kind" :disabled="Boolean(artForm.id)"><option value="alternate-art">已有卡的异画</option><option value="gallery-card">独立联动展示卡</option></select></label><label>编号<input v-model.trim="artForm.artCode" maxlength="80" placeholder="例如：ALT-S01-001 / COLLAB-001"></label><label v-if="artForm.kind === 'alternate-art'">绑定原卡<button class="art-choice" type="button" :disabled="artForm.builtIn || Boolean(artForm.id)" @click="openArtPicker('base')">{{ selectedBaseCard ? `${selectedBaseCard.number} · ${selectedBaseCard.nameZh}` : '打开单卡筛选选择原卡' }}</button></label><label v-if="artForm.kind === 'alternate-art'">卡名（随原卡）<input :value="selectedBaseCard?.nameZh || artForm.displayName || '选择原卡后自动带入'" disabled></label><label v-else>联动卡名称<input v-model.trim="artForm.displayName" maxlength="120" placeholder="独立撰写名称"></label><label>画师<input v-model.trim="artForm.artist" maxlength="120" placeholder="例如：画师姓名或署名"></label><label>联动<input v-model.trim="artForm.collaboration" maxlength="120" placeholder="联动作品或合作方（可选）"></label><label v-if="artForm.kind === 'gallery-card'" class="full-field">联动卡卡效<textarea v-model.trim="artForm.effect" rows="4" maxlength="2000" placeholder="填写在画廊展示的正式卡效文本；不会接入对局结算"></textarea></label><fieldset class="metadata-choice"><legend>收录产品（可多选） <button v-if="canWrite" type="button" @click="openProductDialog">新建产品</button></legend><label v-for="item in products.filter(product => product.active || artForm.productIds.includes(product.id))" :key="item.id" class="check"><input v-model="artForm.productIds" type="checkbox" :value="item.id"><span>{{ item.name }}<small>{{ item.categoryName || '未分类' }} · {{ item.source === 'site' ? item.status === 'published' ? '已发布' : '草稿' : item.source === 'catalog' ? '卡牌产品' : '自定义产品' }}</small></span></label></fieldset><fieldset class="metadata-choice"><legend>罕贵（可多选） <button v-if="canWrite" type="button" @click="rarityManagerOpen = !rarityManagerOpen">管理罕贵</button></legend><label v-for="item in rarities.filter(rarity => rarity.active || artForm.rarityIds.includes(rarity.id))" :key="item.id" class="check"><input v-model="artForm.rarityIds" type="checkbox" :value="item.id">{{ item.code }} · {{ item.name }}</label><p v-if="!rarities.length">尚未建立罕贵定义。</p></fieldset><label v-if="!artForm.id">卡图素材<SiteMediaPicker v-model="artForm.mediaAssetId" kind="card-art" :selected-media="media.find(item => item.id === artForm.mediaAssetId)" @selected="uploaded"/></label><label class="check"><input v-model="artForm.active" type="checkbox" :disabled="artForm.builtIn">启用并在画廊展示</label></div><MediaUploadField v-if="canWrite && !artForm.id" kind="card-art" :initial-alt="artForm.kind === 'gallery-card' ? artForm.displayName || '联动卡图' : selectedBaseCard?.nameZh || '异画卡图'" @uploaded="uploaded" @notice="notice"/><section v-if="rarityManagerOpen" class="inline-manager"><h5>可复用罕贵</h5><div class="form-grid"><label>罕贵代码<input v-model.trim="rarityForm.code" maxlength="40" placeholder="例如：UR"></label><label>显示名称<input v-model.trim="rarityForm.name" maxlength="80" placeholder="例如：终极罕贵"></label><label class="check"><input v-model="rarityForm.active" type="checkbox">可继续选用</label></div><div class="actions"><button @click="resetRarity">清空</button><button class="primary" @click="saveRarity">保存罕贵定义</button></div><article v-for="rarity in rarities" :key="rarity.id"><span><b>{{ rarity.code }}</b> · {{ rarity.name }} · {{ rarity.active ? '可用' : '已停用' }}</span><button @click="editRarity(rarity)">编辑</button></article></section><div class="actions"><button @click="closeEditor">取消</button><button v-if="canWrite" class="primary" @click="saveArt">保存卡片资料</button></div></section>
    </div>
    <div v-else class="rights-workspace">
    <section class="grant-panel"><h4>直接派发（兜底）</h4><p>赛季达段、赛季结算和活动派发将使用同一权益记录；此处可处理补发、修正与活动名单。</p><div class="form-grid"><label>异画<button class="art-choice" type="button" @click="openArtPicker('grant')">{{ selectedArt ? `${selectedArt.artCode} · ${selectedArt.displayName}` : '打开异画卡查选择' }}</button></label><AdminAccountPicker v-model="grantForm.accountId"/><label>派发来源<select v-model="grantForm.sourceKind"><option value="manual">后台直接派发</option><option value="rank-reached">赛季达到段位</option><option value="season-final">赛季结算</option><option value="event">活动派发</option></select></label><label>派发原因<input v-model.trim="grantForm.sourceReference" maxlength="240" :placeholder="selectedArt ? `例如：${selectedArt.displayName} 补发` : '可留空；填写后会原样展示给玩家'"></label></div><button v-if="hasPermission('admin.content.draft')" class="primary" @click="grant">派发异画权益</button></section>
    <section class="award-rules"><h4>自动派发规则</h4><p>“达到段位”在排位结算后即时检查；“赛季结算”和“最强主宰”在后台归档旧赛季时自动执行；活动规则需由管理员在名单确认后执行。</p><div class="form-grid"><label>异画<button class="art-choice" type="button" @click="openArtPicker('rule')">{{ selectedRuleArt ? `${selectedRuleArt.artCode} · ${selectedRuleArt.displayName}` : '打开异画卡查选择' }}</button></label><label>规则类型<select v-model="ruleForm.kind"><option value="rank-reached">赛季达到段位</option><option value="season-final">赛季结算</option><option value="master-champion-season-final">赛季最强主宰</option><option value="event">活动派发</option></select></label><label v-if="ruleForm.kind !== 'event'">赛季编号<input v-model.trim="ruleForm.seasonId" maxlength="100" placeholder="例如：S01"></label><label v-else>活动编号<input v-model.trim="ruleForm.eventId" maxlength="160" placeholder="例如：2026-国庆活动"></label><label v-if="ruleForm.kind === 'master-champion-season-final'">指定主宰<select v-model="ruleForm.masterId"><option value="">所有最强主宰得主</option><option v-for="card in catalog.filter(item => item.cardType === 'master')" :key="card.id" :value="card.id">{{ card.nameZh }}</option></select></label><label v-if="ruleForm.kind === 'rank-reached' || ruleForm.kind === 'season-final'">最低段位<select v-model.number="ruleForm.minimumTierIndex"><option v-for="index in 5" :key="index - 1" :value="index - 1">第 {{ index }} 档及以上</option></select></label><label class="check"><input v-model="ruleForm.active" type="checkbox">启用此规则</label></div><div class="actions"><button @click="resetRule">清空</button><button v-if="hasPermission('admin.content.draft')" class="primary" @click="saveRule">保存规则</button></div><article v-for="rule in awardRules" :key="rule.id" :class="{ inactive: !rule.active }"><div><b>{{ arts.find(art => art.id === rule.alternateArtId)?.displayName || rule.alternateArtId }}</b><span>{{ rule.kind === 'rank-reached' ? `赛季达段 · ${rule.seasonId} · 第 ${rule.minimumTierIndex + 1} 档及以上` : rule.kind === 'season-final' ? `赛季结算 · ${rule.seasonId} · 第 ${rule.minimumTierIndex + 1} 档及以上` : rule.kind === 'master-champion-season-final' ? `赛季最强主宰 · ${rule.seasonId} · ${rule.masterId || '全部主宰'}` : `活动派发 · ${rule.eventId}` }}</span><small>{{ rule.active ? '已启用' : '已停用' }}</small></div><button @click="editRule(rule)">编辑</button></article><p v-if="!awardRules.length">尚未设置自动派发规则。</p></section>
    <section class="event-dispatch"><h4>赛季排位参与者派发</h4><p>选择异画后先预览。默认只覆盖本赛季参与过排位的玩家；也可明确填写一个历史赛季。确认后同一异画、同一赛季只会派发一次。</p><div class="form-grid"><label>异画<button class="art-choice" type="button" @click="openArtPicker('participants')">{{ selectedParticipantArt ? `${selectedParticipantArt.artCode} · ${selectedParticipantArt.displayName}` : '打开异画卡查选择' }}</button></label><label>派发范围<select v-model="rankedParticipantScope" @change="betaGrantPreview = null"><option value="current">本赛季参与过排位的玩家</option><option value="specified">指定赛季参与过排位的玩家</option></select></label><label v-if="rankedParticipantScope === 'specified'">指定赛季<input v-model.trim="betaGrantForm.seasonId" maxlength="100" placeholder="例如：S01" @input="betaGrantPreview = null"></label></div><p v-if="betaGrantPreview">{{ betaGrantPreview.seasonId }} 赛季可参与 {{ betaGrantPreview.eligibleAccounts }} 人；已拥有同来源权益 {{ betaGrantPreview.alreadyGranted }} 人；本次新增 {{ betaGrantPreview.toGrant }} 人。</p><div class="actions"><button @click="previewBetaGrant">预览派发范围</button><button v-if="betaGrantPreview && hasPermission('admin.content.draft')" class="primary" @click="dispatchBetaGrant">确认派发</button></div></section>
    <section v-if="awardRules.some(rule => rule.active && rule.kind === 'event')" class="event-dispatch"><h4>执行活动名单</h4><p>逐名从统一账号候选加入获奖玩家；重名账号以唯一玩家ID区分，系统提交前仍会校验账号状态。</p><AdminAccountPicker v-model="eventAccountId" label="加入获奖玩家" @change="addEventAccount"/><div class="event-accounts"><p v-if="!eventAccounts.length">尚未加入获奖玩家。</p><button v-for="account in eventAccounts" :key="account.id" type="button" @click="removeEventAccount(account.id)"><b>{{ account.username }}</b><code>{{ account.id }}</code><span>移除</span></button></div><div v-for="rule in awardRules.filter(rule => rule.active && rule.kind === 'event')" :key="rule.id" class="event-rule"><span>{{ arts.find(art => art.id === rule.alternateArtId)?.displayName || rule.alternateArtId }} · {{ rule.eventId }}</span><button v-if="hasPermission('admin.content.draft')" class="primary" @click="dispatchEvent(rule)">按此规则派发</button></div></section>
    <section class="grant-list"><h4>派发记录</h4><article v-for="grantRow in grants" :key="grantRow.id" :class="{ revoked: grantRow.revokedAt }"><div><b>{{ grantRow.username }}</b><span>{{ arts.find(art => art.id === grantRow.alternateArtId)?.displayName || grantRow.alternateArtId }}</span><small>{{ grantRow.sourceKind }}{{ grantRow.sourceReference ? ` · ${grantRow.sourceReference}` : '' }} · {{ new Date(grantRow.grantedAt).toLocaleString() }}</small></div><button v-if="!grantRow.revokedAt && hasPermission('admin.content.draft')" class="danger" @click="revoke(grantRow)">撤回</button><em v-else-if="grantRow.revokedAt">已撤回</em></article><p v-if="!grants.length">暂无派发记录。</p></section>
    </div>
    <Teleport to="body">
      <div v-if="imageDialogOpen && imageTarget" class="registry-mask" @click.self="closeImageEditor">
        <section class="image-dialog ui-state-scope" role="dialog" aria-modal="true" aria-labelledby="alternate-art-image-title"><header><div><small>REPLACE ARTWORK</small><h2 id="alternate-art-image-title">更换卡图</h2><p>{{ imageTarget.artCode }} · {{ imageTarget.displayName }}</p></div><button aria-label="关闭" @click="closeImageEditor">×</button></header><div class="image-compare"><figure><figcaption>当前卡图</figcaption><CardImage v-if="imageTarget.builtIn" :card-id="imageTarget.cardImageId" :legacy-url="imageTarget.imageUrl" :alt="imageTarget.displayName" intent="detail"/><img v-else :src="imageTarget.imageUrl" :alt="imageTarget.displayName"></figure><figure><figcaption>待替换卡图</figcaption><img v-if="replacementMedia" :src="replacementMedia.desktopUrl" :alt="replacementMedia.altText"><div v-else class="empty-preview">上传或选择素材后在此预览</div></figure></div><label>已上传卡图<SiteMediaPicker v-model="imageMediaAssetId" kind="card-art" :selected-media="replacementMedia" @selected="replacementUploaded"/></label><MediaUploadField v-if="canWrite" kind="card-art" :initial-alt="`${imageTarget.displayName} 新卡图`" @uploaded="replacementUploaded" @notice="notice"/><p class="identity-note">保存只会替换图片。卡牌身份、编号、卡效、产品、罕贵、构筑与权益记录不会改变。</p><footer><button v-if="imageTarget.builtIn && imageTarget.mediaAssetId" class="danger" @click="restoreCatalogImage">恢复系统卡图</button><span></span><button @click="closeImageEditor">取消</button><button v-if="canWrite" class="primary" :disabled="!imageMediaAssetId" @click="saveReplacementImage">确认更换</button></footer></section>
      </div>
      <div v-if="productDialogOpen" class="registry-mask" @click.self="productDialogOpen = false">
        <section class="product-dialog ui-state-scope" role="dialog" aria-modal="true" aria-labelledby="alternate-art-product-title"><header><div><small>PRODUCT DRAFT</small><h2 id="alternate-art-product-title">新建产品</h2></div><button aria-label="关闭" @click="productDialogOpen = false">×</button></header><p>在现有产品体系中创建草稿并立即选用。草稿仅后台可见，不会自动发布资讯或公开画廊归属。</p><label>产品名称<input v-model.trim="productForm.title" maxlength="160" placeholder="例如：联动纪念卡系列"></label><label>产品分类<select v-model="productForm.categoryId"><option v-for="item in productCategories" :key="item.id" :value="item.id">{{ item.name }}</option></select></label><p v-if="pendingProductId" class="draft-state">草稿已创建：{{ pendingProductId }}。如果列表刷新失败，只需重试回选，不会重复创建。</p><footer><button @click="productDialogOpen = false">取消</button><button v-if="canWrite" class="primary" :disabled="productSaving" @click="saveProductDraft">{{ pendingProductId ? '重试回选已建草稿' : productSaving ? '保存中…' : '保存草稿并选用' }}</button></footer></section>
      </div>
      <div v-if="productFilterDialogOpen" class="registry-mask" @click.self="productFilterDialogOpen = false">
        <section class="product-dialog product-filter-dialog ui-state-scope" role="dialog" aria-modal="true" aria-labelledby="gallery-product-filter-title"><header><div><small>GALLERY FILTERS</small><h2 id="gallery-product-filter-title">管理收录产品</h2></div><button aria-label="关闭" @click="productFilterDialogOpen = false">×</button></header><p>发布顺序数字越小越靠前。停用或隐藏筛选不会删除卡片归属；改名仅更新画廊标签。</p><form class="managed-product-form" @submit.prevent="saveManagedProduct"><label>产品名称<input v-model.trim="managedProductForm.name" maxlength="100" :disabled="managedProductSaving" required></label><label class="inline-check"><input v-model="managedProductForm.active" type="checkbox" :disabled="managedProductSaving">启用</label><button type="submit" :disabled="!canWrite || managedProductSaving">{{ managedProductSaving ? '保存中…' : managedProductForm.id ? '保存产品' : '新建产品' }}</button><button v-if="managedProductForm.id" type="button" :disabled="managedProductSaving" @click="editManagedProduct()">取消编辑</button></form><div class="product-filter-list"><article v-for="item in products" :key="item.id" class="product-settings-row"><label class="product-filter-toggle"><input type="checkbox" :checked="productFilterChecked(item)" :disabled="!canWrite || productFilterPending[item.id] !== undefined" @change="onProductFilterChange(item, $event)"><span><b>{{ item.name }}</b><small>{{ item.categoryName || '未分类' }} · {{ item.source === 'site' ? item.status === 'published' ? '已发布' : '草稿' : item.source === 'catalog' ? '卡牌产品' : '自定义产品' }}</small><small>{{ item.active ? '已启用' : '已停用' }}</small></span></label><button type="button" :disabled="!canWrite || managedProductSaving" @click="editManagedProduct(item)">编辑产品</button><div class="product-order-controls"><label>发布顺序<input :value="productOrderDrafts[item.id] ?? item.releaseOrder ?? 0" type="number" min="0" max="100000" step="1" :disabled="!canWrite || productFilterPending[item.id] !== undefined" @input="productOrderDrafts[item.id] = Number(($event.target as HTMLInputElement).value)"></label><button type="button" :disabled="!canWrite || productFilterPending[item.id] !== undefined" @click="saveProductOrder(item)">保存顺序</button></div></article></div><footer><button @click="productFilterDialogOpen = false">完成</button></footer></section>
      </div>
      <div v-if="productMergeDialogOpen" class="registry-mask" @click.self="productMergeDialogOpen = false">
        <section class="product-dialog product-merge-dialog ui-state-scope" role="dialog" aria-modal="true" aria-labelledby="alternate-art-product-merge-title"><header><div><small>MERGE PRODUCTS</small><h2 id="alternate-art-product-merge-title">合并重复产品</h2></div><button aria-label="关闭" @click="productMergeDialogOpen = false">×</button></header><p>保留一个正式产品，系统会迁移全部异画归属，并归档或隐藏重复来源。</p><label>保留产品<select v-model="productMergeForm.targetProductId"><option value="">请选择最终保留的产品</option><option v-for="item in products.filter(product => product.active)" :key="item.id" :value="item.id">{{ item.name }} · {{ item.categoryName || '未分类' }} · {{ item.source === 'site' ? item.status === 'published' ? '已发布' : '草稿' : item.source === 'catalog' ? '卡牌产品' : '自定义产品' }}</option></select></label><fieldset class="merge-sources"><legend>合并这些重复来源</legend><label v-for="item in products.filter(product => product.id !== productMergeForm.targetProductId)" :key="item.id"><input v-model="productMergeForm.sourceProductIds" type="checkbox" :value="item.id"><span><b>{{ item.name }}</b><small>{{ item.categoryName || '未分类' }} · {{ item.source === 'site' ? item.status === 'published' ? '已发布' : '草稿' : item.source === 'catalog' ? '卡牌产品' : '自定义产品' }}</small></span></label></fieldset><footer><button @click="productMergeDialogOpen = false">取消</button><button v-if="canWrite" class="primary" :disabled="!productMergeForm.targetProductId || !productMergeForm.sourceProductIds.length" @click="mergeProducts">核对并合并</button></footer></section>
      </div>
      <SingleCardPicker v-if="artPickerTarget" :title="artPickerTarget === 'base' ? '选择异画绑定的原卡' : '选择异画'" :items="artPickerTarget === 'base' ? basePickerItems : artPickerItems" @select="choosePickerItem" @close="artPickerTarget = null"/>
    </Teleport>
  </section>
</template>

<style scoped>
.shared-gallery-source{margin-bottom:16px}.shared-gallery-source>div{min-width:0}.shared-gallery-source>a{display:flex;align-items:center;justify-content:center;box-sizing:border-box;min-height:44px;flex-shrink:0;padding:10px 14px;border:1px solid #8b773c;background:#221d11;color:#efd27b;font-weight:900;text-align:center;text-decoration:none}.shared-gallery-source>a:focus-visible{outline:2px solid #60d2d9;outline-offset:3px}
.alternate-admin{padding:22px;border:1px solid #35424a;background:#0e161d}.alternate-admin>header{display:flex;justify-content:space-between;gap:18px;margin:-22px -22px 20px;padding:22px;border-bottom:1px solid #35424a;background:#101821}.alternate-admin h3,.alternate-admin h4{margin:5px 0 10px}.alternate-admin p,.alternate-admin span,.alternate-admin small{color:#93a0a6;font-size:14px;line-height:1.65}.alternate-admin small{color:#56c4cc;font-family:monospace;letter-spacing:.08em}.alternate-admin button,.alternate-admin input,.alternate-admin select{box-sizing:border-box;min-height:38px;padding:8px 10px;border:1px solid #4a5860;background:#070d12;color:#fff;font-size:14px}.art-editor,.art-list,.grant-panel,.grant-list,.product-editor{margin-top:16px;padding:16px;border:1px solid #35424a;background:#091016}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}.form-grid label{display:grid;gap:5px;color:#c4ccce;font-size:13px;font-weight:800}.form-grid .check{display:flex;align-items:center;gap:8px}.actions{display:flex;justify-content:flex-end;gap:8px;margin-top:12px}.primary{border-color:#d8ba68!important;background:#d8ba68!important;color:#080b0d!important;font-weight:900}.art-list article,.grant-list article,.product-editor article{display:flex;align-items:center;gap:12px;margin-top:8px;padding:9px;border:1px solid #35424a;background:#101820}.art-list img,.art-list :deep(.l12-card-image){width:46px;height:64px;flex:0 0 46px;object-fit:cover}.art-list article div,.grant-list article div{display:grid;min-width:0;gap:3px;flex:1}.art-list button,.grant-list button,.product-editor button{margin-left:auto}.grant-panel>p{margin-top:-4px}.grant-list article.revoked{opacity:.58}.danger{border-color:#81434c!important;background:#281117!important;color:#eea6ae!important}@media(max-width:760px){.alternate-admin>header{align-items:flex-start;flex-direction:column}.form-grid{grid-template-columns:1fr}}
.award-rules,.event-dispatch{margin-top:16px;padding:16px;border:1px solid #35424a;background:#091016}.award-rules article{display:flex;align-items:center;gap:12px;margin-top:8px;padding:9px;border:1px solid #35424a;background:#101820}.award-rules article div{display:grid;min-width:0;gap:3px;flex:1}.award-rules article.inactive{opacity:.58}.event-dispatch textarea{box-sizing:border-box;width:100%;margin:8px 0;padding:10px;border:1px solid #4a5860;background:#070d12;color:#fff;font:14px/1.5 ui-monospace,monospace}.event-rule{display:flex;align-items:center;justify-content:space-between;gap:10px;margin-top:8px;padding:9px;border:1px solid #35424a}.event-rule span{color:#c4ccce}.art-choice{text-align:left;font-weight:800}.art-picker-mask{position:fixed;z-index:3400;inset:0;display:grid;padding:36px;background:#020507dc;place-items:center}.art-picker{display:grid;width:min(1080px,96vw);height:min(780px,90vh);grid-template-rows:auto auto minmax(0,1fr);border:1px solid #66572b;background:#0b1116;color:#eff2ef;box-shadow:0 30px 100px #000}.art-picker>header{display:flex;align-items:center;justify-content:space-between;padding:15px 18px;border-bottom:1px solid #374147}.art-picker h2{margin:4px 0 0;font-size:22px}.art-picker small{color:#cfad43;font:900 12px monospace;letter-spacing:.08em}.art-picker>header button{border:0;background:transparent;color:#fff;font-size:28px}.art-picker>input{margin:12px;padding:10px;border:1px solid #445159;background:#070c10;color:#fff;font-weight:800}.art-picker-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:11px;overflow:auto;padding:14px;align-content:start}.art-picker-card{display:grid;gap:6px;padding:8px;border:1px solid #39464d;background:#111a21;color:#fff;text-align:left}.art-picker-card:hover{border-color:#e1bd50;box-shadow:0 0 14px #c598383d}.art-picker-card img{width:100%;aspect-ratio:5/7;object-fit:cover;background:#050708}.art-picker-card b,.art-picker-card small,.art-picker-card span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.art-picker-card span{color:#93a0a6;font-size:12px}@media(max-width:760px){.art-picker-mask{padding:8px}.art-picker{width:100%;height:96vh}.art-picker-grid{grid-template-columns:repeat(auto-fill,minmax(110px,1fr))}}
.event-accounts{display:grid;gap:6px;margin:10px 0}.event-accounts>button{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:3px;text-align:left}.event-accounts>button code{overflow-wrap:anywhere;color:#7bcbd0}.event-accounts>button span{grid-column:2;grid-row:1/3;align-self:center;color:#e8b0b6}
.art-registry-entry{display:flex;align-items:center;justify-content:space-between;gap:18px}.art-registry-entry h4,.art-registry-entry p{margin:0}.registry-mask{position:fixed;z-index:3500;inset:0;display:grid;place-items:center;padding:32px;background:#020507df}.registry-modal{display:grid;width:min(1040px,94vw);height:min(760px,90vh);grid-template-rows:auto auto minmax(0,1fr) auto;border:1px solid #66572b;background:#0b1116;color:#eff2ef;box-shadow:0 30px 100px #000}.registry-modal>header,.registry-modal>footer{display:flex;align-items:center;justify-content:space-between;gap:14px;padding:14px 18px;border-bottom:1px solid #374147}.registry-modal>header h2{margin:4px 0 0}.registry-modal>header small{color:#cfad43;font:900 12px monospace;letter-spacing:.08em}.registry-modal>header button{border:0;background:transparent;color:#fff;font-size:28px}.registry-filters{display:grid;grid-template-columns:repeat(3,minmax(0,1fr)) auto auto;align-items:end;gap:8px;padding:12px 18px;border-bottom:1px solid #283238}.registry-filters label{display:grid;gap:5px;color:#aeb8bb;font-size:13px;font-weight:900}.registry-filters input,.registry-filters button,.registry-modal>footer button{box-sizing:border-box;min-height:38px;padding:8px 10px;border:1px solid #4a5860;background:#070d12;color:#fff}.registry-results{overflow:auto;padding:12px 18px}.registry-results article{display:grid;grid-template-columns:46px minmax(0,1fr) auto;align-items:center;gap:12px;margin-bottom:8px;padding:9px;border:1px solid #35424a;background:#101820}.registry-results img,.registry-results :deep(.l12-card-image){width:46px;height:64px;object-fit:cover}.registry-results article>div{display:grid;gap:3px}.registry-results span,.registry-results small{color:#93a0a6;font-size:13px}.registry-results article>button{min-height:34px;padding:7px 10px;border:1px solid #4a5860;background:#070d12;color:#fff}.registry-modal>footer{border-top:1px solid #374147;border-bottom:0}.registry-modal>footer span{color:#93a0a6}.registry-modal>footer div{display:flex;gap:8px}.registry-modal button:disabled{cursor:not-allowed;opacity:.42}@media(max-width:760px){.art-registry-entry{align-items:stretch;flex-direction:column}.registry-mask{padding:8px}.registry-modal{width:100%;height:96dvh}.registry-filters{grid-template-columns:1fr 1fr}.registry-filters label:first-child{grid-column:1/-1}.registry-results{padding:9px}.registry-results article{grid-template-columns:38px minmax(0,1fr)}.registry-results img,.registry-results :deep(.l12-card-image){width:38px;height:54px}.registry-results article>button{grid-column:1/-1}.registry-modal>footer{align-items:flex-start;flex-direction:column}.registry-modal>footer div,.registry-modal>footer button{width:100%}}
.alternate-admin,.alternate-admin>*,.product-editor,.art-editor,.grant-panel,.award-rules,.event-dispatch,.form-grid,.form-grid>*{box-sizing:border-box;min-width:0;max-width:100%}.alternate-admin button,.alternate-admin input,.alternate-admin select,.alternate-admin textarea{max-width:100%;min-width:0;white-space:normal}@media(max-width:380px){.alternate-admin{padding:14px}.alternate-admin>header{margin:-14px -14px 14px;padding:14px}.product-editor,.art-editor,.grant-panel,.art-list,.grant-list,.award-rules,.event-dispatch{padding:10px}}
.full-field,.metadata-choice{grid-column:1/-1}.metadata-choice{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:7px;padding:12px;border:1px solid #35424a}.metadata-choice legend{padding:0 6px;color:#d9c176;font-weight:900}.metadata-choice .check{justify-content:flex-start;padding:7px;background:#101820}.metadata-choice small{margin-left:auto}.product-groups{display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:6px;max-height:220px;overflow:auto}.product-groups article{margin:0}.form-grid textarea{width:100%;resize:vertical}@media(max-width:760px){.full-field,.metadata-choice{grid-column:auto}}
.workspace-tabs{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:1px;margin-bottom:16px;border:1px solid #35424a;background:#35424a}.workspace-tabs button{border:0;background:#0a1117;color:#aab4b7;font-weight:900}.workspace-tabs button.active{background:#273039;color:#f0ce67;box-shadow:inset 0 -3px #d7b64c}.task-strip{display:flex;align-items:center;justify-content:space-between;gap:18px;padding:18px;border:1px solid #5b4e2c;background:linear-gradient(110deg,#15170f,#0a1218 58%)}.task-strip h4,.task-strip p{margin:2px 0}.gallery-filters{display:grid;grid-template-columns:2fr repeat(6,minmax(120px,1fr));gap:8px;margin-top:14px;padding:14px;border:1px solid #35424a;background:#091016}.gallery-filters label,.image-dialog>label,.product-dialog>label{display:grid;gap:5px;color:#c4ccce;font-size:13px;font-weight:800}.filter-actions{display:flex;grid-column:1/-1;justify-content:flex-end;gap:8px}.gallery-list{margin-top:14px;border:1px solid #35424a;background:#091016}.gallery-list>header,.gallery-list>footer{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:14px 16px;border-bottom:1px solid #35424a}.gallery-list>footer{border-top:1px solid #35424a;border-bottom:0}.gallery-list>footer>div{display:flex;gap:8px}.gallery-list article{display:grid;grid-template-columns:64px minmax(0,1fr) auto;align-items:center;gap:14px;padding:12px 16px;border-bottom:1px solid #29343a}.gallery-list article>img,.gallery-list article>:deep(.l12-card-image){width:64px;height:90px;object-fit:cover}.art-summary{display:grid;gap:5px}.art-summary>b{font-size:17px}.chips{display:flex;flex-wrap:wrap;gap:5px}.chips em{display:inline-flex;gap:4px;padding:3px 7px;border:1px solid #4a555a;background:#111a20;color:#cdd4d5;font-size:12px;font-style:normal}.chips small{padding-left:4px;border-left:1px solid #776a3d;color:#e2c360;font-family:inherit;letter-spacing:0}.row-actions{display:flex;gap:8px}.editor-heading,.image-dialog>header,.product-dialog>header{display:flex;align-items:center;justify-content:space-between;gap:14px}.editor-heading h4,.image-dialog h2,.product-dialog h2{margin:3px 0}.inline-manager{grid-column:1/-1;margin-top:14px;padding:14px;border:1px solid #35424a;background:#0e171e}.inline-manager article{display:flex;align-items:center;gap:10px;margin-top:7px;padding:8px;border:1px solid #2f3a40}.inline-manager article button{margin-left:auto}.image-dialog,.product-dialog{width:min(900px,94vw);max-height:92vh;overflow:auto;padding:18px;border:1px solid #66572b;background:#0b1116;color:#eff2ef;box-shadow:0 30px 100px #000}.product-dialog{width:min(560px,94vw)}.image-dialog>header,.product-dialog>header{margin:-18px -18px 18px;padding:16px 18px;border-bottom:1px solid #374147}.image-dialog>header button,.product-dialog>header button{border:0;background:transparent;color:#fff;font-size:28px}.image-compare{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px;margin-bottom:16px}.image-compare figure{display:grid;justify-items:center;gap:8px;margin:0;padding:12px;border:1px solid #35424a;background:#070d12}.image-compare figcaption{justify-self:start;color:#cdd4d5;font-weight:900}.image-compare img,.image-compare :deep(.l12-card-image),.empty-preview{width:min(250px,100%);aspect-ratio:5/7;object-fit:contain;background:#020405}.empty-preview{display:grid;align-items:center;padding:15px;color:#78868c;text-align:center}.identity-note,.draft-state{padding:10px;border-left:3px solid #d1b34e;background:#17160e}.image-dialog>footer,.product-dialog>footer{display:flex;align-items:center;justify-content:flex-end;gap:8px;margin-top:16px}.image-dialog>footer>span{flex:1}.product-dialog>label{margin-top:12px}.rights-workspace>.grant-panel:first-child{margin-top:0}
@media(max-width:1050px){.gallery-filters{grid-template-columns:repeat(3,minmax(0,1fr))}.gallery-filters label:first-child{grid-column:span 2}}
@media(max-width:760px){.task-strip,.gallery-list>header,.gallery-list>footer{align-items:stretch;flex-direction:column}.gallery-filters{grid-template-columns:1fr}.gallery-filters label:first-child{grid-column:auto}.gallery-list article{grid-template-columns:52px minmax(0,1fr);padding:10px}.gallery-list article>img,.gallery-list article>:deep(.l12-card-image){width:52px;height:74px}.row-actions{grid-column:1/-1}.row-actions button{flex:1}.image-compare{grid-template-columns:1fr}.image-compare img,.image-compare :deep(.l12-card-image),.empty-preview{width:min(210px,100%)}.image-dialog,.product-dialog{width:calc(100vw - 16px);max-height:96dvh;padding:14px}.image-dialog>header,.product-dialog>header{margin:-14px -14px 14px;padding:14px}.image-dialog>footer,.product-dialog>footer{align-items:stretch;flex-direction:column}.image-dialog>footer button,.product-dialog>footer button{width:100%}.workspace-tabs{position:sticky;z-index:3;top:0}}
.gallery-filters{grid-template-columns:repeat(3,minmax(120px,1fr))}.art-editor{position:fixed;z-index:3300;inset:4vh max(16px,5vw);overflow:auto;margin:0;padding:20px;border-color:#66572b;box-shadow:0 30px 100px #000}.art-editor>.editor-heading{position:sticky;z-index:2;top:-20px;margin:-20px -20px 18px;padding:16px 20px;border-bottom:1px solid #374147;background:#0b1116}@media(max-width:760px){.gallery-filters{grid-template-columns:1fr}.art-editor{inset:8px;padding:14px}.art-editor>.editor-heading{top:-14px;margin:-14px -14px 14px;padding:14px}}
/* Teleported dialogs are outside .alternate-admin and need their own control baseline. */
.image-dialog>footer>button,.product-dialog>footer>button,.product-dialog>label>input,.product-dialog>label>select{box-sizing:border-box;min-height:38px;padding:8px 10px;border:1px solid #4a5860;background:#070d12;color:#fff;font:inherit}
@media(max-width:900px),(pointer:coarse){.image-dialog>header>button,.product-dialog>header>button{box-sizing:border-box;min-width:44px;min-height:44px;flex:0 0 44px;padding:0}.image-dialog>footer>button,.product-dialog>footer>button,.product-dialog>label>input,.product-dialog>label>select{min-height:44px}.product-dialog>label>select{height:44px}}
.task-actions{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:8px}.merge-sources{display:grid;gap:7px;max-height:320px;overflow:auto;margin:14px 0 0;padding:12px;border:1px solid #35424a}.merge-sources legend{padding:0 6px;color:#d9c176;font-weight:900}.merge-sources label{display:flex;align-items:center;gap:9px;padding:8px;background:#101820}.merge-sources label span{display:grid;gap:2px}.merge-sources label small{color:#93a0a6}.product-merge-dialog{width:min(720px,94vw)}
.product-filter-dialog{width:min(720px,94vw)}.product-filter-list{display:grid;gap:7px;max-height:min(58vh,520px);overflow:auto;margin-top:14px}.product-filter-list label{display:flex;align-items:center;gap:10px;padding:10px;border:1px solid #35424a;background:#101820;color:#e2e7e7}.product-filter-list label>span{display:grid;gap:2px}.product-filter-list small{color:#93a0a6}
@media(max-width:760px){.task-actions{width:100%;justify-content:stretch}.task-actions button{flex:1}.merge-sources label{align-items:flex-start}.product-merge-dialog,.product-filter-dialog{width:calc(100vw - 16px)}}
.gallery-list .gallery-card-open{display:block;min-width:0;padding:0;border:0;background:transparent;cursor:zoom-in}.gallery-card-open:focus-visible{outline:2px solid #54c7cf;outline-offset:3px}.product-settings-row{display:flex;align-items:center;justify-content:space-between;gap:16px;padding:10px;border-bottom:1px solid #35424a}.product-filter-toggle{flex:1;min-width:0}.product-order-controls{display:flex;align-items:end;gap:8px}.product-order-controls label{display:grid;gap:4px}.product-order-controls input{box-sizing:border-box;width:100px;min-height:38px}.product-order-controls button{min-height:38px}@media(max-width:680px){.product-settings-row{align-items:stretch;flex-direction:column}.product-order-controls input{min-height:44px}.product-order-controls button{min-height:44px}}
.managed-product-form{display:flex;flex-wrap:wrap;align-items:end;gap:10px;margin:16px 0}.managed-product-form>label:first-child{flex:1;min-width:160px}.managed-product-form .inline-check{display:flex;align-items:center;gap:6px}.managed-product-form input:not([type=checkbox]){width:100%;min-height:38px}.managed-product-form button{min-height:38px}
.managed-product-form input:not([type=checkbox]),.product-order-controls input{box-sizing:border-box;border:1px solid #52616a;background:#101820;color:#edf1ef;padding:8px;font:inherit}.managed-product-form button,.product-settings-row>button,.product-order-controls button{border:1px solid #52616a;background:#18252c;color:#edf1ef;padding:8px 12px;cursor:pointer;font:inherit}.managed-product-form button:disabled,.product-settings-row button:disabled{opacity:.5;cursor:wait}.managed-product-form :focus-visible,.product-settings-row :focus-visible{outline:2px solid #64d7d0;outline-offset:2px}
</style>
