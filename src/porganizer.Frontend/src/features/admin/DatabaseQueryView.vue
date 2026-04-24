<template>
  <v-container fluid>
    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">{{ error }}</v-alert>

    <!-- Toolbar -->
    <v-card class="mb-4">
      <v-card-text>
        <v-row align="start" dense>
          <v-col cols="12" md="3">
            <v-select
              v-model="selectedHistory"
              :items="history"
              label="History"
              density="compact"
              hide-details
              clearable
              no-data-text="No history yet"
              prepend-inner-icon="mdi-history"
              @update:model-value="onHistorySelect"
            >
              <template #append-item>
                <v-divider class="mb-1" />
                <v-list-item
                  title="Clear history"
                  prepend-icon="mdi-delete-outline"
                  density="compact"
                  class="text-error"
                  @click="clearHistory"
                />
              </template>
            </v-select>
          </v-col>

          <v-col cols="12" md="8">
            <v-textarea
              v-model="sql"
              label="SQL"
              density="compact"
              hide-details
              rows="4"
              auto-grow
              class="sql-input"
              placeholder="SELECT * FROM Indexers WHERE Id = 1"
              @keydown.ctrl.enter.prevent="execute"
            />
          </v-col>

          <v-col cols="12" md="1" class="d-flex align-start justify-end pt-1">
            <v-btn
              color="primary"
              variant="tonal"
              size="small"
              :disabled="!sql.trim()"
              :loading="loading"
              prepend-icon="mdi-play"
              title="Run (Ctrl+Enter)"
              @click="execute"
            >Run</v-btn>
          </v-col>
        </v-row>
      </v-card-text>
    </v-card>

    <!-- Results -->
    <v-card v-if="result">
      <v-card-subtitle class="pt-3 pb-1">
        <span v-if="result.columns.length > 0">
          {{ result.rows.length.toLocaleString() }} row{{ result.rows.length === 1 ? '' : 's' }} returned
        </span>
        <span v-else>
          {{ result.rowsAffected >= 0 ? result.rowsAffected.toLocaleString() : '0' }} row{{ result.rowsAffected === 1 ? '' : 's' }} affected
        </span>
      </v-card-subtitle>

      <v-data-table
        v-if="result.columns.length > 0"
        :headers="headers"
        :items="result.rows"
        density="compact"
        class="db-table"
        :items-per-page="50"
        :items-per-page-options="[20, 50, 100, 250, 500].map(n => ({ value: n, title: String(n) }))"
      >
        <template #item="{ item }">
          <tr>
            <td
              v-for="col in result!.columns"
              :key="col"
              class="db-cell text-body-2"
            >
              <span v-if="item[col] === null" class="text-medium-emphasis font-italic">null</span>
              <span v-else class="db-value">{{ item[col] }}</span>
            </td>
          </tr>
        </template>
      </v-data-table>
    </v-card>

    <v-card v-else-if="!loading">
      <v-card-text class="text-medium-emphasis text-body-2">Enter SQL and press Run or Ctrl+Enter.</v-card-text>
    </v-card>
  </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { api, type DatabaseQueryResult } from '../../api'

const HISTORY_KEY = 'db-query-history'
const MAX_HISTORY = 20

const sql     = ref('')
const loading = ref(false)
const error   = ref<string | null>(null)
const result  = ref<DatabaseQueryResult | null>(null)
const history = ref<string[]>([])
const selectedHistory = ref<string | null>(null)

const headers = computed(() =>
  (result.value?.columns ?? []).map(col => ({
    title: col,
    key: col,
    sortable: true,
  }))
)

onMounted(() => {
  const stored = localStorage.getItem(HISTORY_KEY)
  if (stored) {
    try { history.value = JSON.parse(stored) } catch { /* ignore */ }
  }
})

function onHistorySelect(value: string | null) {
  if (value) sql.value = value
  selectedHistory.value = null
}

function clearHistory() {
  history.value = []
  localStorage.removeItem(HISTORY_KEY)
}

function pushHistory(query: string) {
  const trimmed = query.trim()
  const updated = [trimmed, ...history.value.filter(h => h !== trimmed)].slice(0, MAX_HISTORY)
  history.value = updated
  localStorage.setItem(HISTORY_KEY, JSON.stringify(updated))
}

async function execute() {
  const query = sql.value.trim()
  if (!query) return
  loading.value = true
  error.value = null
  result.value = null
  try {
    result.value = await api.database.query(query)
    pushHistory(query)
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Query failed.'
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.sql-input :deep(textarea) {
  font-family: monospace;
  font-size: 0.85rem;
}

.db-table :deep(th) {
  white-space: nowrap;
  font-size: 0.72rem;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.db-cell {
  max-width: 320px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  font-family: monospace;
  font-size: 0.8rem;
}

.db-value {
  display: block;
  max-width: 320px;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
