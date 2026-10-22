<script setup lang="ts">
import { computed } from 'vue'
import { date } from '../api'
type Bucket = { id: string; label: string; count: number }
const props = defineProps<{ overview: { total: number; buckets: Bucket[]; generatedAt: string } | null; selected: string; loading: boolean }>()
const emit = defineEmits<{ select: [id: string] }>()
const points = computed(() => {
  const buckets = props.overview?.buckets ?? []
  const maximum = Math.max(1, ...buckets.map(b => b.count))
  return buckets.map((b, i) => ({ ...b, x: 48 + i * 88, y: 165 - b.count / maximum * 120 }))
})
const line = computed(() => points.value.map(p => `${p.x},${p.y}`).join(' '))
</script>
<template>
  <section class="review-planner" :aria-busy="loading">
    <div class="planner-heading"><div><span class="eyebrow">REVIEW TIMELINE</span><h2>把复习，安排在到期之前</h2><p>按实际下次复习时间分布，点击节点筛选单词。</p></div><div class="algorithm-tag">SM2plus <span>当前调度算法</span></div></div>
    <div v-if="overview" class="planner-content"><div class="timeline-chart"><svg viewBox="0 0 712 210" role="group" aria-label="当前词库的复习到期时间分布，纵轴表示单词数量">
      <defs><linearGradient id="timeline-fill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#34d399" stop-opacity=".22"/><stop offset="1" stop-color="#34d399" stop-opacity="0"/></linearGradient></defs>
      <line v-for="y in [45,105,165]" :key="y" x1="25" x2="696" :y1="y" :y2="y" stroke="#ffffff18" stroke-dasharray="4 6"/>
      <polygon v-if="points.length" :points="`48,180 ${line} 664,180`" fill="url(#timeline-fill)" />
      <polyline :points="line" fill="none" stroke="#5eead4" stroke-width="2.5" stroke-linejoin="round"/>
      <g v-for="p in points" :key="p.id" role="button" tabindex="0" :aria-label="p.label + '，' + p.count + '个词'" :aria-pressed="selected === p.id" @click="emit('select', selected === p.id ? '' : p.id)" @keydown.enter="emit('select', selected === p.id ? '' : p.id)" @keydown.space.prevent="emit('select', selected === p.id ? '' : p.id)"><rect :x="p.x - 32" y="8" width="64" height="202" fill="transparent" pointer-events="all"/><circle :cx="p.x" :cy="p.y" :r="selected === p.id ? 8 : 5" fill="#99f6e4" stroke="#164e45" stroke-width="3"/><text :x="p.x" :y="p.y - 25" text-anchor="middle" fill="#e2f7ef" font-size="12">{{ p.count }}</text><text :x="p.x" y="204" text-anchor="middle" fill="#b9d4cd" font-size="11">{{ p.label }}</text></g>
    </svg><p class="chart-disclaimer">纵轴：单词数量 · 不是遗忘概率或个人记忆保持率预测。</p></div><div class="planner-summary"><span>当前词库进入复习</span><strong>{{ overview.total }} <small>个词</small></strong><p v-if="!overview.total">还没有进入复习的单词，完成学习后将在这里展示。</p><p v-else>完整词库汇总，不只统计当前列表页。</p><small>更新于 {{ date(overview.generatedAt) }}</small></div></div>
    <div v-else class="planner-empty">{{ loading ? '正在读取复习计划…' : '复习计划暂不可用，请重新加载。' }}</div>
    <div v-if="overview" class="timeline-filters" aria-label="复习到期时间筛选"><button v-for="b in overview.buckets" :key="b.id" :aria-pressed="selected === b.id" :class="{ selected: selected === b.id }" @click="emit('select', selected === b.id ? '' : b.id)"><span>{{ b.label }}</span><strong>{{ b.count }}</strong></button></div>
  </section>
</template>
