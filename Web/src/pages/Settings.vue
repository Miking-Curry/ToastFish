<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { action, api, download, mutations, query, readFile } from '../api'
import AppIcon from '../components/AppIcon.vue'
const settings = ref<any>(null), books = ref<any[]>([]), preview = ref<any>(null), file = ref<File | null>(null), name = ref('')
const busy = ref(false)
let upload = ''
const load = async () => { settings.value = await api('settings'); books.value = await api('books') }
onMounted(() => action(load))
const save = () => action(async () => {
  if (!Number.isInteger(settings.value.Number) || settings.value.Number < 1 || settings.value.Number > 100) throw new Error('每轮数量应为 1～100 的整数。')
  await api('settings', settings.value)
}, '设置已保存。')
const previewFile = () => action(async () => {
  if (busy.value) return false
  if (!file.value) throw new Error('请先选择英语 Excel 文件。')
  busy.value = true
  try { upload = await readFile(file.value, 10); preview.value = await api('imports/preview', { filename: file.value.name, data: upload }) }
  finally { busy.value = false }
})
const importFile = () => action(async () => {
  if (busy.value) return false
  if (!file.value || !preview.value) throw new Error('请先预览并检查文件。')
  busy.value = true
  try {
    await api('imports/confirm', { filename: file.value.name, data: upload, name: name.value })
    preview.value = null; file.value = null; name.value = ''; upload = ''; await load()
  } finally { busy.value = false }
}, '个人英语词库已保存，可以在首页选择它。')
const reset = () => action(async () => {
  if (!confirm('重置所选词库的学习进度？历史记录和专项词表将保留，操作前会自动备份。')) return false
  await api('books/reset', { bookId: settings.value.BookId }); await load()
}, '词库学习进度已重置，历史记录仍保留。')
const restore = (event: Event, legacy: boolean) => action(async () => {
  const input = event.target as HTMLInputElement
  const chosen = input.files?.[0]; input.value = ''
  if (!chosen) return false
  if (!confirm(legacy ? '导入旧版数据库将替换当前词库与基线，只能在尚无新学习记录时操作。当前数据会自动备份，是否继续？' : '恢复会替换全部词库、记录和设置。当前数据会自动备份，是否继续？')) return false
  busy.value = true
  try { const data = await readFile(chosen, 128); await api('backups/' + (legacy ? 'import-legacy' : 'restore'), { data }); await load() }
  finally { busy.value = false }
}, '数据库已加载。若备份中有未完成轮次，可回到首页继续。')
</script>
<template>
  <template v-if="settings">
    <div class="capability-strip"><AppIcon name="shield"/><strong>真实数据 · 本地运行</strong><span>当前算法：SM2plus</span><span>Windows 原生通知</span><span>SQLite 备份</span><span>FSRS、词频和词根扩展尚未接入，没有模拟开关。</span></div>
    <div class="two-columns"><section class="panel"><h2>学习偏好</h2><form @submit.prevent="save"><fieldset :disabled="busy || mutations > 0"><label>默认英语词库<select v-model="settings.BookId"><option v-for="b in books" :key="b.id" :value="b.id">{{ b.name }}</option></select></label><label>每轮新词数量<input v-model.number="settings.Number" type="number" min="1" max="100" required></label><label>发音<select v-model.number="settings.EngType"><option :value="1">美式英语</option><option :value="2">英式英语</option></select></label><label class="check-label"><input v-model="settings.AutoPlay" type="checkbox"> 展示单词时自动发音</label><label class="check-label"><input v-model="settings.AutoLog" type="checkbox"> 完成本轮后自动导出英语词表</label><p class="hint">学习通知常驻，直到你评分或手动收起。Alt+Q 恢复当前学习，Alt+1～4 对应按钮，Alt+~ 发音。</p><button class="primary" type="submit">保存设置</button></fieldset></form></section>
    <section class="panel"><h2>数据与备份</h2><p class="muted">学习记录保存在本机用户目录，重新编译或更新程序不会覆盖。</p><div class="stack-buttons"><button :disabled="busy || mutations > 0" @click="action(() => download('backups', 'ToastFish备份.sqlite'))">下载完整备份</button><label class="file-button">恢复完整备份<input type="file" accept=".sqlite,.db" :disabled="busy || mutations > 0" @change="restore($event, false)"></label><label class="file-button">迁移旧安装版数据库<input type="file" accept=".sqlite,.db" :disabled="busy || mutations > 0" @change="restore($event, true)"></label></div><p class="hint">恢复前需结束未完成的学习。支持 128MB 以内的兼容数据库；旧版数据无法补出准确的历史日期。</p><hr><h3>重置当前词库</h3><p class="hint">只清除学习状态，保留过去记录；已有未完成轮次时不能重置。</p><button class="danger" :disabled="busy || mutations > 0" @click="reset">重置所选词库进度</button></section></div>
    <section class="panel"><div class="section-heading"><h2>英语词库</h2><button :disabled="busy || mutations > 0" @click="action(() => download('books/export?' + query({book: settings.BookId}), '英语词库.xlsx'))">导出所选词库 / 模板</button></div><div class="table-wrap"><table><thead><tr><th>词库</th><th>来源</th><th>总词数</th><th>已开始学习</th><th>进入复习</th></tr></thead><tbody><tr v-for="b in books" :key="b.id"><td>{{ b.name }}</td><td><span class="badge">{{ b.personal ? '个人词库' : '内置词库' }}</span></td><td>{{ b.total }}</td><td>{{ b.learned }}</td><td>{{ b.reviewed }}</td></tr></tbody></table></div></section>
    <section class="panel"><h2>导入个人英语词库</h2><p class="muted">使用英语导出模板：第 1 行依次为“英语、单词、翻译……”；单词和翻译必填。导入不会覆盖内置词库。</p><div class="filters"><label>词库名称<input v-model="name" maxlength="80" placeholder="例如 工作常用英语"></label><label>英语 Excel 文件<input type="file" accept=".xlsx,.xls" :disabled="busy || mutations > 0" @change="(event: Event) => { file = (event.target as HTMLInputElement).files?.[0] ?? null; preview = null }"></label><button :disabled="busy || mutations > 0 || !file" @click="previewFile">检查并预览</button></div><p class="hint">最多 10MB、20000 行，不接受重复单词；错误会标出行号。日语和自定义内容请继续使用托盘导入。</p><template v-if="preview"><div class="section-heading"><p>共 {{ preview.count }} 个词，显示前 50 个。</p><button class="primary" :disabled="busy || mutations > 0 || !name.trim()" @click="importFile">确认保存词库</button></div><div class="table-wrap"><table><thead><tr><th>单词</th><th>翻译</th><th>词性</th></tr></thead><tbody><tr v-for="w in preview.items" :key="w.wordRank"><td>{{ w.headWord }}</td><td>{{ w.tranCN }}</td><td>{{ w.pos }}</td></tr></tbody></table></div></template></section>
  </template>
  <section v-else class="panel empty"><AppIcon name="settings"/><h2>设置暂未加载</h2><p>请确认本地服务已启动，并从托盘打开管理页。</p><button @click="action(load)">重新加载</button></section>
</template>
