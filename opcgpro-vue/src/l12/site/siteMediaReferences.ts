import { adminApi, type SiteMedia } from '@/l12/platform'

// Resolve only explicit persisted bindings, never scan or silently substitute a
// recent asset. Keep missing/failed references visible to the editor as a failure.
export async function loadSiteMediaReferences(ids: readonly string[]): Promise<SiteMedia[]> {
  const unique = [...new Set(ids.filter(Boolean))]
  if (unique.length > 1000 || unique.some(id => typeof id !== 'string' || id.length > 100 || id.includes(',')))
    throw new Error('素材引用无法确认，请检查绑定编号')
  const found = new Map<string, SiteMedia>()
  for (let offset = 0; offset < unique.length; offset += 100) {
    const group = unique.slice(offset, offset + 100)
    const expected = new Set(group)
    const result = await adminApi.searchSiteMedia({ ids: group, pageSize: 100 })
    if (!Array.isArray(result.items) || result.items.length > group.length) throw new Error('素材返回内容无法确认')
    for (const item of result.items) {
      if (!expected.delete(item.id) || found.has(item.id)) throw new Error('素材返回内容无法确认')
      found.set(item.id, item)
    }
    if (expected.size) throw new Error('部分已绑定素材无法读取，请重试')
  }
  return unique.map(id => found.get(id)!)
}
