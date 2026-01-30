<!--
  @file App.vue
  @description 根组件
  
  功能：
  1. 提供 Naive UI 主题配置
  2. 初始化 SignalR 连接
  3. 加载项目数据
  4. 渲染路由视图
-->

<template>
  <!-- Naive UI 配置提供器：设置暗色主题 -->
  <n-config-provider :theme="darkTheme" :theme-overrides="themeOverrides">
    <!-- 消息提供器：用于全局消息提示 -->
    <n-message-provider>
      <!-- 对话框提供器：用于全局对话框 -->
      <n-dialog-provider>
        <!-- 通知提供器：用于全局通知 -->
        <n-notification-provider>
          <!-- 应用主容器 -->
          <div class="app-container">
            <!-- 加载状态 -->
            <div v-if="loading" class="loading-overlay">
              <n-spin size="large" />
              <p>Loading...</p>
            </div>
            
            <!-- 路由视图 -->
            <router-view v-else />

            <!-- MCU 致命错误对话框 -->
            <FatalErrorDialog 
              v-model:show="showFatalError"
              :error-data="currentFatalError"
              @goto-source="handleGotoSource"
            />
          </div>
        </n-notification-provider>
      </n-dialog-provider>
    </n-message-provider>
  </n-config-provider>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { 
  NConfigProvider, 
  NMessageProvider, 
  NDialogProvider,
  NNotificationProvider,
  NSpin,
  darkTheme,
  type GlobalThemeOverrides
} from 'naive-ui'
import { useProjectStore, useFilesStore, useUiStore } from '@/stores'
import { useSignalR, type FatalErrorData } from '@/composables'
import FatalErrorDialog from '@/components/graph/FatalErrorDialog.vue'

// ============================================
// 主题配置
// ============================================

/**
 * Naive UI 主题覆盖
 * 自定义颜色以匹配原有 UI 风格
 */
const themeOverrides: GlobalThemeOverrides = {
  common: {
    // 主色调：蓝色
    primaryColor: '#4f8cff',
    primaryColorHover: '#6a9fff',
    primaryColorPressed: '#3d7aed',
    
    // 背景色：深色
    bodyColor: '#0b1220',
    cardColor: '#111827',
    modalColor: '#1a2332',
    popoverColor: '#1a2332',
    
    // 边框
    borderColor: '#2d3748',
    
    // 文字
    textColorBase: '#e2e8f0',
    textColor1: '#e2e8f0',
    textColor2: '#a0aec0',
    textColor3: '#718096',
    
    // 圆角
    borderRadius: '8px',
    borderRadiusSmall: '4px'
  },
  Button: {
    // 按钮样式
    fontWeight: '500'
  },
  Card: {
    // 卡片样式
    borderRadius: '10px'
  },
  DataTable: {
    // 表格样式
    borderRadius: '8px'
  }
}

// ============================================
// 初始化逻辑
// ============================================

const loading = ref(true)

const projectStore = useProjectStore()
const filesStore = useFilesStore()
const uiStore = useUiStore()

// 初始化 SignalR 连接
const { onFatalError, offFatalError } = useSignalR()

// ============================================
// 致命错误处理
// ============================================

const showFatalError = ref(false)
const currentFatalError = ref<FatalErrorData | null>(null)

/**
 * 处理 MCU 致命错误
 */
function handleFatalError(errorData: FatalErrorData) {
  console.error('[App] Fatal error received:', errorData)
  currentFatalError.value = errorData
  showFatalError.value = true
}

/**
 * 跳转到源代码
 */
function handleGotoSource(file: string, line: number) {
  console.log(`[App] Go to source: ${file}:${line}`)
  // 通过 UI store 触发源码跳转
  uiStore.gotoSource(file, line)
}

// 注册致命错误回调
onMounted(() => {
  onFatalError(handleFatalError)
})

onUnmounted(() => {
  offFatalError(handleFatalError)
})

/**
 * 应用初始化
 * 加载项目数据和文件树
 */
onMounted(async () => {
  try {
    // 并行加载项目和文件树
    await Promise.all([
      projectStore.loadProject(),
      filesStore.loadFileTree()
    ])
    
    uiStore.setInitialized()
    console.log('[App] Initialized successfully')
  } catch (error) {
    console.error('[App] Initialization failed:', error)
    uiStore.error('Initialization Failed', String(error))
  } finally {
    loading.value = false
  }
})
</script>

<style>
/* 全局应用容器 */
.app-container {
  width: 100vw;
  height: 100vh;
  overflow: hidden;
  background: var(--body-color, #0b1220);
}

/* 加载覆盖层 */
.loading-overlay {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100vh;
  gap: 16px;
  color: #a0aec0;
}

.loading-overlay p {
  margin: 0;
  font-size: 14px;
}
</style>
