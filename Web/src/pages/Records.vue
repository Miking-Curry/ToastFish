<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref } from 'vue'
import { action, api, date, day, download, kind, mutations, query, rating, roundState, status } from '../api'
import AppIcon from '../components/AppIcon.vue'
const monday = new Date(); monday.setDate(monday.getDate() - (monday.getDay() + 6) % 7)
const from = ref(day(monday)), to = ref(day()), book = ref(''), type = ref('')
const books = ref<any[]>([]), result = ref<any>(null), page = ref(1), detail = ref<any>(null), loading = ref(false)
const dialog = ref<HTMLDialogElement>()
const applied = ref({ from: from.value, to: to.value, book: '', kind: '' })
let generation = 0, detailGeneration = 0
let active = true
const load = async () => {
  const current = ++generation; loading.value = true
  try {
    const value = await api('records?' + query({ ...applied.value, page: page.value }))
    if (!value || !Array.isArray(value.items) || !Array.isArray(value.sessions) || !Number.isFinite(value.count)) throw new Error('学习记录数据格式无效，请重新加载。')
    if (current === generation) result.value = value
  }
  catch (error) { if (current === generation) { result.value = null; throw error } }
  finally { if (current === generation) loading.value = false }
}
const search = () => action(async () => {
  if (!from.value || !to.value || from.value > to.value) throw new Error('请选择有效的开始和结束日期。')
  applied.value = { from: from.value, to: to.value, book: book.value, kind: type.value }; page.value = 1; await load()
})
const preset = (week: boolean) => { const start = new Date(); if (week) start.setDate(start.getDate() - (start.getDay() + 6) % 7); from.value = day(start); to.value = day(); void search() }
const show = (id: string) => action(async () => { const current = ++detailGeneration; const value = await api('sessions/detail?id=' + encodeURIComponent(id)); if (current !== detailGeneration) return; detail.value = value; await nextTick(); if (!dialog.value?.open) dialog.value?.showModal() })
const close = () => { ++detailGeneration; dialog.value?.close() }
const exportRecords = () => action(() => download('exports?' + query(applied.value), '学习记录.xlsx'), '当前查询的学习记录已导出为 Excel。')
onMounted(() => action(async () => { books.value = await api('books'); if (active) await load() }))
onUnmounted(() => { active = false; generation++; detailGeneration++ })
</script>
<template>
  <section class="panel search-panel">
    <form class="filters" @submit.prevent="search"><label>开始日期<input v-model="from" type="date" :max="to" required></label><label>结束日期<input v-model="to" type="date" :min="from" required></label><label>词库<select v-model="book" @change="search"><option value="">全部英语词库</option><option v-for="b in books" :key="b.id" :value="b.id">{{ b.name }}</option></select></label><label>操作类型<select v-model="type" @change="search"><option value="">全部操作</option><option value="ratings">通知评分</option><option value="test">随堂测验</option></select></label><button class="primary" type="submit" :disabled="loading">查询</button><button type="button" @click="preset(false)">今天</button><button type="button" @click="preset(true)">本周</button></form>
  </section>
  <section class="panel" :aria-busy="loading"><div class="section-heading"><div><span class="eyebrow">YOUR LEARNING HISTORY</span><h2>每一次练习，都有迹可循 <span class="badge neutral">{{ result?.count ?? '—' }} 条</span></h2></div><button :disabled="!result || !result.count || loading || mutations > 0" @click="exportRecords">导出当前查询 Excel</button></div>
    <p class="hint">评分与测验结果均来自真实记录。当前算法未记录 FSRS 稳定性变化，不补造参数。</p>
    <div v-if="result" class="table-wrap"><table><thead><tr><th>时间</th><th>单词</th><th>词库</th><th>类型</th><th>结果</th><th>轮次</th></tr></thead><tbody><tr v-for="r in result.items" :key="r.id"><td class="small timestamp">{{ date(r.atUtc) }}</td><td><strong class="record-word">{{ r.headWord }}</strong><small>{{ r.translation }}</small></td><td>{{ r.bookName }}</td><td><span class="badge" :class="{neutral: r.kind === 'test'}">{{ kind[r.kind] || r.kind }}</span></td><td><span :class="{'result-wrong': r.correct === 0, 'result-correct': r.correct === 1}">{{ r.correct == null ? (rating[r.action] || r.action) : r.correct === 1 ? '答对' : '答错' }}</span></td><td><button class="text-button" @click="show(r.sessionId)">轮次复盘 →</button></td></tr></tbody></table></div>
    <div v-if="!result" class="empty"><p>{{ loading ? '正在读取学习记录…' : '学习记录暂时无法加载。' }}</p><button v-if="!loading" @click="search">重试</button></div>
    <div v-else-if="!result.items.length" class="empty"><AppIcon name="records"/><h3>这段时间还没有记录</h3><p>尝试调整日期或筛选；旧版状态不会被补成虚构的历史记录。</p></div>
    <div v-if="result" class="pagination"><span>第 {{ page }} / {{ Math.max(1, Math.ceil(result.count / 50)) }} 页</span><button :disabled="page <= 1 || loading" @click="page--; action(load)">上一页</button><button :disabled="page * 50 >= result.count || loading" @click="page++; action(load)">下一页</button></div>
  </section>
  <section v-if="result" class="panel"><div class="section-heading"><div><span class="eyebrow">SESSION RECAP</span><h2>这段时间的学习轮次</h2></div><span class="muted small">最多显示最近 100 轮，不随操作类型筛选</span></div><div class="round-list"><button v-for="s in result.sessions" :key="s.id" class="round-item" @click="show(s.id)"><span class="round-icon"><AppIcon name="records"/></span><span class="grow"><strong>{{ s.bookName }}</strong><small>{{ date(s.created) }}</small></span><span class="badge" :class="{neutral: s.state === 'ended' || s.state === 'paused'}">{{ roundState[s.state] || s.state }}</span></button></div><p v-if="!result.sessions.length" class="empty compact">没有学习轮次。</p></section>
  <dialog ref="dialog" class="round-dialog" aria-label="学习轮次复盘" @close="detailGeneration++"><template v-if="detail"><div class="dialog-heading"><div><span class="eyebrow">SESSION RECAP</span><h2>本轮词表 · {{ roundState[detail.round.State] }}</h2></div><button @click="close" aria-label="关闭轮次复盘"><AppIcon name="close"/></button></div><div class="dialog-body"><p class="muted">{{ date(detail.round.Created) }} · 共 {{ Object.keys(detail.round.Cards).length }} 个词 · {{ detail.events.length }} 条操作（含解析确认）</p><div class="table-wrap"><table><thead><tr><th>单词</th><th>翻译</th><th>已操作</th><th>学习状态</th></tr></thead><tbody><tr v-for="word in detail.round.Cards" :key="word.Key"><td class="record-word">{{ word.Word.headWord }}</td><td>{{ word.Word.tranCN }}</td><td>{{ word.Rated ? '是' : '否' }}</td><td><span class="badge">{{ status(word.Status) }}</span></td></tr></tbody></table></div><p v-if="detail.round.SkippedTests" class="hint">{{ detail.round.SkippedTests }} 个词因缺少干扰选项未测验。</p></div></template></dialog>
</template>
