import { defineStore } from 'pinia';
import { ref, computed } from 'vue';

export type CallState =
  | 'offline'
  | 'connecting'
  | 'registered'
  | 'incoming'
  | 'outgoing'
  | 'ringing'
  | 'in-call'
  | 'on-hold'
  | 'transferring'
  | 'reconnecting'
  | 'error';

export type OperatorStatus =
  | 'online'
  | 'offline'
  | 'lunch'
  | 'break'
  | 'training';

export interface CallRecord {
  id: string;
  number: string;
  name?: string;
  direction: 'incoming' | 'outgoing' | 'missed';
  timestamp: number;
  duration?: number;
  status: string;
}

export const useAppStore = defineStore('app', () => {
  // UI State
  const isExpanded = ref(false);
  const isDragging = ref(false);

  // SIP / Call State
  const callState = ref<CallState>('offline');
  const remoteNumber = ref('');
  const remoteName = ref('');
  const callDuration = ref(0);
  const isMuted = ref(false);
  const isOnHold = ref(false);

  // Operator Status
  const operatorStatus = ref<OperatorStatus>('online');
  const statusTimer = ref<number | null>(null);
  const statusEndTime = ref<number | null>(null);

  // History
  const callHistory = ref<CallRecord[]>([]);

  // Computed
  const statusTimeRemaining = computed(() => {
    if (!statusEndTime.value) return 0;
    return Math.max(0, Math.floor((statusEndTime.value - Date.now()) / 1000));
  });

  // Actions
  function setExpanded(val: boolean) {
    isExpanded.value = val;
  }

  function setCallState(state: CallState) {
    callState.value = state;
  }

  function setOperatorStatus(status: OperatorStatus, durationMinutes?: number) {
    operatorStatus.value = status;

    if (statusTimer.value) {
      clearInterval(statusTimer.value);
      statusTimer.value = null;
    }

    if (durationMinutes && ['lunch', 'break', 'training'].includes(status)) {
      const durationMs = durationMinutes * 60 * 1000;
      statusEndTime.value = Date.now() + durationMs;
      localStorage.setItem('operatorStatus', status);
      localStorage.setItem('statusEndTime', statusEndTime.value.toString());

      startTimer();
    } else {
      statusEndTime.value = null;
      localStorage.setItem('operatorStatus', status);
      localStorage.removeItem('statusEndTime');
    }
  }

  function startTimer() {
    if (statusTimer.value) clearInterval(statusTimer.value);
    statusTimer.value = window.setInterval(() => {
      if (statusEndTime.value && Date.now() >= statusEndTime.value) {
        setOperatorStatus('online');
      }
    }, 1000);
  }

  function addToHistory(record: Omit<CallRecord, 'id'>) {
    const newRecord: CallRecord = {
      ...record,
      id: Math.random().toString(36).substring(2, 9)
    };
    callHistory.value.unshift(newRecord);
    if (callHistory.value.length > 50) {
      callHistory.value.pop();
    }
    saveHistory();
  }

  function saveHistory() {
    localStorage.setItem('callHistory', JSON.stringify(callHistory.value));
  }

  function loadHistory() {
    const saved = localStorage.getItem('callHistory');
    if (saved) {
      try {
        callHistory.value = JSON.parse(saved);
      } catch (e) {
        console.error('Failed to load history', e);
      }
    }
  }

  function initFromStorage() {
    loadHistory();
    const savedStatus = localStorage.getItem('operatorStatus') as OperatorStatus;
    const savedEndTime = localStorage.getItem('statusEndTime');

    if (savedStatus) {
      operatorStatus.value = savedStatus;
    }

    if (savedEndTime) {
      const endTime = parseInt(savedEndTime);
      if (Date.now() < endTime) {
        statusEndTime.value = endTime;
        startTimer();
      } else {
        operatorStatus.value = 'online';
        localStorage.setItem('operatorStatus', 'online');
        localStorage.removeItem('statusEndTime');
      }
    }
  }

  return {
    isExpanded, isDragging,
    callState, remoteNumber, remoteName, callDuration, isMuted, isOnHold,
    operatorStatus, statusEndTime, statusTimeRemaining,
    callHistory,
    setExpanded, setCallState, setOperatorStatus, addToHistory, loadHistory, initFromStorage
  };
});
