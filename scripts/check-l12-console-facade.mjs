#!/usr/bin/env node
// C4b 守卫：服务端除日志门面 L12Log.cs 自身外，不得直接写 Console。
// 结构化日志（L12Log）在未注入 ILoggerFactory 时仍回落 Console，行为不变；
// 这条守卫防止新增代码绕过门面，把可观测性退回散装 Console。
import fs from 'node:fs'
import path from 'node:path'

const repoRoot = path.resolve(import.meta.dirname, '..')
const serverRoot = path.join(repoRoot, '服务端WebSocket')
const facade = 'L12Log.cs'
const pattern = /\bConsole\.(Write|WriteLine|Error)\b/g
const failures = []
let scanned = 0

function walk(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name)
    if (entry.isDirectory()) {
      if (entry.name === 'bin' || entry.name === 'obj') continue
      walk(full)
    } else if (entry.name.endsWith('.cs') && entry.name !== facade) {
      scanned += 1
      const text = fs.readFileSync(full, 'utf8')
      const lines = text.split(/\r?\n/)
      lines.forEach((line, index) => {
        if (pattern.test(line)) {
          failures.push(path.relative(repoRoot, full) + ':' + (index + 1) + '  ' + line.trim().slice(0, 120))
        }
        pattern.lastIndex = 0
      })
    }
  }
}

if (!fs.existsSync(serverRoot)) {
  console.error('L12 console facade guard: server source directory not found: ' + serverRoot)
  process.exit(1)
}
walk(serverRoot)

if (failures.length > 0) {
  console.error('L12 console facade guard failed: ' + failures.length + ' direct Console write(s) outside ' + facade)
  for (const failure of failures) console.error('  ' + failure)
  process.exit(1)
}
console.log('L12 console facade guard passed: ' + scanned + ' files use L12Log only.')
