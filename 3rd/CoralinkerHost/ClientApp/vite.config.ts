/**
 * @file vite.config.ts
 * @description Vite 构建配置
 * 
 * 关键配置：
 * 1. Monaco Editor 插件 - 处理 Web Worker 文件
 * 2. 开发服务器代理 - 转发 API 和 SignalR 请求到后端
 * 3. 构建输出 - 输出到 ../wwwroot 供 ASP.NET Core 托管
 */

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import monacoEditorPlugin from 'vite-plugin-monaco-editor'
import { resolve } from 'path'

export default defineConfig({
  plugins: [
    vue(),
    // Monaco Editor 插件：自动处理 editor.worker.js 等 Worker 文件
    (monacoEditorPlugin as any).default({
      languageWorkers: ['editorWorkerService', 'css', 'html', 'json', 'typescript']
    })
  ],

  resolve: {
    alias: {
      // 路径别名：使用 @ 代替 src 目录
      '@': resolve(__dirname, 'src')
    }
  },

  server: {
    port: 5173,
    // 开发模式下，将 API 和 SignalR 请求代理到后端服务器
    proxy: {
      '/api': {
        target: 'http://localhost:4499',
        changeOrigin: true
      },
      '/hubs': {
        target: 'http://localhost:4499',
        changeOrigin: true,
        // SignalR 需要 WebSocket 支持
        ws: true
      }
    }
  },

  build: {
    // 构建产物输出到 wwwroot，供 ASP.NET Core 静态文件服务
    outDir: '../wwwroot',
    emptyOutDir: true,
    sourcemap: true
  },

  // 预构建优化：提前处理较大的依赖库
  optimizeDeps: {
    include: [
      'monaco-editor',
      'litegraph.js',
      'naive-ui',
      'pinia',
      '@microsoft/signalr'
    ]
  }
})
