import QRCode from 'qrcode'
import jsQR from 'jsqr'
import sharp from 'sharp'

const publicDeckUrls = [
  'https://legion-12.com/decks/a07fdfb1e7ab494b8fb225e85bdad2bb',
  'https://legion-12.com/decks/14ca76d2c09d41649ac01a68e8bc3f00',
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

console.log(`公开牌库二维码扫描通过：${publicDeckUrls.length}/2 个真实稳定链接`)
