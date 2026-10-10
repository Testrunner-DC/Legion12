import type { OfficialL12PresetDeck } from '../decks'

export async function officialDeckId(deck: Pick<OfficialL12PresetDeck, 'masterId' | 'name'>) {
  if (!globalThis.crypto?.subtle) throw new Error('当前环境无法验证官方牌库稳定引用')
  const digest = await globalThis.crypto.subtle.digest('SHA-256', new TextEncoder().encode(deck.masterId + '\0' + deck.name))
  return 'official:' + Array.from(new Uint8Array(digest), value => value.toString(16).padStart(2, '0')).join('')
}

export async function resolveOfficialDeck(reference: string, load: () => Promise<OfficialL12PresetDeck[]>) {
  if (!/^official:(?:[a-f0-9]{64})$/.test(reference) && !/^official-\d+$/.test(reference))
    throw new Error('官方牌库稳定引用无效')
  const presets = await load()
  if (reference.startsWith('official-')) {
    const index = Number(reference.slice('official-'.length))
    if (!Number.isSafeInteger(index) || !presets[index]) throw new Error('未找到这个官方牌库')
    return presets[index]
  }
  const matched = []
  for (const preset of presets) if (await officialDeckId(preset) === reference) matched.push(preset)
  if (matched.length !== 1) throw new Error('官方牌库目录未匹配或存在重复身份，请刷新后重新打开')
  return matched[0]!
}
