<!--
  @file components/assets/AssetTree.vue
  @description 资源文件树组件
  
  显示 assets 目录下的文件结构，支持：
  - 展开/折叠文件夹
  - 点击打开文件
  - 右键菜单（删除等）
-->

<template>
  <div class="asset-tree">
    <!-- 树内容 -->
    <div class="tree-content">
      <div v-if="loading" class="tree-loading">
        <n-spin size="small" />
      </div>
      
      <div v-else-if="fileTree.length === 0" class="tree-empty">
        No files yet
      </div>
      
      <template v-else>
        <TreeNode 
          v-for="node in fileTree" 
          :key="node.path"
          :node="node"
          :depth="0"
          @select="handleSelect"
          @delete="handleDelete"
        />
      </template>
    </div>
    
    <!-- 底部上传区域 -->
    <div class="upload-area" @dragover.prevent @drop="handleDrop">
      <input 
        ref="fileInputRef"
        type="file" 
        class="hidden"
        @change="handleFileInput"
      />
      <button class="upload-btn" @click="fileInputRef?.click()">
        📤 Upload Asset
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NSpin } from 'naive-ui'
import { storeToRefs } from 'pinia'
import { useFilesStore, useUiStore } from '@/stores'
import TreeNode from './TreeNode.vue'

// ============================================
// Emits
// ============================================

const emit = defineEmits<{
  (e: 'select', path: string): void
}>()

// ============================================
// Store 引用
// ============================================

const filesStore = useFilesStore()
const uiStore = useUiStore()
const { fileTree, loading } = storeToRefs(filesStore)

// ============================================
// 本地状态
// ============================================

const fileInputRef = ref<HTMLInputElement | null>(null)

// ============================================
// 方法
// ============================================

/**
 * 处理文件选择
 */
function handleSelect(path: string) {
  emit('select', path)
}

/**
 * 处理文件删除
 */
async function handleDelete(path: string) {
  try {
    await filesStore.deleteFile(path)
    uiStore.success('Deleted', path)
  } catch (error) {
    uiStore.error('Delete Failed', String(error))
  }
}

/**
 * 处理文件拖放
 */
async function handleDrop(event: DragEvent) {
  event.preventDefault()
  
  const files = event.dataTransfer?.files
  if (!files || files.length === 0) return
  
  for (const file of files) {
    try {
      await filesStore.uploadAsset(file)
      uiStore.success('Uploaded', file.name)
    } catch (error) {
      uiStore.error('Upload Failed', String(error))
    }
  }
}

/**
 * 处理文件输入
 */
async function handleFileInput(event: Event) {
  const input = event.target as HTMLInputElement
  const files = input.files
  
  if (!files || files.length === 0) return
  
  for (const file of files) {
    try {
      await filesStore.uploadAsset(file)
      uiStore.success('Uploaded', file.name)
    } catch (error) {
      uiStore.error('Upload Failed', String(error))
    }
  }
  
  // 清空 input
  input.value = ''
}
</script>

<style scoped>
.asset-tree {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.tree-content {
  flex: 1;
  overflow-y: auto;
  padding: 8px;
}

.tree-loading,
.tree-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100px;
  color: var(--text-muted);
  font-size: 13px;
}

/* 上传区域 */
.upload-area {
  padding: 10px;
  border-top: 1px solid var(--border-color);
}

.upload-btn {
  width: 100%;
  padding: 10px;
  background: var(--panel-color-2);
  border: 1px dashed var(--border-color);
  border-radius: var(--radius);
  color: var(--text-muted);
  font-size: 13px;
  cursor: pointer;
  transition: all var(--transition-fast);
}

.upload-btn:hover {
  background: rgba(79, 140, 255, 0.1);
  border-color: var(--primary);
  color: var(--primary);
}

.hidden {
  display: none;
}
</style>
