<!--
  @file components/assets/TreeNode.vue
  @description 文件树节点组件（递归）
-->

<template>
  <div class="tree-node" :style="{ paddingLeft: `${depth * 12}px` }">
    <!-- 节点行 -->
    <div 
      class="node-row"
      :class="{ folder: isFolder }"
      @click="handleClick"
      @contextmenu.prevent="showContextMenu"
    >
      <!-- 展开/折叠图标 -->
      <span v-if="isFolder" class="node-toggle" @click.stop="toggle">
        {{ expanded ? '▼' : '▶' }}
      </span>
      <span v-else class="node-spacer"></span>
      
      <!-- 文件图标 -->
      <span class="node-icon">{{ icon }}</span>
      
      <!-- 文件名 -->
      <span class="node-name">{{ node.name }}</span>
    </div>
    
    <!-- 子节点 -->
    <div v-if="isFolder && expanded && node.children" class="node-children">
      <TreeNode
        v-for="child in node.children"
        :key="child.path"
        :node="child"
        :depth="depth + 1"
        @select="$emit('select', $event)"
        @delete="$emit('delete', $event)"
      />
    </div>
    
    <!-- 右键菜单 -->
    <Teleport to="body">
      <div 
        v-if="contextMenuVisible"
        class="context-menu"
        :style="contextMenuStyle"
        @click.stop
      >
        <button @click="handleDelete">🗑 Delete</button>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import type { FileNode } from '@/types'

// ============================================
// Props 和 Emits
// ============================================

const props = defineProps<{
  node: FileNode
  depth: number
}>()

const emit = defineEmits<{
  (e: 'select', path: string): void
  (e: 'delete', path: string): void
}>()

// ============================================
// 状态
// ============================================

const expanded = ref(true) // 默认展开
const contextMenuVisible = ref(false)
const contextMenuStyle = ref({ left: '0px', top: '0px' })

// ============================================
// 计算属性
// ============================================

const isFolder = computed(() => props.node.kind === 'folder')

const icon = computed(() => {
  if (isFolder.value) {
    return expanded.value ? '📂' : '📁'
  }
  
  // 根据扩展名显示不同图标
  const ext = props.node.name.split('.').pop()?.toLowerCase()
  const iconMap: Record<string, string> = {
    cs: '📄',
    json: '📋',
    bin: '📦',
    diver: '⚡',
    h: '📝',
    c: '📝'
  }
  
  return iconMap[ext || ''] || '📄'
})

// ============================================
// 方法
// ============================================

function toggle() {
  if (isFolder.value) {
    expanded.value = !expanded.value
  }
}

function handleClick() {
  if (isFolder.value) {
    toggle()
  } else {
    emit('select', props.node.path)
  }
}

function showContextMenu(event: MouseEvent) {
  contextMenuVisible.value = true
  contextMenuStyle.value = {
    left: `${event.clientX}px`,
    top: `${event.clientY}px`
  }
  
  // 点击其他地方关闭菜单
  const closeMenu = () => {
    contextMenuVisible.value = false
    document.removeEventListener('click', closeMenu)
  }
  
  setTimeout(() => {
    document.addEventListener('click', closeMenu)
  }, 0)
}

function handleDelete() {
  contextMenuVisible.value = false
  emit('delete', props.node.path)
}
</script>

<style scoped>
.tree-node {
  user-select: none;
}

.node-row {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 4px 8px;
  border-radius: var(--radius-sm);
  cursor: pointer;
  transition: background var(--transition-fast);
}

.node-row:hover {
  background: rgba(255, 255, 255, 0.05);
}

.node-row.folder {
  font-weight: 500;
}

.node-toggle {
  width: 12px;
  font-size: 10px;
  color: var(--text-muted);
}

.node-spacer {
  width: 12px;
}

.node-icon {
  font-size: 14px;
}

.node-name {
  font-size: 13px;
  color: var(--text-color);
}

/* 右键菜单 */
.context-menu {
  position: fixed;
  min-width: 120px;
  background: var(--panel-color-2);
  border: 1px solid var(--border-color);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  padding: 4px;
  z-index: 1000;
}

.context-menu button {
  width: 100%;
  padding: 6px 12px;
  background: none;
  border: none;
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-size: 13px;
  text-align: left;
  cursor: pointer;
}

.context-menu button:hover {
  background: rgba(255, 255, 255, 0.1);
}
</style>
