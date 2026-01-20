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
import { 
  create, 
  NConfigProvider, 
  NMessageProvider, 
  NDialogProvider,
  NNotificationProvider
} from 'naive-ui'
import App from './App.vue'
import router from './router'

// 全局样式
import './styles/main.css'

// LiteGraph 样式
import 'litegraph.js/css/litegraph.css'

// 创建 Naive UI 实例（按需引入的组件会自动注册）
const naive = create({
  components: [
    NConfigProvider,
    NMessageProvider,
    NDialogProvider,
    NNotificationProvider
  ]
})

// 创建 Vue 应用
const app = createApp(App)

// 安装 Pinia 状态管理
app.use(createPinia())

// 安装 Naive UI
app.use(naive)

// 安装路由
app.use(router)

// 挂载应用到 DOM
app.mount('#app')

console.log('[App] Coralinker DIVER Host started')
