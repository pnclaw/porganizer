import { ref } from 'vue'

// Module-level singleton — shared across all components
const filterPanelOpen = ref(false)

export function useFilterPanel() {
  function toggle() {
    filterPanelOpen.value = !filterPanelOpen.value
  }

  function closePanel() {
    filterPanelOpen.value = false
  }

  return { filterPanelOpen, toggle, closePanel }
}
