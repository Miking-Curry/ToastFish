<script setup lang="ts">
import { onUnmounted, watch } from 'vue'
import { date, kind, rating, status } from '../api'
import { pronounce, speechAvailable, speaking, stopSpeech } from '../speech'
import AppIcon from './AppIcon.vue'
const props = defineProps<{ detail: any; loading: boolean; busy: boolean }>()
const emit = defineEmits<{ star: [key: string, value: boolean]; study: [key: string]; retry: [] }>()
onUnmounted(stopSpeech)
watch(() => props.detail?.word.Key, stopSpeech)
const interval = (value: number) => Number.isFinite(value) && value > 0 ? `${Number(value.toFixed(2))} 天` : '暂无'
</script>
<template>
  <aside class="word-inspector panel" tabindex="0" aria-label="单词详情，可在面板内滚动" :aria-busy="loading">
    <div class="inspector-heading"><span class="eyebrow">WORD INSPECTOR</span><AppIcon name="book"/></div>
    <div v-if="loading" class="inspector-empty" role="status">正在读取词条…</div>
    <template v-else-if="detail">
      <h2 class="inspector-word">{{ detail.word.Word.headWord }}</h2>
      <span class="badge">{{ status(detail.word.Status) }}</span>
      <div class="pronunciations">
        <button :disabled="!speechAvailable" @click="pronounce(detail.word.Word.headWord, 'en-GB')" :aria-label="'朗读 ' + detail.word.Word.headWord + ' 英式发音'"><span>英</span><code>{{ detail.word.Word.ukPhone ? '/' + detail.word.Word.ukPhone + '/' : '音标未提供' }}</code><AppIcon name="volume"/></button>
        <button :disabled="!speechAvailable" @click="pronounce(detail.word.Word.headWord, 'en-US')" :aria-label="'朗读 ' + detail.word.Word.headWord + ' 美式发音'"><span>美</span><code>{{ detail.word.Word.usPhone ? '/' + detail.word.Word.usPhone + '/' : '音标未提供' }}</code><AppIcon name="volume"/></button>
      </div>
      <p class="speech-note">{{ !speechAvailable ? '当前浏览器不支持朗读，可使用通知中的发音。' : speaking ? '正在朗读，可再次点击切换发音。' : '使用浏览器语音，具体音色取决于本机语音包。' }}</p>
      <div class="definition"><span>{{ detail.word.Word.pos || '释义' }}</span><p>{{ detail.word.Word.tranCN || '当前词库未提供释义。' }}</p></div>
      <section v-if="detail.word.Word.phrase" class="inspector-section"><h3>常用搭配</h3><p>{{ detail.word.Word.phrase }}</p><p class="muted">{{ detail.word.Word.phraseCN }}</p></section>
      <section v-if="detail.word.Word.sentence" class="inspector-section"><h3>词库例句</h3><blockquote>{{ detail.word.Word.sentence }}<small>{{ detail.word.Word.sentenceCN }}</small></blockquote></section>
      <section class="inspector-section"><h3>当前学习参数 <span class="badge neutral">SM2plus</span></h3><div class="parameter-grid"><div><span>复习间隔</span><strong>{{ interval(detail.word.Interval) }}</strong></div><div><span>难度系数</span><strong>{{ Number.isFinite(detail.word.Difficulty) ? detail.word.Difficulty.toFixed(2) : '暂无' }}</strong></div></div><p class="detail-date">下次复习 <strong>{{ date(detail.word.DueUtc) }}</strong></p><p class="hint">这些是现有调度参数，不等同于 FSRS 的稳定性或记忆保持率。</p></section>
      <div class="inspector-actions"><button class="primary" :disabled="busy" @click="emit('study', detail.word.Key)"><AppIcon name="bell"/>通知重学</button><button :disabled="busy" @click="emit('star', detail.word.Key, !detail.starred)">{{ detail.starred ? '移出专项词表' : '加入专项词表' }}</button></div>
      <section class="inspector-section"><h3>最近学习记录</h3><ul class="timeline"><li v-for="e in detail.events.slice(0, 12)" :key="e.id"><span>{{ date(e.atUtc) }}</span><strong>{{ kind[e.kind] || '学习操作' }} · {{ e.correct == null ? (rating[e.action] || e.action) : e.correct === 1 ? '答对' : '答错' }}</strong></li></ul><p v-if="!detail.events.length" class="empty compact">暂无新版本记录，旧版学习状态保留为基线。</p></section>
      <p class="hint">仅展示当前词库已有内容。词频、星级、词根等扩展数据尚未接入。</p>
    </template>
    <div v-else class="inspector-empty"><AppIcon name="book"/><h3>点选一个单词</h3><p>在这里查看释义、例句和学习时间线。</p><button class="text-button" @click="emit('retry')">重新加载列表</button></div>
  </aside>
</template>
