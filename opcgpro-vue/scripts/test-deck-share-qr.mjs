import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import QRCode from 'qrcode'
import jsQR from 'jsqr'
import sharp from 'sharp'

const publicDeckUrls = [
  'https://legion-12.com/decks/7K4M9QX3ZP2R',
  'https://legion-12.com/decks/N8W5TCY7R3KA',
]

for (const url of publicDeckUrls) {
  const png = await QRCode.toBuffer(url, {
    type: 'png', errorCorrectionLevel: 'M', margin: 4, width: 190,
    color: { dark: '#050708', light: '#ffffff' },
  })
  const { data, info } = await sharp(png).ensureAlpha().raw().toBuffer({ resolveWithObject: true })
  const decoded = jsQR(new Uint8ClampedArray(data), info.width, info.height)
  if (decoded?.data !== url) throw new Error(`二维码无法还原公开牌库链接：${url}`)
}

console.log(`公开牌库二维码扫描通过：${publicDeckUrls.length}/2 个短码链接`)

// Load the extracted production codec; QR rendering still uses the existing libraries.
const codecSource = fs.readFileSync(new URL('../src/l12/deckCodeCodec.ts', import.meta.url), 'utf8')
const codecJavaScript = ts.transpileModule(codecSource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const codecModuleUrl = 'data:text/javascript;base64,' + Buffer.from(codecJavaScript).toString('base64')
const { encodeDeckCode, decodeDeckCode } = await import(codecModuleUrl)
const sharedDeck = { name: '二维码兼容', masterId: 'S01-01M1', cardIds: ['S01-0001', 'S01-0001', 'PROMO-X'],
  moraleIds: ['S01-01C1'], specialIds: [], updatedAt: '' }
const sharedCode = encodeDeckCode(sharedDeck)
const sharedPng = await QRCode.toBuffer(sharedCode, { type: 'png', errorCorrectionLevel: 'M', margin: 4, width: 640 })
const sharedPixels = await sharp(sharedPng).ensureAlpha().raw().toBuffer({ resolveWithObject: true })
const sharedDecoded = jsQR(new Uint8ClampedArray(sharedPixels.data), sharedPixels.info.width, sharedPixels.info.height)
assert.equal(sharedDecoded?.data, sharedCode, 'Extracted L12D2 share code must survive a real QR scan')
assert.equal(decodeDeckCode(sharedDecoded.data).name, sharedDeck.name)
assert.equal(decodeDeckCode(sharedDecoded.data).masterId, sharedDeck.masterId)
console.log('完整牌库码二维码扫描通过：抽离后的真实 L12D2 编码往返。')
