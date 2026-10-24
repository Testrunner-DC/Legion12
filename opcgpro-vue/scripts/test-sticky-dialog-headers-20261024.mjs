import assert from 'node:assert/strict'
import fs from 'node:fs'

const read = path => fs.readFileSync(new URL(`../${path}`, import.meta.url), 'utf8')
const ui = read('src/l12/site/uiSystem.css')
const shell = read('src/l12/site/SiteShell.vue')

assert.match(ui, /:where\(\[role='dialog'\]\[aria-modal='true'\],\[role='alertdialog'\]\[aria-modal='true'\]\)>header:first-child:has\(>button\)\{[\s\S]*?position:sticky;[\s\S]*?top:0;/)
assert.match(ui, /header:first-child:has\(>button\)\{[\s\S]*?--l12-sticky-dialog-background:rgb\(12,20,27\);[\s\S]*?background:var\(--l12-sticky-dialog-background\);/)
assert.match(ui, /header:first-child:has\(>button\)::before\{[\s\S]*?bottom:100%;[\s\S]*?height:32px;[\s\S]*?background:var\(--l12-sticky-dialog-background\);[\s\S]*?pointer-events:none;/)
assert.match(shell, /class="site-modal update-modal" role="dialog" aria-modal="true" aria-labelledby="site-update-modal-title"/)
assert.match(shell, /<h2 id="site-update-modal-title">更新日志<\/h2>/)
assert.match(shell, /<button type="button" aria-label="关闭更新日志" @click="modal = null">×<\/button>/)

const modalInventory = [
  ['src/l12/CatalogCardDetails.vue', 'catalog-detail-dialog'],
  ['src/l12/SingleCardPicker.vue', 'single-card-picker'],
  ['src/l12/L12DeckEditor.vue', 'mobile-saved-decks-dialog'],
  ['src/l12/game/BattleUtilityDock.vue', 'battle-dialog'],
  ['src/l12/site/GlobalBugFeedback.vue', 'bug-feedback-dialog'],
  ['src/l12/site/L12SettingsModal.vue', 'l12-settings-modal'],
  ['src/l12/site/DeckSnapshotViewer.vue', 'deck-viewer-modal'],
  ['src/l12/site/DeckLibraryPage.vue', 'import-modal'],
  ['src/l12/site/PublicDeckPicker.vue', 'public-deck-picker'],
  ['src/l12/site/SiteMediaPicker.vue', 'site-media-picker'],
]

for (const [path, className] of modalInventory) {
  const source = read(path)
  assert.match(source, new RegExp(`class="[^"]*${className}[^"]*"[^>]*role="(?:alert)?dialog"[^>]*aria-modal="true"`), `${path} must keep modal semantics`)
  assert.match(source, /<header>[\s\S]*?<button[^>]*(?:aria-label|title)=/, `${path} must keep a named header action`)
}

assert.doesNotMatch(read('src/l12/site/FriendChallengeNotifications.vue'), /aria-modal="true"/, 'non-blocking invitation cards are outside the sticky modal family')
assert.doesNotMatch(read('src/l12/site/SocialPopover.vue'), /aria-modal="true"/, 'non-modal social popover is outside the sticky modal family')

console.log(`滚动弹框固定标题合同通过：${modalInventory.length + 1} 个正式弹框入口，同步排除 2 个非模态浮层`)
