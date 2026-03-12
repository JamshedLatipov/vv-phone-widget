<script setup lang="ts">
import { useAppStore } from './stores/appState';
import Widget from './components/Widget.vue';
import Dialer from './components/Dialer.vue';
import CallHistory from './components/CallHistory.vue';
import CallControls from './components/CallControls.vue';
import { ref } from 'vue';

const store = useAppStore();
const activeTab = ref('dialer');

const tabs = [
  { id: 'dialer', icon: '🔢', label: 'Dialer' },
  { id: 'history', icon: '🕒', label: 'History' },
  { id: 'settings', icon: '⚙️', label: 'Settings' }
];

const isInActiveCall = () => {
  return ['in-call', 'on-hold', 'transferring', 'ringing', 'outgoing'].includes(store.callState);
};

const isInIncomingCall = () => {
  return store.callState === 'incoming';
};

const closeExpanded = () => {
  store.setExpanded(false);
};

const setStatus = (status: any, minutes?: number) => {
  store.setOperatorStatus(status, minutes);
};
</script>

<template>
  <div class="fixed inset-0 flex items-center justify-center p-4">
    <!-- Floating Widget -->
    <Widget v-if="!store.isExpanded && !isInIncomingCall() && !isInActiveCall()" />

    <!-- Expanded UI -->
    <div
      v-if="store.isExpanded && !isInIncomingCall() && !isInActiveCall()"
      v-motion
      :initial="{ opacity: 0, scale: 0.8, y: 20 }"
      :enter="{ opacity: 1, scale: 1, y: 0 }"
      class="bg-slate-900 rounded-3xl shadow-2xl overflow-hidden border border-slate-700 w-80"
    >
      <!-- Header -->
      <div class="p-4 border-b border-slate-800 flex items-center justify-between">
        <div class="flex items-center space-x-2">
          <div class="w-3 h-3 rounded-full" :class="store.callState === 'registered' ? 'bg-green-500' : 'bg-red-500'"></div>
          <span class="text-xs font-bold text-slate-400 uppercase">Orbital SIP</span>
        </div>
        <button @click="closeExpanded" class="text-slate-500 hover:text-white">✕</button>
      </div>

      <!-- Status Bar -->
      <div class="bg-slate-800/50 p-3 flex items-center justify-around border-b border-slate-800">
        <button
          v-for="s in ['online', 'break', 'lunch']"
          :key="s"
          @click="setStatus(s, s === 'online' ? undefined : 15)"
          class="px-3 py-1 rounded-full text-xs font-medium transition-colors"
          :class="store.operatorStatus === s ? 'bg-blue-600 text-white' : 'text-slate-400 hover:bg-slate-700'"
        >
          {{ s.toUpperCase() }}
        </button>
      </div>

      <!-- Main Content -->
      <div class="p-4">
        <Dialer v-if="activeTab === 'dialer'" />
        <CallHistory v-if="activeTab === 'history'" />
        <div v-if="activeTab === 'settings'" class="p-4 text-slate-400 italic text-sm">Settings coming soon...</div>
      </div>

      <!-- Footer Tabs -->
      <div class="flex border-t border-slate-800">
        <button
          v-for="tab in tabs"
          :key="tab.id"
          @click="activeTab = tab.id"
          class="flex-1 py-3 flex flex-col items-center justify-center space-y-1 transition-colors"
          :class="activeTab === tab.id ? 'text-blue-500 bg-slate-800/30' : 'text-slate-500 hover:text-slate-300'"
        >
          <span class="text-xl">{{ tab.icon }}</span>
          <span class="text-[10px] uppercase font-bold">{{ tab.label }}</span>
        </button>
      </div>
    </div>

    <!-- Active Call UI -->
    <div
      v-if="isInActiveCall()"
      v-motion
      :initial="{ opacity: 0, scale: 0.9 }"
      :enter="{ opacity: 1, scale: 1 }"
    >
      <CallControls />
    </div>

    <!-- Incoming Call UI -->
    <div
      v-if="isInIncomingCall()"
      v-motion
      :initial="{ y: 100, opacity: 0 }"
      :enter="{ y: 0, opacity: 1 }"
      class="bg-slate-900 border border-slate-700 p-6 rounded-3xl shadow-2xl w-80 text-center"
    >
      <div class="w-16 h-16 bg-blue-600 rounded-full mx-auto flex items-center justify-center text-3xl mb-4 animate-bounce">
        📞
      </div>
      <div class="text-sm text-slate-400 uppercase tracking-widest mb-1">Incoming Call</div>
      <div class="text-2xl font-bold text-white mb-8">{{ store.remoteNumber }}</div>

      <div class="flex justify-around">
        <button
          @click="store.setCallState('registered')"
          class="w-16 h-16 bg-red-600 rounded-full flex items-center justify-center text-2xl shadow-lg shadow-red-900/30 hover:bg-red-500"
        >
          ✕
        </button>
        <button
          @click="store.setCallState('in-call')"
          class="w-16 h-16 bg-green-600 rounded-full flex items-center justify-center text-2xl shadow-lg shadow-green-900/30 hover:bg-green-500"
        >
          ✓
        </button>
      </div>
    </div>
  </div>
</template>

<style>
/* Global styles in main.ts/style.css */
</style>
