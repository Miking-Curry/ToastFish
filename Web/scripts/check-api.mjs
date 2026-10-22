import assert from 'node:assert/strict'
import { api, action, connection, date, download, failure, mutations, notice, query } from '../src/api.ts'

// 所有 fetch 都在内存里替换，不访问网络或数据库。
globalThis.sessionStorage = { getItem: () => 'frontend-check' }
let checks = 0
const check = (condition, message) => { assert.ok(condition, message); checks++ }
let requests = 0
globalThis.fetch = async (url, options) => {
  requests++
  check(url === '/api/settings', 'local API path')
  check(options.headers.Authorization === 'Bearer frontend-check', 'authorization header')
  check(options.method === 'GET', 'read method')
  return Response.json({ Number: 10 })
}
check((await api('settings')).Number === 10, 'JSON result')
check(connection.value === 'online', 'connected state')
let finish
globalThis.fetch = (url, options) => {
  requests++
  check(options.method === 'POST' && options.body === '{"number":10}', 'mutation payload')
  return new Promise(resolve => { finish = resolve })
}
const before = requests
const first = api('sessions/start', { number: 10 })
await assert.rejects(api('sessions/start', { number: 10 }), /正在处理中/); checks++
check(mutations.value === 1, 'pending mutation state')
finish(Response.json({ ok: true }))
await first
check(requests === before + 1 && mutations.value === 0, 'one request and cleanup')

globalThis.fetch = async () => Response.json({ error: '凭据失效' }, { status: 401 })
await assert.rejects(api('sessions'), /凭据失效/); checks++
check(connection.value === 'unauthorized', 'unauthorized state')
globalThis.fetch = async () => { throw new TypeError('network disconnected') }
await assert.rejects(api('words'), /无法连接本地服务/); checks++
check(connection.value === 'offline', 'offline state')
globalThis.fetch = async () => { throw new DOMException('aborted', 'AbortError') }
await assert.rejects(api('words'), /响应超时/); checks++
globalThis.fetch = async () => new Response('<html>bad gateway</html>', { status: 502 })
await assert.rejects(api('words'), /502/); checks++
globalThis.fetch = async () => new Response('<html>static preview</html>')
await assert.rejects(api('settings'), /无法识别的数据/); checks++
check(connection.value === 'offline', 'HTML is not a connected API')
globalThis.fetch = async () => Response.json({ error: '操作冲突' }, { status: 409 })
await assert.rejects(api('settings', { Number: 10 }), /操作冲突/); checks++
check(mutations.value === 0, 'failed mutation releases guard')
globalThis.fetch = async () => Response.json({ ok: true })
await api('settings', { Number: 10 })
check(mutations.value === 0, 'retry allowed after failure')
await action(async () => false, '不应出现')
check(notice.value === '', 'cancel is not success')
await action(async () => { throw new Error('保存失败') }, '不应出现')
check(failure.value === '保存失败' && notice.value === '', 'failure is not success')
check(date(null) === '—' && date('invalid-time') === '日期不可用', 'missing and invalid dates')
check(query({ q: 'a&b', page: 2 }) === 'q=a%26b&page=2', 'encoded query')
globalThis.fetch = async () => new Response('<html>not a workbook</html>', { headers: { 'content-type': 'text/html' } })
await assert.rejects(download('exports', 'records.xlsx'), /有效下载文件/); checks++
globalThis.fetch = async () => new Response(new Uint8Array(), { headers: { 'content-type': 'application/octet-stream' } })
await assert.rejects(download('exports', 'records.xlsx'), /文件为空/); checks++
console.log(`PASS: ${checks} frontend-only checks; no network or database accessed`)
