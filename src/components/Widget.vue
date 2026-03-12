<script setup lang="ts">
import { useAppStore } from '../stores/appState';
import { computed } from 'vue';

const store = useAppStore();

const statusColor = computed(() => {
  switch (store.callState) {
    case 'registered': return 'border-green-500';
    case 'reconnecting':
    case 'connecting': return 'border-yellow-500';
    case 'error':
    case 'offline': return 'border-red-500';
    default: return 'border-green-500';
  }
});

const handleClick = () => {
  if (!store.isDragging) {
    store.setExpanded(!store.isExpanded);
  }
};
</script>

<template>
  <div
    data-tauri-drag-region
    class="relative w-14 h-14 rounded-full bg-slate-800 flex items-center justify-center cursor-pointer border-4 transition-all duration-300  overflow-hidden"
    :class="[statusColor, store.isExpanded ? 'scale-0' : 'scale-100']"
    @click="handleClick"
  >
    <div class="pointer-events-none select-none text-2xl">
      📞
    </div>

    <!-- Status Indicator for Operator -->
    <div
      class="absolute bottom-0 right-0 w-4 h-4 rounded-full border-2 border-slate-800"
      :class="{
        'bg-green-500': store.operatorStatus === 'online',
        'bg-slate-500': store.operatorStatus === 'offline',
        'bg-orange-500': ['lunch', 'break', 'training'].includes(store.operatorStatus)
      }"
    ></div>
  </div>
</template>

<style scoped>
/* Ensure the widget stays circular and pretty */
</style>
