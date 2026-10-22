import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
export default defineConfig({
  plugins: [vue()],
  preview: { host: '127.0.0.1', port: 4173, strictPort: true, proxy: {} },
  server: {
    port: 5173, strictPort: true,
    proxy: { '/api': {
      target: 'http://127.0.0.1:17653', changeOrigin: true,
      configure(proxy) { proxy.on('proxyReq', req => { req.setHeader('Origin', 'http://127.0.0.1:17653') }) }
    } }
  }
})
