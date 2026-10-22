<script setup lang="ts">
import { ref } from 'vue'
import { useRoute } from 'vue-router'
import { action, api, connection, failure, mutations, notice, poll, roundState } from './api'
import AppIcon from './components/AppIcon.vue'
const route = useRoute()
const links = [
  { path: '/', icon: 'home', label: '学习首页', caption: 'Overview' },
  { path: '/records', icon: 'records', label: '学习记录', caption: 'History' },
  { path: '/words', icon: 'book', label: '我的单词', caption: 'Vocabulary' },
  { path: '/reviews', icon: 'review', label: '复习与错词', caption: 'Review' },
  { path: '/settings', icon: 'settings', label: '词库与设置', caption: 'Settings' }
] as const
const session = ref<any>(null), launching = ref(false)
poll(async () => { session.value = await api('sessions') })
const learn = async () => {
  if (launching.value) return
  launching.value = true
  await action(async () => {
    const current = await api('sessions')
    if (current) await api('sessions/resume', {})
    else { const settings = await api('settings'); await api('sessions/start', { bookId: settings.BookId, number: settings.Number, mode: 'normal' }) }
    session.value = await api('sessions')
  }, '已请求 Windows 原生通知，请查看桌面右下角；页面不会模拟评分或更改记忆参数。')
  launching.value = false
}
</script>
<template>
  <div class="layout">
    <aside class="sidebar">
      <RouterLink class="brand" to="/" aria-label="ToastFish 首页"><span class="brand-mark">tf</span><span>ToastFish <b class="brand-badge">工作台</b><small>一点一滴，记住更多</small></span></RouterLink>
      <div class="nav-label">学习空间</div>
      <nav aria-label="管理导航"><RouterLink v-for="link in links" :key="link.path" :to="link.path" :class="{ active: route.path === link.path }"><AppIcon :name="link.icon"/><span>{{ link.label }}</span><small>{{ link.caption }}</small></RouterLink></nav>
      <div class="sidebar-engine"><AppIcon name="shield"/><strong>数据留在本机</strong><p>当前使用 SM2plus 调度。评分来自真实学习，不使用模拟保持率。</p><span>WINDOWS NATIVE NOTIFICATIONS</span></div>
      <div class="sidebar-foot"><span class="online-dot" :class="{offline: connection !== 'online'}"></span>{{ connection === 'online' ? '本地服务已连接' : connection === 'unauthorized' ? '需要重新打开管理页' : connection === 'offline' ? '本地服务未连接' : '正在连接本地服务' }}</div>
    </aside>
    <main>
      <header class="page-header"><div><div class="eyebrow">YOUR LEARNING SPACE <span class="engine-chip">SM2plus</span></div><h1 tabindex="-1">{{ route.meta.title }}</h1><p>{{ route.meta.subtitle }}</p></div><div class="header-actions"><span class="session-pill"><i :class="{running: session?.state === 'running'}"></i>{{ session ? roundState[session.state] : '尚无未完成轮次' }}</span><button class="primary" :disabled="launching || mutations > 0 || connection !== 'online'" @click="learn"><AppIcon name="bell"/>{{ session ? '继续通知背词' : '通知背词' }}</button></div></header>
      <div v-if="failure" role="alert" class="alert error"><span>{{ failure }}</span><button class="text-button" @click="failure = ''" aria-label="关闭错误提示"><AppIcon name="close"/></button></div>
      <div v-if="notice" role="status" class="alert success"><span>{{ notice }}</span><button class="text-button" @click="notice = ''" aria-label="关闭操作提示"><AppIcon name="close"/></button></div>
      <RouterView />
      <footer>ToastFish · 本地学习工作台 <span>记录从启用管理功能后开始积累；日语、五十音及自定义内容请使用托盘入口。</span></footer>
    </main>
  </div>
</template>
