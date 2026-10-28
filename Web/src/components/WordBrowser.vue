<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { action, api, date, failure, mutations, query, status } from '../api'
import AppIcon from './AppIcon.vue'
import ReviewTimeline from './ReviewTimeline.vue'
import WordInspector from './WordInspector.vue'
const props = defineProps<{ review?: boolean }>()
const books = ref<any[]>([]), book = ref(''), q = ref(''), filter = ref(props.review ? 'due' : ''), window = ref('')
const result = ref<any>(null), page = ref(1), selected = ref<string[]>([]), detail = ref<any>(null)
const loading = ref(false), detailLoading = ref(false), overviewLoading = ref(false), overview = ref<any>(null), overviewError = ref('')
const tabs = [['review', '全部计划'], ['due', '到期复习'], ['forgotten', '评分遗忘'], ['wrong', '测验错词'], ['starred', '专项词表']]
const allVisible = computed(() => result.value?.items.length > 0 && result.value.items.every((w: any) => selected.value.includes(w.key)))
const windowLabel = computed(() => overview.value?.buckets.find((b: any) => b.id === window.value)?.label ?? '')
const overdueCount = computed(() => overview.value?.buckets.find((b: any) => b.id === 'overdue')?.count ?? 0)
const reviewingDue = ref(false), reviewNumber = ref(10)
let generation = 0, detailGeneration = 0, overviewGeneration = 0
let bootstrapGeneration = 0
const show = async (key: string) => {
  const current = ++detailGeneration
  detail.value = null; detailLoading.value = true
  try {
    const value = await api('words/detail?key=' + encodeURIComponent(key))
    if (!value?.word?.Word || !value.word.Key || !Array.isArray(value.events)) throw new Error('词条详情数据不完整，请确认程序版本一致。')
    if (current === detailGeneration) detail.value = value
  } catch (error) { if (current === detailGeneration) throw error }
  finally { if (current === detailGeneration) detailLoading.value = false }
}
const load = async () => {
  const current = ++generation
  loading.value = true
  try {
    const value = await api('words?' + query({ book: book.value, q: q.value, filter: filter.value, window: window.value, page: page.value }))
    if (!value || !Array.isArray(value.items) || !Number.isFinite(value.count) || value.count < 0) throw new Error('词表数据格式无效，请重新加载。')
    if (current !== generation) return
    result.value = value
    const key = detail.value?.word.Key
    if (!value.items.some((w: any) => w.key === key)) {
      if (value.items.length) {
        try { await show(value.items[0].key) }
        catch (error) { if (current === generation) failure.value = '词表已加载，但详情暂不可用。' + (error instanceof Error ? error.message : '') }
      }
      else { ++detailGeneration; detail.value = null; detailLoading.value = false }
    }
  } catch (error) { if (current === generation) { result.value = null; detail.value = null; throw error } }
  finally { if (current === generation) loading.value = false }
}
const loadOverview = async () => {
  if (!props.review) return
  const current = ++overviewGeneration
  overviewLoading.value = true; overviewError.value = ''
  try {
    const value = await api('reviews/overview?' + query({ book: book.value }))
    if (!value || !Array.isArray(value.buckets) || !Number.isFinite(value.total) || value.buckets.some((b: any) => !b || typeof b.id !== 'string' || !Number.isFinite(b.count) || b.count < 0)) throw new Error('复习计划数据格式无效。')
    if (current === overviewGeneration) overview.value = value
  }
  catch (error) { if (current === overviewGeneration) { overview.value = null; overviewError.value = error instanceof Error ? error.message : '复习计划暂不可用。' } }
  finally { if (current === overviewGeneration) overviewLoading.value = false }
}
const search = () => action(async () => { page.value = 1; selected.value = []; await load() })
const changeBook = () => action(async () => { page.value = 1; selected.value = []; window.value = ''; detail.value = null; await Promise.all([load(), loadOverview()]) })
const selectTab = (value: string) => { filter.value = value; window.value = ''; void search() }
const selectWindow = (value: string) => { window.value = value; filter.value = 'review'; void search() }
const star = (key: string, value: boolean) => action(async () => {
  await api('words/star', { key, starred: value })
  if (detail.value?.word.Key === key) detail.value.starred = value
  await load()
}, '专项词表已更新。')
const study = (keys: string[] = selected.value) => action(async () => {
  if (!book.value || !keys.length) throw new Error('请先选择词库和需要学习的单词。')
  if (keys.length > 100) throw new Error('每轮最多选择 100 个单词。')
  await api('sessions/start', { bookId: book.value, number: keys.length, mode: 'selected', keys })
}, '已请求原生通知学习，请查看桌面右下角。已有轮次时请先继续或结束。')
const reviewDue = () => action(async () => {
  if (reviewingDue.value) return false
  if (!book.value || !overdueCount.value) throw new Error('当前词库没有已到期的复习单词。')
  reviewingDue.value = true
  try {
    await api('sessions/start', { bookId: book.value, number: reviewNumber.value, mode: 'due' })
  } finally { reviewingDue.value = false }
}, '已开始到期复习，请查看桌面右下角通知。')
const bootstrap = () => action(async () => {
  const current = ++bootstrapGeneration
  const [list, settings] = await Promise.all([api('books'), api('settings')])
  if (current !== bootstrapGeneration) return
  books.value = list
  reviewNumber.value = settings.Number
  book.value = list.some((b: any) => b.id === settings.BookId) ? settings.BookId : (list[0]?.id ?? '')
  await Promise.all([load(), loadOverview()])
})
const toggleAll = () => { selected.value = allVisible.value ? [] : result.value.items.map((w: any) => w.key) }
onMounted(bootstrap)
onUnmounted(() => { generation++; detailGeneration++; overviewGeneration++; bootstrapGeneration++ })
</script>
<template>
  <ReviewTimeline v-if="review" :overview="overview" :selected="window" :loading="overviewLoading" @select="selectWindow">
    <template #actions>
      <button class="primary overdue-review" :disabled="!book || !overdueCount || overviewLoading || reviewingDue || mutations > 0" @click="reviewDue">{{ reviewingDue ? '正在启动…' : `复习已到期 ${overdueCount} 个` }}</button>
      <p>每轮最多 {{ reviewNumber }} 个，优先复习最早到期的词。有未完成轮次时，请先在首页继续或结束。</p>
    </template>
  </ReviewTimeline>
  <div v-if="overviewError" class="alert warning" role="status"><span>{{ overviewError }} 单词列表仍可使用；时间分布需要配套的新版本本地服务。</span><button @click="loadOverview">重试</button></div>
  <section class="panel search-panel">
    <div v-if="review" class="tabs" aria-label="复习类型"><button v-for="tab in tabs" :key="tab[0]" :aria-pressed="filter === tab[0]" :class="{selected: filter === tab[0]}" @click="selectTab(tab[0]!)">{{ tab[1] }}</button></div>
    <form class="filters" @submit.prevent="search"><label>英语词库<select v-model="book" @change="changeBook"><option v-for="b in books" :key="b.id" :value="b.id">{{ b.name }}</option></select></label><label class="grow">搜索单词或释义<div class="search-input"><AppIcon name="search"/><input v-model="q" placeholder="搜索单词或中文释义…" maxlength="200"></div></label><label v-if="!review">学习状态<select v-model="filter" @change="search"><option value="">全部状态</option><option value="new">未学习</option><option value="learning">学习中</option><option value="review">复习中</option><option value="starred">专项词表</option></select></label><button type="submit" class="primary" :disabled="loading">{{ loading ? '查询中…' : '搜索' }}</button></form>
    <div v-if="window" class="active-filter">复习时间：{{ windowLabel }}<button class="text-button" @click="selectWindow('')">清除时间筛选</button></div>
  </section>
  <div class="dictionary-workspace">
    <section class="panel word-list-panel" aria-label="单词列表" :aria-busy="loading">
      <div class="section-heading"><div><span class="eyebrow">{{ review ? 'REVIEW QUEUE' : 'YOUR VOCABULARY' }}</span><h2>{{ review ? '需要再看一眼的词' : '你的单词' }} <span class="muted small">{{ result?.count ?? '—' }} 个</span></h2></div><button class="primary" :disabled="!selected.length || loading || mutations > 0" @click="study()">学习选中 {{ selected.length }} 个<AppIcon name="arrow"/></button></div>
      <div v-if="result" class="table-wrap"><table><thead><tr><th><input type="checkbox" :checked="allVisible" :disabled="!result.items.length || loading" aria-label="全选当前页单词" @change="toggleAll"></th><th>单词 / 释义</th><th>状态</th><th>{{ review ? '下次复习' : '专项词表' }}</th></tr></thead><tbody><tr v-for="w in result.items" :key="w.key" :class="{ 'inspected-row': detail?.word.Key === w.key }"><td><input v-model="selected" type="checkbox" :value="w.key" :disabled="loading" :aria-label="'选择 ' + w.headWord"></td><td><button class="word-link" @click="action(() => show(w.key))">{{ w.headWord }}</button><small>{{ w.translation }}</small></td><td><span class="badge" :class="{neutral: w.status === 0}">{{ status(w.status) }}</span><small v-if="w.wrongCount">测验答错 {{ w.wrongCount }} 次</small></td><td v-if="review" class="small">{{ date(w.dueUtc) }}</td><td v-else><button class="text-button" :disabled="mutations > 0" @click="star(w.key, !w.starred)">{{ w.starred ? '已加入 · 移出' : '加入' }}</button></td></tr></tbody></table></div>
      <div v-if="loading && !result" class="empty" role="status">正在读取词表…</div>
      <div v-else-if="!result" class="empty"><p>词表暂时无法加载。</p><button @click="bootstrap">重新加载</button></div>
      <div v-else-if="!result.items.length" class="empty"><AppIcon name="book"/><h3>没有符合条件的单词</h3><p>换个筛选条件，或继续当前学习。</p><button v-if="q || window" @click="q = ''; window = ''; search()">清除搜索和时间筛选</button></div>
      <div v-if="result" class="pagination"><span>每页 50 个 · 勾选仅保留当前页</span><button :disabled="page <= 1 || loading" @click="page--; selected = []; action(load)">上一页</button><span>{{ page }} / {{ Math.max(1, Math.ceil(result.count / 50)) }}</span><button :disabled="page * 50 >= result.count || loading" @click="page++; selected = []; action(load)">下一页</button></div>
    </section>
    <WordInspector :detail="detail" :loading="detailLoading" :busy="mutations > 0" @star="star" @study="key => study([key])" @retry="bootstrap"/>
  </div>
</template>
