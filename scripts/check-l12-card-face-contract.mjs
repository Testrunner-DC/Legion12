import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

// 卡面投影契约（2026-11-26 清单P6 + 清单P5 结构守卫）：
// 1) 对战界面 opcgpro-vue/src/l12/game/** 只能使用桌面变体；thumbnailUrl 是列表/缩略图字段，
//    出现在对局界面会让卡面变成低清缩略（2026-11-25 起是事实，这里把它固化成约定）。
// 2) 服务端重建「效果来源快照」时必须走 CreatePlayerCard；裸 ?? CreateCard(x.SourceCardId,
//    x.SourceInstanceId) 会丢掉玩家异画（清单P5 已把全池同型迁移成 CreatePlayerCard）。
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const failures = []
const skippedDirectories = new Set(['node_modules', 'bin', 'obj', 'dist', 'dist-testrun', '.git'])

function walk(directory, extensions) {
  const absolute = join(root, directory)
  if (!existsSync(absolute)) return []
  const files = []
  for (const entry of readdirSync(absolute)) {
    if (skippedDirectories.has(entry)) continue
    const relative = join(directory, entry)
    const stats = statSync(join(root, relative))
    if (stats.isDirectory()) files.push(...walk(relative, extensions))
    else if (extensions.some(extension => entry.endsWith(extension))) files.push(relative)
  }
  return files
}

function scan(files, label, pattern) {
  for (const file of files) {
    const lines = readFileSync(join(root, file), 'utf8').replace(/\r\n/g, '\n').split('\n')
    lines.forEach((line, index) => {
      if (pattern.test(line)) failures.push(label + ': ' + file + ':' + (index + 1) + ': ' + line.trim().slice(0, 160))
    })
  }
}

scan(walk('opcgpro-vue/src/l12/game', ['.vue', '.ts']),
  '对战界面不得使用缩略图变体（thumbnailUrl）', /thumbnailUrl/)
scan(walk('服务端WebSocket', ['.cs']),
  '重建效果来源快照必须走 CreatePlayerCard（否则丢玩家异画）',
  /\?\?\s*CreateCard\(\s*[A-Za-z_]\w*\.SourceCardId\s*,\s*[A-Za-z_]\w*\.SourceInstanceId\s*\)/)

if (failures.length > 0) {
  console.error('L12 card face contract failed:')
  for (const failure of failures) console.error('  ' + failure)
  process.exit(1)
}
console.log('L12 card face contract passed: game UI uses desktop variants; source-snapshot rebuilds resolve alternate art.')
