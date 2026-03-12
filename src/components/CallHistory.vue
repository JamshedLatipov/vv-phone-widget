<script setup lang="ts">
import { useAppStore } from '../stores/appState';
import { onMounted } from 'vue';

const store = useAppStore();

onMounted(() => {
  store.loadHistory();
});

const formatDate = (ts: number) => {
  return new Date(ts).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
};

const call = (num: string) => {
  store.remoteNumber = num;
  store.setCallState('outgoing');
};
</script>

<template>
  <div class="p-4 bg-slate-900 text-white rounded-lg shadow-2xl w-72 h-96 overflow-hidden flex flex-col">
    <h3 class="text-sm font-bold text-slate-400 mb-2 uppercase tracking-wider">Recent Calls</h3>

    <div class="flex-grow overflow-y-auto space-y-1 pr-1 custom-scrollbar">
      <div
        v-for="record in store.callHistory"
        :key="record.id"
        class="flex items-center p-2 hover:bg-slate-800 rounded group cursor-pointer"
        @click="call(record.number)"
      >
        <div class="mr-3 text-lg" :class="record.direction === 'missed' ? 'text-red-500' : 'text-slate-400'">
          {{ record.direction === 'incoming' ? '↙️' : record.direction === 'outgoing' ? '↗️' : '❌' }}
        </div>
        <div class="flex-grow">
          <div class="font-medium">{{ record.name || record.number }}</div>
          <div class="text-xs text-slate-500">{{ formatDate(record.timestamp) }}</div>
        </div>
        <button class="opacity-0 group-hover:opacity-100 text-green-500 p-1">📞</button>
      </div>

      <div v-if="store.callHistory.length === 0" class="text-center text-slate-600 mt-10 italic">
        No recent calls
      </div>
    </div>
  </div>
</template>

<style scoped>
.custom-scrollbar::-webkit-scrollbar {
  width: 4px;
}
.custom-scrollbar::-webkit-scrollbar-track {
  background: transparent;
}
.custom-scrollbar::-webkit-scrollbar-thumb {
  background: #334155;
  border-radius: 10px;
}
</style>
