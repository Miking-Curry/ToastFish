import { createApp, nextTick } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import App from './App.vue'
import Dashboard from './pages/Dashboard.vue'
import Records from './pages/Records.vue'
import Words from './pages/Words.vue'
import Reviews from './pages/Reviews.vue'
import Settings from './pages/Settings.vue'
import './style.css'

const token = new URLSearchParams(location.hash.slice(1)).get('token')
if (token) {
  sessionStorage.setItem('toastfish-token', token)
  history.replaceState(null, '', location.pathname + location.search)
}
const router = createRouter({ history: createWebHistory(), routes: [
  { path: '/', component: Dashboard, meta: { title: '学习首页', subtitle: '从上次停下的地方，继续。' } },
  { path: '/records', component: Records, meta: { title: '学习记录', subtitle: '每一次记住，都有迹可循。' } },
  { path: '/words', component: Words, meta: { title: '我的单词', subtitle: '查找词义，也看看你与它的学习记录。' } },
  { path: '/reviews', component: Reviews, meta: { title: '复习与错词', subtitle: '把时间花在需要巩固的地方。' } },
  { path: '/settings', component: Settings, meta: { title: '词库与设置', subtitle: '选择内容，按自己的节奏学习。' } }
] })
router.afterEach(to => {
  document.title = `${String(to.meta.title || '学习管理')} · ToastFish`
  void nextTick(() => document.querySelector<HTMLElement>('main h1')?.focus({ preventScroll: true }))
})
createApp(App).use(router).mount('#app')
