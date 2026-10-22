<script setup lang="ts">
import { action, api, roundState } from '../api'
defineProps<{ session: any }>()
const emit = defineEmits<{ changed: [] }>()
const command = (name: string) => action(async () => {
  if (name === 'end' && !confirm('结束本轮？已经保存的评分和记录会保留，剩余任务将不再继续。')) return false
  await api('sessions/' + name, {}); emit('changed')
}, '学习状态已更新。')
</script>
<template>
  <section v-if="session" class="session-card">
    <div><span class="eyebrow">CONTINUE WHERE YOU LEFT OFF</span><h2>这轮学习，接着来</h2><p>{{ roundState[session.state] }} · 已操作 {{ session.rated }} / {{ session.total }} 个词 · 已进入复习 {{ session.finished }} 个</p><p v-if="session.current" class="muted">当前：{{ session.current }} <span v-if="session.stage === 'feedback'">· 等待阅读解析</span><span v-else-if="session.stage === 'test'">· 测验阶段</span></p></div>
    <div class="button-row"><button class="primary" @click="command('resume')">继续学习 ↗</button><button @click="command('pause')">暂停</button><button class="text-button" @click="command('end')">结束本轮</button></div>
  </section>
</template>
