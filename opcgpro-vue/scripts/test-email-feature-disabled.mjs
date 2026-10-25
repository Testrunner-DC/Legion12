import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import { parse, compileScript, compileTemplate, compileStyle } from '@vue/compiler-sfc'

const read = name => fs.readFileSync(new URL('../src/l12/site/' + name, import.meta.url), 'utf8')
const profile = read('ProfilePage.vue')
const recovery = read('AccountRecoveryPage.vue')
let checks = 0
for (const [filename, source] of [['ProfilePage.vue', profile], ['AccountRecoveryPage.vue', recovery]]) {
  const { descriptor, errors } = parse(source, { filename })
  assert.deepEqual(errors, []); checks++
  const script = compileScript(descriptor, { id: 'email-retired' })
  assert.deepEqual(compileTemplate({ source: descriptor.template.content, filename, id: 'email-retired' }).errors, []); checks++
  for (const style of descriptor.styles) {
    assert.deepEqual(compileStyle({ source: style.content, filename, id: 'email-retired', scoped: true }).errors, []); checks++
  }
  if (filename === 'AccountRecoveryPage.vue') {
    const compiled = ts.transpileModule(script.content, {
      compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
    }).outputText
    for (const hash of ['', '#token=synthetic-old-token', '#unrecognized=synthetic']) {
      const calls = []
      const module = { exports: {} }
      const fakeRequire = name => {
        assert.equal(name, 'vue', 'retired recovery must not import platform/API/session modules')
        return { defineComponent: value => value, onMounted: callback => callback() }
      }
      new Function('require', 'module', 'exports', 'location', 'history', compiled)(
        fakeRequire, module, module.exports,
        { pathname: '/auth/recovery', search: '?mode=reset-password', hash },
        { replaceState: (...args) => calls.push(args) },
      )
      module.exports.default.setup({}, { expose() {} })
      assert.deepEqual(calls, hash ? [[null, '', '/auth/recovery?mode=reset-password']] : []); checks++
    }
  }
}
assert.doesNotMatch(profile, /emailApi|email-manager|emailFeatureEnabled|loadEmail|to="\/auth\/recovery"/); checks++
assert.match(profile, /changePassword\(auth.currentPassword, auth.newPassword\)/); checks++
assert.match(profile, /sessionApi\.revoke/); checks++
assert.doesNotMatch(profile, /\.session-row code p/); checks++
assert.doesNotMatch(recovery, /emailApi|logout|requestPasswordReset|resetPassword\(|<form|<input|<button/); checks++
assert.match(recovery, /邮箱功能已停用/); checks++
assert.match(recovery, /to="\/me"/); checks++
console.log(`Email retirement passed: ${checks} actual-SFC/lifecycle assertions; no recovery API or session mutation.`)
