import type { DeckCard } from '../decks'
import type { AlternateArt } from '../platformAlternateArtContracts'
import type { SingleCardPickerItem } from '../SingleCardPicker.vue'
import { cleanArticleAlternateArtId, type ArticleCardBlock } from './articleBlocks'

export type ArticleCardPickerItem = SingleCardPickerItem & { alternateArtId?: string }

// Only public gallery metadata supplies artwork. Never persist or accept an article URL.
export function articleCardArtwork(base: DeckCard, art: AlternateArt): DeckCard | null {
  if (!art.active || (art.kind && art.kind !== 'alternate-art') || art.baseCardId !== base.id
    || !cleanArticleAlternateArtId(art.id)) return null
  const media = /^\/api\/site\/media\/[A-Za-z0-9-]+\/(?:desktop|mobile|thumbnail)\/[A-Fa-f0-9]{64}\.webp$/.test(art.imageUrl || '') ? art.imageUrl : ''
  const imageId = art.builtIn && /^[A-Za-z0-9-]{1,100}$/.test(art.cardImageId || '') ? art.cardImageId : ''
  if (!media && !imageId) return null
  return { ...base, number: art.artCode || base.number, imageUrl: media || `l12-card-id:${imageId}`,
    archiveBaseCardId: base.id, products: art.products?.map(product => product.name) ?? base.products,
    product: art.products?.map(product => product.name).join(' / ') || art.productName || base.product,
    rarity: art.rarities?.map(rarity => rarity.name).join(' / ') || base.rarity }
}

export function articleCardPickerItems(catalog: DeckCard[], arts: AlternateArt[]): ArticleCardPickerItem[] {
  const byId = new Map(catalog.map(card => [card.id, card]))
  const item = (card: DeckCard): ArticleCardPickerItem => ({ id: card.id, cardId: card.id,
    number: card.number, name: card.nameZh, nameZh: card.nameZh, cardType: card.cardType,
    faction: card.faction, product: card.product, cost: card.cost, disasterLevel: card.disasterLevel,
    effect: card.effect, imageUrl: card.imageUrl, cardImageId: card.id, detailCard: card })
  return [...catalog.map(item), ...arts.flatMap(art => {
    const base = byId.get(art.baseCardId)
    const detail = base && articleCardArtwork(base, art)
    return detail ? [{ ...item(detail), id: `art:${art.id}`, cardId: base!.id,
      alternateArtId: art.id, subtitle: `异画 · ${art.displayName || base!.nameZh} · ${detail.product}` }] : []
  })]
}

export function articleCardFor(block: ArticleCardBlock, base: DeckCard | undefined, arts: AlternateArt[]) {
  if (!base || !block.alternateArtId) return base
  const art = arts.find(item => item.id === block.alternateArtId)
  return art ? articleCardArtwork(base, art) ?? undefined : undefined
}
