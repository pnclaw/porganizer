<template>
  <v-tabs v-model="tab" class="mb-4">
    <v-tab value="browser">
      <v-icon start>mdi-table</v-icon>Table Browser
    </v-tab>
    <v-tab value="query">
      <v-icon start>mdi-code-braces</v-icon>SQL Query
    </v-tab>
  </v-tabs>

  <v-tabs-window v-model="tab">
    <v-tabs-window-item value="browser">
      <v-container fluid>
        <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">{{ error }}</v-alert>

        <!-- Toolbar -->
        <v-card class="mb-4">
          <v-card-text>
            <v-row align="center" dense>
              <v-col cols="12" md="3">
                <v-select
                  v-model="selectedTable"
                  :items="tables"
                  label="Table"
                  density="compact"
                  hide-details
                  :loading="tablesLoading"
                  no-data-text="No tables found"
                  @update:model-value="onTableChange"
                />
              </v-col>

              <v-col cols="12" md="4">
                <v-text-field
                  v-model="whereClause"
                  label="WHERE clause (optional)"
                  density="compact"
                  hide-details
                  clearable
                  placeholder='e.g. Id = 1 OR Title LIKE "%test%"'
                  prepend-inner-icon="mdi-filter-outline"
                  @keyup.enter="runQuery"
                />
              </v-col>

              <v-col cols="12" md="2">
                <v-select
                  v-model="orderByCol"
                  :items="orderByOptions"
                  label="Order by"
                  density="compact"
                  hide-details
                  clearable
                  :disabled="!rowsData"
                />
              </v-col>

              <v-col cols="auto">
                <v-btn-toggle
                  v-model="orderDir"
                  density="compact"
                  variant="outlined"
                  :disabled="!orderByCol"
                >
                  <v-btn value="asc" size="small" title="Ascending">
                    <v-icon>mdi-sort-ascending</v-icon>
                  </v-btn>
                  <v-btn value="desc" size="small" title="Descending">
                    <v-icon>mdi-sort-descending</v-icon>
                  </v-btn>
                </v-btn-toggle>
              </v-col>

              <v-col cols="12" md="1">
                <v-select
                  v-model="pageSize"
                  :items="pageSizeOptions"
                  label="Per page"
                  density="compact"
                  hide-details
                  @update:model-value="onOptionsChange({ page: 1, itemsPerPage: pageSize })"
                />
              </v-col>

              <v-col cols="12" md="1" class="d-flex justify-end">
                <v-btn
                  color="primary"
                  variant="tonal"
                  size="small"
                  :disabled="!selectedTable"
                  :loading="rowsLoading"
                  prepend-icon="mdi-play"
                  @click="runQuery"
                >Run</v-btn>
              </v-col>
            </v-row>
          </v-card-text>
        </v-card>

        <!-- Results -->
        <v-card v-if="selectedTable">
          <v-card-subtitle v-if="rowsData" class="pt-3 pb-1">
            {{ rowsData.total.toLocaleString() }} rows in <strong>{{ selectedTable }}</strong>
            <span v-if="whereClause"> — filtered</span>
          </v-card-subtitle>

          <v-data-table-server
            v-model:items-per-page="pageSize"
            v-model:page="page"
            :headers="headers"
            :items="rowsData?.rows ?? []"
            :items-length="rowsData?.total ?? 0"
            :loading="rowsLoading"
            :items-per-page-options="pageSizeOptions.map(n => ({ value: n, title: String(n) }))"
            density="compact"
            class="db-table"
            @update:options="onOptionsChange"
          >
            <template #item="{ item }">
              <tr>
                <td
                  v-for="col in columns"
                  :key="col"
                  class="db-cell text-body-2"
                >
                  <span v-if="item[col] === null" class="text-medium-emphasis font-italic">null</span>
                  <span v-else class="db-value">{{ item[col] }}</span>
                </td>
              </tr>
            </template>
          </v-data-table-server>
        </v-card>

        <v-card v-else-if="!tablesLoading">
          <v-card-text class="text-medium-emphasis text-body-2">Select a table to browse its rows.</v-card-text>
        </v-card>
      </v-container>
    </v-tabs-window-item>

    <v-tabs-window-item value="query">
      <DatabaseQueryView />
    </v-tabs-window-item>
  </v-tabs-window>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { api, type DatabaseTableRows } from '../../api'
import DatabaseQueryView from './DatabaseQueryView.vue'

const tab = ref('browser')

const tables        = ref<string[]>([])
const selectedTable = ref<string | null>(null)
const whereClause   = ref('')
const orderByCol    = ref<string | null>(null)
const orderDir      = ref<'asc' | 'desc'>('asc')
const page          = ref(1)
const pageSize      = ref(50)
const rowsData      = ref<DatabaseTableRows | null>(null)
const tablesLoading = ref(false)
const rowsLoading   = ref(false)
const error         = ref<string | null>(null)

const pageSizeOptions = [20, 50, 100, 250, 500]

const columns = computed(() => rowsData.value?.columns ?? [])

const orderByOptions = computed(() => columns.value)

const headers = computed(() =>
  columns.value.map(col => ({
    title: col,
    key: col,
    sortable: false,
  }))
)

watch([orderByCol, orderDir], () => { if (rowsData.value) { page.value = 1; fetchRows() } })

onMounted(fetchTables)

async function fetchTables() {
  tablesLoading.value = true
  error.value = null
  try {
    tables.value = await api.database.tables()
  } catch {
    error.value = 'Failed to load tables.'
  } finally {
    tablesLoading.value = false
  }
}

function onTableChange() {
  page.value = 1
  orderByCol.value = null
  rowsData.value = null
  fetchRows()
}

function runQuery() {
  page.value = 1
  fetchRows()
}

function onOptionsChange(opts: { page?: number; itemsPerPage?: number }) {
  if (opts.page !== undefined) page.value = opts.page
  if (opts.itemsPerPage !== undefined) pageSize.value = opts.itemsPerPage
  fetchRows()
}

async function fetchRows() {
  if (!selectedTable.value) return
  rowsLoading.value = true
  error.value = null
  try {
    rowsData.value = await api.database.rows(selectedTable.value, {
      where: whereClause.value || undefined,
      orderBy: orderByCol.value || undefined,
      orderDir: orderByCol.value ? orderDir.value : undefined,
      page: page.value,
      pageSize: pageSize.value,
    })
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Failed to load rows.'
    rowsData.value = null
  } finally {
    rowsLoading.value = false
  }
}
</script>

<style scoped>
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
