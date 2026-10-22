<script setup lang="ts">
import { computed, ref } from 'vue'
import { action, api, date, mutations, poll } from '../api'
import AppIcon from '../components/AppIcon.vue'
import SessionCard from '../components/SessionCard.vue'
const data = ref<any>(null), book = ref(''), number = ref(10), starting = ref(false)
let generation = 0
const load = async () => {
  const current = ++generation
  const value = await api('dashboard')
  if (!value?.settings || !Array.isArray(value.books) || !Array.isArray(value.days) || !value.today || !value.week) throw new Error('首页数据不完整，请确认本地程序版本与管理页一致。')
  if (current !== generation) return
  data.value = value
  if (!book.value) { book.value = value.settings.BookId; number.value = value.settings.Number }
}
poll(load)
const max = computed(() => Math.max(1, ...(data.value?.days ?? []).flatMap((d: any) => [d.counts.newCount, d.counts.reviewCount])))
const chartLabel = computed(() => (data.value?.days ?? []).map((d: any) => `${d.date}新学${d.counts.newCount}个、复习${d.counts.reviewCount}个`).join('；'))
const start = async () => {
  if (starting.value) return
  starting.value = true
  await action(async () => {
    if (!Number.isInteger(number.value) || number.value < 1 || number.value > 100) throw new Error('每轮新词数量应为 1～100 的整数。')
    if (!book.value) throw new Error('请先选择英语词库。')
    await api('sessions/start', { bookId: book.value, number: number.value, mode: 'normal' }); await load()
  }, '学习已开始，请查看 Windows 原生通知。')
  starting.value = false
}
</script>
<template>
  <template v-if="data">
    <div class="stats-grid">
      <article class="stat"><div class="stat-heading"><span>今日新学</span><AppIcon name="book"/></div><strong>{{ data.today.newCount }}<small>个词</small></strong><p>首次开始学习的新词，按词去重</p></article>
      <article class="stat"><div class="stat-heading"><span>今日复习</span><AppIcon name="review"/></div><strong>{{ data.today.reviewCount }}<small>个词</small></strong><p>包含复习与重学，不重复计词数</p></article>
      <article class="stat urgent-stat"><div class="stat-heading"><span>当前待复习</span><AppIcon name="records"/></div><strong>{{ data.due }}<small>个词</small></strong><RouterLink to="/reviews">查看到期词表 <span>→</span></RouterLink></article>
      <article class="stat"><div class="stat-heading"><span>本周操作</span><AppIcon name="home"/></div><strong>{{ data.week.actions }}<small>次</small></strong><p>每次有效评分和测验都会记录</p></article>
    </div>
    <SessionCard :session="data.session" @changed="load" />
    <div class="two-columns dashboard-columns">
      <section class="panel"><div class="section-heading"><div><span class="eyebrow">WEEKLY ACTIVITY</span><h2>每天一点，记忆多一点</h2></div><span class="legend"><i></i> 新学 <i class="secondary"></i> 复习</span></div>
        <div class="week-summary"><span>本周新学 <strong>{{ data.week.newCount }}</strong> 个</span><span>本周复习 <strong>{{ data.week.reviewCount }}</strong> 个</span><span class="muted">按词去重，与每日词数之和可能不同</span></div>
        <div class="chart" role="img" :aria-label="chartLabel || '本周暂无学习记录'"><div v-for="(item, i) in data.days" :key="item.date" class="chart-column"><div class="bar-space"><div class="bar-pair" :title="`${item.date} 新学 ${item.counts.newCount}，复习 ${item.counts.reviewCount}`"><div class="bar" :style="{height: `${item.counts.newCount / max * 164}px`}"></div><div class="bar review-bar" :style="{height: `${item.counts.reviewCount / max * 164}px`}"></div></div></div><span>周{{ ['一','二','三','四','五','六','日'][Number(i)] }}</span><small>{{ item.counts.newCount }} / {{ item.counts.reviewCount }}</small></div></div>
        <p v-if="!data.week.actions" class="chart-empty-note">这一周还没有学习记录，从一轮新的学习开始。</p>
      </section>
      <section class="panel launch-panel"><div class="launch-icon"><AppIcon name="bell"/></div><span class="eyebrow">START A SMALL SESSION</span><h2>准备好，开始这一轮</h2><p class="muted">留在通知里学习，在工作台里回顾。</p><form @submit.prevent="start"><label>英语词库<select v-model="book" :disabled="starting"><option v-for="b in data.books" :key="b.id" :value="b.id">{{ b.name }}</option></select></label><label>本轮新词数量<input v-model.number="number" type="number" min="1" max="100" required :disabled="starting"></label><button class="primary wide" type="submit" :disabled="!!data.session || starting || mutations > 0">{{ starting ? '正在启动…' : '开始通知背词' }}<AppIcon name="arrow"/></button></form><p class="hint">每轮仍会按现有规则加入复习词。有未完成轮次时，请先继续或结束。</p></section>
    </div>
    <section class="panel"><div class="section-heading"><div><span class="eyebrow">LIBRARY PROGRESS</span><h2>每本词库，都有进展</h2></div><RouterLink to="/settings">管理词库 →</RouterLink></div><div class="book-grid"><article v-for="b in data.books" :key="b.id" class="book-progress"><div class="book-heading"><span class="book-symbol"><AppIcon name="book"/></span><span class="badge neutral">{{ b.personal ? '个人词库' : '内置词库' }}</span></div><h3>{{ b.name }}</h3><p><strong>{{ b.learned }}</strong> <span class="muted">/ {{ b.total }} 个已开始学习</span></p><progress :value="b.learned" :max="b.total || 1" :aria-label="b.name + '已开始学习进度'"></progress><div class="book-meta"><small>{{ b.reviewed }} 个进入复习</small><small>{{ b.total ? Math.round(b.learned / b.total * 100) : 0 }}%</small></div></article></div></section>
    <div class="data-note"><AppIcon name="shield"/><span>记录开始于 {{ date(data.historySince) }}<br><small>用户数据：{{ data.dataPath }}</small></span></div>
  </template>
  <div v-else class="panel empty"><AppIcon name="home"/><h2>正在打开你的学习空间</h2><p>尚未取得有效数据时不会显示示例统计。</p><button @click="action(load)">重新加载</button></div>
</template>
