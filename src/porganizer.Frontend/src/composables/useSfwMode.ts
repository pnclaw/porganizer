import { ref } from 'vue'

// Module-level singleton — shared across all components
const sfwMode = ref(false)

export function useSfwMode() {
  return { sfwMode }
}
