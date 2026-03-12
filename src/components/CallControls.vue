<script setup lang="ts">
import { useAppStore } from '../stores/appState';

const store = useAppStore();

const toggleMute = () => {
  store.isMuted = !store.isMuted;
};

const toggleHold = () => {
  store.isOnHold = !store.isOnHold;
};

const endCall = () => {
  store.setCallState('registered'); // Logic in sipService will handle the actual hangup
};

const transfer = () => {
  store.setCallState('transferring');
};
</script>

<template>
  <div class="flex flex-col items-center p-6 bg-slate-900 text-white rounded-3xl  w-72">
    <div class="text-center mb-8">
      <div class="w-20 h-20 bg-slate-800 rounded-full mx-auto flex items-center justify-center text-4xl mb-4 shadow-inner">
        👤
      </div>
      <div class="text-xl font-bold">{{ store.remoteName || store.remoteNumber }}</div>
      <div class="text-green-500 font-mono text-lg mt-1">
        {{ Math.floor(store.callDuration / 60) }}:{{ String(store.callDuration % 60).padStart(2, '0') }}
      </div>
      <div class="text-xs text-slate-500 uppercase tracking-widest mt-1">{{ store.callState }}</div>
    </div>

    <div class="grid grid-cols-2 gap-4 w-full mb-8">
      <button
        @click="toggleMute"
        class="flex flex-col items-center justify-center p-3 rounded-2xl transition-colors"
        :class="store.isMuted ? 'bg-red-500/20 text-red-500' : 'bg-slate-800 text-slate-300'"
      >
        <span class="text-xl mb-1">{{ store.isMuted ? '🔇' : '🎤' }}</span>
        <span class="text-xs">Mute</span>
      </button>

      <button
        @click="toggleHold"
        class="flex flex-col items-center justify-center p-3 rounded-2xl transition-colors"
        :class="store.isOnHold ? 'bg-yellow-500/20 text-yellow-500' : 'bg-slate-800 text-slate-300'"
      >
        <span class="text-xl mb-1">⏸️</span>
        <span class="text-xs">Hold</span>
      </button>

      <button
        @click="transfer"
        class="flex flex-col items-center justify-center p-3 rounded-2xl bg-slate-800 text-slate-300 hover:bg-slate-700"
      >
        <span class="text-xl mb-1">↗️</span>
        <span class="text-xs">Transfer</span>
      </button>

      <button
        class="flex flex-col items-center justify-center p-3 rounded-2xl bg-slate-800 text-slate-300 hover:bg-slate-700"
      >
        <span class="text-xl mb-1">⌨️</span>
        <span class="text-xs">Keypad</span>
      </button>
    </div>

    <button
      @click="endCall"
      class="w-16 h-16 bg-red-600 hover:bg-red-500 rounded-full flex items-center justify-center text-3xl shadow-lg transition-all transform hover:scale-110 active:scale-95 shadow-red-900/40"
    >
      📞
    </button>
  </div>
</template>
