/**
 * @file router.ts
 * @description Vue Router 配置
 * 
 * 路由定义：
 * - / : 主页面（节点图编辑器）
 * - /control : 控制面板页面
 */

import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'home',
      component: () => import('./views/HomeView.vue'),
      meta: {
        title: 'Coralinker DIVER Host'
      }
    },
    {
      path: '/control',
      name: 'control',
      component: () => import('./views/ControlPanelView.vue'),
      meta: {
        title: 'Control Panel - Coralinker'
      }
    }
  ]
})

// 路由守卫：更新页面标题
router.beforeEach((to) => {
  document.title = (to.meta.title as string) || 'Coralinker DIVER Host'
})

export default router
