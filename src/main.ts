import { createApp } from "vue";
import { createPinia } from "pinia";
import { MotionPlugin } from "@vueuse/motion";
import "./style.css";
import App from "./App.vue";
import { useAppStore } from "./stores/appState";

const app = createApp(App);
app.use(createPinia());
app.use(MotionPlugin);
app.mount("#app");

// Initialize store from storage
const store = useAppStore();
store.initFromStorage();
