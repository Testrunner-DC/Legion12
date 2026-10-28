export type SiteContentKind = 'news' | 'video' | 'product'
export type SiteMediaKind = 'hero' | 'article' | 'rule' | 'card-art' | SiteContentKind
export interface SiteMediaPolicy {
  kind: SiteMediaKind; label: string; desktopWidth: number; desktopHeight: number
  mobileWidth: number; mobileHeight: number; thumbnailWidth: number; thumbnailHeight: number
  safeArea: string; acceptedOriginalFormats: string[]; flexibleDimensions: boolean
}
export interface SiteMedia {
  id: string; kind: SiteMediaKind; altText: string; focalX: number; focalY: number
  originalFormat: string; contentHash: string; desktopUrl: string; mobileUrl: string; thumbnailUrl: string
  desktopWidth: number; desktopHeight: number; mobileWidth: number; mobileHeight: number
  thumbnailWidth: number; thumbnailHeight: number; originalBytes: number; deliveryBytes: number
  createdBy: string; createdAt: string; referenceCount: number
  desktopAltText: string; mobileAltText: string; thumbnailAltText: string; independentVariants: boolean
}
export interface SiteMediaSearch { items: SiteMedia[]; page: number; pageSize: number; total: number; hasMore: boolean }
export interface SiteMediaEmbed {
  id: string; altText: string; desktopUrl: string; mobileUrl: string; thumbnailUrl: string
  desktopWidth: number; desktopHeight: number; mobileWidth: number; mobileHeight: number
}
export interface SiteCategory {
  id: string; kind: SiteContentKind; name: string; slug: string; sortOrder: number
  active: boolean; version: number; itemCount: number
}
export type ArticleStatus = 'draft' | 'published' | 'scheduled' | 'withdrawn' | 'archived'
export interface Article {
  id: string; title: string; summary: string; body: string; category: string; coverUrl: string; link: string; slug: string
  pinned: boolean; status: ArticleStatus; hasUnpublishedChanges: boolean; publishAt?: string
  createdAt: string; updatedAt: string; publishedAt?: string; author: string; updatedBy: string; publishedBy?: string; revision: number
  kind: SiteContentKind; categoryId?: string; mediaAssetId?: string; sortOrder: number; bodyMedia?: SiteMediaEmbed[]
  videoAuthorName?: string
}
export type ArticleSummary = Omit<Article, 'body' | 'bodyMedia'> & { bodyPreview: string }
export interface ArticleRevision {
  revision: number; action: string; title: string; summary: string; body: string; category: string; coverUrl: string
  link: string; slug: string; pinned: boolean; publishAt?: string; actor: string; createdAt: string
  kind: SiteContentKind; categoryId?: string; mediaAssetId?: string; sortOrder: number
}
export type ArticleDraft = Pick<Article, 'title' | 'summary' | 'body' | 'category' | 'coverUrl' | 'link' | 'slug' | 'pinned'> & {
  publishAt?: string; expectedRevision?: number; kind?: SiteContentKind; categoryId?: string
  mediaAssetId?: string; sortOrder?: number
}
export type ModianImportState = 'new' | 'imported' | 'local-edited' | 'remote-changed' | 'remote-changed-local-edited' | 'check-failed'
export interface ModianImportPreviewItem {
  updateId: string; sequence: number; title: string; originalPublishedAt: string; state: ModianImportState
  articleId?: string; articleStatus?: ArticleStatus; localDraftChanged: boolean; checkMessage?: string
}
export interface ModianImportPreview { projectId: string; projectName: string; total: number; items: ModianImportPreviewItem[] }
export interface ModianImportItemResult { updateId: string; status: string; message: string; articleId?: string; warnings?: string[] }
export interface ModianImportBatchResult {
  projectId: string; requested: number; created: number; updated: number; unchanged: number
  needsReimport: number; conflicted: number; failed: number; items: ModianImportItemResult[]
}
export interface SiteHomePayload {
  composition: string; legal: string; news: Article[]; videos: Article[]; products: Article[]; media: SiteMedia[]
}
export interface SiteHomeSummaryPayload {
  composition: string; legal: string; news: ArticleSummary[]; videos: ArticleSummary[]; products: ArticleSummary[]; media: SiteMediaEmbed[]
}
