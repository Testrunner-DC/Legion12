import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
const read = name => fs.readFileSync(new URL('../src/l12/'+name,import.meta.url),'utf8')
const source=read('site/maintenanceCountdown.ts')
const js=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.ESNext,target:ts.ScriptTarget.ES2022}}).outputText
const {maintenanceCountdown}=await import('data:text/javascript;base64,'+Buffer.from(js).toString('base64'))
const immediate={enabled:true,immediateActive:true,immediateExpectedDurationHours:10,message:'预约文案',broadcastMessage:'当前服务器维护中，预计维护时间为10小时。',startsAt:'2099-01-01',endsAt:'2000-01-01'}
const view=maintenanceCountdown(immediate,Date.parse('2026-09-07'))
assert.equal(view.phase,'active')
assert.equal(view.message,immediate.broadcastMessage)
assert(view.countdown.includes('进行中对局可继续'))
assert.equal(maintenanceCountdown({...immediate,enabled:false,immediateActive:false}),null)
const shell=read('site/SiteShell.vue'),panel=read('site/ImmediateMaintenancePanel.vue'),ticker=read('site/MaintenanceTicker.vue'),admin=read('site/AdminOperationsPanel.vue'),api=read('platform.ts')
assert(shell.includes('<MaintenanceTicker v-if="route.meta.section === \'battle\'"/>'),'Ticker must cover shared battle subpages')
assert(admin.includes('<ImmediateMaintenancePanel v-if="activeSection === \'maintenance\'"/>'))
assert(admin.includes('result.current.immediateMaintenance?.enabled')&&admin.includes('即时维护仍在生效，新对局尚未开放'))
assert(!admin.includes('服务器当前已开放')&&admin.includes('启动服务器（解除预约维护）'),'Scheduled-only action must not claim it ends immediate maintenance')
assert(panel.includes('Number.isInteger(hours.value)')&&panel.includes('hours.value >= 1')&&panel.includes('hours.value <= 168'))
assert(panel.includes('if (busy.value || !current.value')&&panel.includes('current.value.version'))
assert(panel.includes('current.value?.immediateMaintenance !== undefined'),'Old backend must disable immediate commands')
assert(!panel.includes('applyOperationsConfig')&&!panel.includes('serialize()'),'Dedicated operation must not write scheduled form edits')
assert(api.includes('/api/admin/operations/server/maintenance/end')&&api.includes('/api/admin/operations/server/maintenance'))
assert(ticker.includes('policy.version >=')&&ticker.includes('getEffectiveOperationsPolicy()')&&ticker.includes('5000'))
assert(ticker.includes('clearInterval(timer)')&&ticker.includes("removeEventListener('visibilitychange'"))
assert(ticker.includes('prefers-reduced-motion:reduce')&&ticker.includes('aria-hidden="true"'))
console.log('Immediate maintenance UI guards passed: manual-vs-scheduled rendering, dedicated commands, old backend safety, shared polling and cleanup.')
