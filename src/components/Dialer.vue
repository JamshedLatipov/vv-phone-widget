<script setup lang="ts">
import { ref } from 'vue';
import { useAppStore } from '../stores/appState';

const store = useAppStore();
const number = ref('');

const keys = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '*', '0', '#'];

const addDigit = (digit: string) => {
  number.value += digit;
};

const backspace = () => {
  number.value = number.value.slice(0, -1);
};

const call = () => {
  if (number.value) {
    store.remoteNumber = number.value;
    store.setCallState('outgoing');
    // SIP call logic will be triggered by watching the state
  }
};
</script>

<template>
  <div class="p-4 bg-slate-900 text-white rounded-lg shadow-2xl w-72">
    <div class="mb-4 flex items-center bg-slate-800 rounded p-2">
      <input
        v-model="number"
        class="bg-transparent border-none outline-none flex-grow text-xl font-mono"
        placeholder="Enter number..."
        autofocus
      />
      <button @click="backspace" class="ml-2 text-slate-400 hover:text-white">⌫</button>
    </div>

    <div class="grid grid-cols-3 gap-3 mb-6">
      <button
        v-for="key in keys"
        :key="key"
        @click="addDigit(key)"
        class="h-12 w-full bg-slate-800 hover:bg-slate-700 rounded-lg text-xl font-bold flex items-center justify-center transition-colors"
      >
        {{ key }}
      </button>
    </div>

    <button
      @click="call"
      class="w-full h-14 bg-green-600 hover:bg-green-500 rounded-full flex items-center justify-center text-2xl shadow-lg transition-all transform active:scale-95"
    >
      📞
    </button>
  </div>
</template>
