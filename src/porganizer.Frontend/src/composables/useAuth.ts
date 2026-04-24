import { ref } from 'vue'
import { api, type AuthStatus } from '../api'

// Module-level singleton — shared across all components
const authStatus = ref<AuthStatus | null>(null)
let fetchPromise: Promise<void> | null = null

export function useAuth() {
  function fetchStatus(): Promise<void> {
    if (!fetchPromise) {
      fetchPromise = api.auth.status().then(s => { authStatus.value = s })
    }
    return fetchPromise
  }

  function invalidate() {
    authStatus.value = null
    fetchPromise = null
  }

  return { authStatus, fetchStatus, invalidate }
}
