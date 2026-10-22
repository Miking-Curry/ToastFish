import { onMounted, onUnmounted, ref } from 'vue'
export const notice = ref('')
export const failure = ref('')
export const connection = ref<'connecting' | 'online' | 'offline' | 'unauthorized'>('connecting')
export const mutations = ref(0)
const inFlight = new Set<string>()

async function request(path: string, data?: unknown): Promise<Response> {
  const key = data === undefined ? '' : path
  if (key && inFlight.has(key)) throw new Error('这项操作正在处理中，请勿重复提交。')
  if (key) { inFlight.add(key); mutations.value++ }
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), 30000)
  try {
    let response: Response
    try {
      response = await fetch('/api/' + path, {
        method: data === undefined ? 'GET' : 'POST', signal: controller.signal,
        headers: { Authorization: 'Bearer ' + (sessionStorage.getItem('toastfish-token') ?? ''), ...(data === undefined ? {} : { 'Content-Type': 'application/json' }) },
        ...(data === undefined ? {} : { body: JSON.stringify(data) })
      })
    } catch (error) {
      connection.value = 'offline'
      throw new Error(error instanceof Error && error.name === 'AbortError' ?
        (key ? '本地服务响应超时，请刷新确认操作结果后再重试。' : '本地服务响应超时，请稍后重试。') :
        '无法连接本地服务，请确认 ToastFish 已启动，并从托盘重新打开管理页。')
    }
    if (!response.ok) {
      connection.value = response.status === 401 ? 'unauthorized' : 'online'
      const value = await response.json().catch(() => null)
      throw new Error(value?.error || `请求失败（${response.status}），请稍后重试。`)
    }
    connection.value = 'online'
    return response
  } finally {
    clearTimeout(timer)
    if (key) { inFlight.delete(key); mutations.value-- }
  }
}

export async function api<T = any>(path: string, data?: unknown): Promise<T> {
  const response = await request(path, data)
  try { return await response.json() as T }
  catch { connection.value = 'offline'; throw new Error('本地服务返回了无法识别的数据，请重新打开管理页。') }
}
export async function action(work: () => Promise<unknown>, message = '') {
  failure.value = ''; notice.value = ''
  try { const result = await work(); if (result !== false) notice.value = message }
  catch (error) { failure.value = error instanceof Error ? error.message : '操作失败。' }
}
export async function download(path: string, filename: string) {
  const response = await request(path)
  if (/text\/html|application\/json/i.test(response.headers.get('content-type') ?? '')) throw new Error('本地服务未返回有效下载文件，请重新打开管理页。')
  const blob = await response.blob()
  if (!blob.size) throw new Error('下载文件为空，请稍后重试。')
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a'); link.href = url; link.download = filename; link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
export const query = (values: Record<string, string | number>) => new URLSearchParams(Object.entries(values).map(([key, value]) => [key, String(value)])).toString()
export const date = (value: string | null) => !value ? '—' : Number.isFinite(new Date(value).getTime()) ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '日期不可用'
export const day = (value = new Date()) => `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`
export const kind: Record<string, string> = { new: '新学', learning: '巩固', review: '复习', relearn: '重学', test: '测验', feedback: '解析' }
export const rating: Record<string, string> = { again: '没有印象', hard: '记忆模糊', good: '暂时记住', easy: '已经牢记', continue: '继续' }
export const status = (value: number) => value === 0 ? '未学习' : value === 5 ? '复习中' : '学习中'
export const roundState: Record<string, string> = { running: '进行中', paused: '已暂停', ended: '已结束', completed: '已完成' }

export function poll(load: () => Promise<unknown>) {
  let timer: ReturnType<typeof setInterval>
  let running = false
  let active = true
  const refresh = async () => {
    if (!active || document.hidden || running) return
    running = true
    try { await load() }
    catch (error) { if (active) failure.value = error instanceof Error ? error.message : '加载失败。' }
    finally { running = false }
  }
  onMounted(() => { refresh(); timer = setInterval(refresh, 5000); document.addEventListener('visibilitychange', refresh) })
  onUnmounted(() => { active = false; clearInterval(timer); document.removeEventListener('visibilitychange', refresh) })
}
export function readFile(file: File, maxMB: number): Promise<string> {
  if (file.size > maxMB * 1024 * 1024) return Promise.reject(new Error(`文件不能超过 ${maxMB}MB。`))
  return new Promise((resolve, reject) => {
    const reader = new FileReader(); reader.onload = () => resolve(String(reader.result).split(',')[1]!)
    reader.onerror = () => reject(new Error('文件读取失败。')); reader.readAsDataURL(file)
  })
}
