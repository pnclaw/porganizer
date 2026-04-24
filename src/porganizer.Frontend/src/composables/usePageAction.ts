import { ref } from 'vue'

export interface PageAction {
  icon: string
  title: string
  onClick: () => void
  badgeActive?: () => boolean
  mobileOnly?: boolean
}

// Module-level singleton — shared across all components
const pageActions = ref<PageAction[]>([])
const pageActionLoading = ref(false)

export function usePageAction() {
  function setActions(...actions: PageAction[]) {
    pageActions.value = actions
    pageActionLoading.value = false
  }

  // Convenience for a single action
  function setAction(icon: string, title: string, onClick: () => void) {
    setActions({ icon, title, onClick })
  }

  function clearAction() {
    pageActions.value = []
    pageActionLoading.value = false
  }

  function setActionLoading(loading: boolean) {
    pageActionLoading.value = loading
  }

  return { pageActions, pageActionLoading, setAction, setActions, clearAction, setActionLoading }
}
