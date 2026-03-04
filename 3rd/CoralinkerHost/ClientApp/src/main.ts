/**
 * @file main.ts
 * @description 应用入口文件
 * 
 * 初始化顺序：
 * 1. 创建 Vue 应用实例
 * 2. 安装 Pinia 状态管理
 * 3. 安装 Naive UI 组件库
 * 4. 安装路由
 * 5. 挂载应用
 */

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'

// 本地字体（替代 Google Fonts CDN，支持纯局域网部署）
import '@fontsource/inter/400.css'
import '@fontsource/inter/500.css'
import '@fontsource/inter/600.css'
import '@fontsource/jetbrains-mono/400.css'
import '@fontsource/jetbrains-mono/500.css'

// 全局样式
import './styles/main.css'

// LiteGraph 样式
import 'litegraph.js/css/litegraph.css'

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.mount('#app')

console.log('[App] Coralinker DIVER Host started')
