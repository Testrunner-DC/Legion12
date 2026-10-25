import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import { parse, compileScript, compileTemplate, compileStyle } from '@vue/compiler-sfc'

const read = name => fs.readFileSync(new URL('../src/l12/site/' + name, import.meta.url), 'utf8')
const profile = read('ProfilePage.vue')
const recovery = read('AccountRecoveryPage.vue')
const platform = fs.readFileSync(new URL('../src/l12/platform.ts', import.meta.url), 'utf8')
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
const retiredExports = new Set(['EmailStatus', 'EmailCapability', 'AuthOperation', 'emailApi', 'requestPasswordReset', 'resetPassword'])
function assertNoRetiredExports(source) {
  const ast = ts.createSourceFile('platform.ts', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  assert.deepEqual(ast.parseDiagnostics, [])
  for (const statement of ast.statements) {
    if (ts.isExportDeclaration(statement)) {
      assert.ok(statement.exportClause && ts.isNamedExports(statement.exportClause), 'Use explicit named exports, not wildcard email re-exports')
      for (const element of statement.exportClause.elements)
        assert.ok(!retiredExports.has(element.name.text), `Retired email re-export remains: ${element.name.text}`)
      continue
    }
    const exported = statement.modifiers?.some(modifier => modifier.kind === ts.SyntaxKind.ExportKeyword)
    if (!exported) continue
    const names = ts.isVariableStatement(statement)
      ? statement.declarationList.declarations.map(declaration => declaration.name.getText(ast))
      : statement.name ? [statement.name.getText(ast)] : []
    for (const name of names) assert.ok(!retiredExports.has(name), `Retired email export remains: ${name}`)
  }
}
assertNoRetiredExports(platform); checks++
for (const source of [
  'export const emailApi = {}', 'export function resetPassword() {}', 'export interface EmailStatus {}',
  'const legacy = {}; export { legacy as emailApi }', 'export type { Status as EmailStatus } from "./legacy"',
  'export * from "./legacy"',
]) { assert.throws(() => assertNoRetiredExports(source)); checks++ }
assertNoRetiredExports('const resetPassword = () => {}; export { resetPassword as adminReset }; export type { Account } from "./account"'); checks++
for (const endpoint of ['/api/auth/email/capability', '/api/auth/email/verify', '/api/auth/password/forgot', '/api/auth/password/reset']) {
  assert.ok(platform.includes(`path === '${endpoint}'`), `Preserve anonymous credential-request boundary for ${endpoint}`); checks++
}
console.log(`Email retirement passed: ${checks} SFC/lifecycle/export-boundary assertions; no recovery API or session mutation.`)
