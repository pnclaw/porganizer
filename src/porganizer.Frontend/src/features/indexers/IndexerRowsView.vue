<template>
  <v-container fluid>
    <!-- Header -->
    <v-row align="center" class="mb-4">
      <v-col cols="auto">
        <v-btn icon="mdi-arrow-left" variant="text" @click="router.push('/indexers')" />
      </v-col>
      <v-col class="text-right d-flex justify-end ga-2">
        <v-btn
          color="primary"
          prepend-icon="mdi-download"
          :loading="scraping"
          @click="scrapeLatest"
        >
          Get Latest
        </v-btn>
        <v-btn
          color="secondary"
          prepend-icon="mdi-history"
          @click="backfillDialog = true"
        >
          Backfill
        </v-btn>
        <v-btn
          color="error"
          prepend-icon="mdi-delete-sweep"
          :disabled="!filters.indexerId"
          @click="clearDialog = true"
        >
          Clear
        </v-btn>
      </v-col>
    </v-row>

    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">
      {{ error }}
    </v-alert>
    <v-alert v-if="successMsg" type="success" class="mb-4" closable @click:close="successMsg = null">
      {{ successMsg }}
    </v-alert>

    <!-- Filters -->
    <v-card class="mb-4" variant="outlined">
      <v-card-text>
        <v-row dense>
          <v-col cols="12" sm="6" md="3">
            <v-select
              v-model="filters.indexerId"
              label="Indexer"
              :items="indexers"
              item-title="title"
              item-value="id"
              clearable
              hide-details
              @update:model-value="onIndexerChange"
            />
          </v-col>
          <v-col cols="12" sm="6" md="3">
            <v-select
              v-model="filters.categories"
              label="Category"
              :items="availableCategories"
              multiple
              clearable
              hide-details
              :disabled="!filters.indexerId"
            />
          </v-col>
          <v-col cols="12" sm="6" md="3">
            <v-text-field
              v-model="filters.from"
              label="Published from"
              type="date"
              clearable
              hide-details
            />
          </v-col>
          <v-col cols="12" sm="6" md="3">
            <v-text-field
              v-model="filters.to"
              label="Published to"
              type="date"
              clearable
              hide-details
            />
          </v-col>
          <v-col cols="12" sm="6" md="3">
            <v-text-field
              v-model="filters.search"
              label="Title search"
              prepend-inner-icon="mdi-magnify"
              clearable
              hide-details
            />
          </v-col>
          <v-col cols="12" sm="6" md="3">
            <v-text-field
              v-model.number="filters.minSizeGb"
              label="Min size (GB)"
              type="number"
              min="0"
              step="0.1"
              clearable
              hide-details
            />
          </v-col>
          <v-col cols="12" sm="6" md="3">
            <v-text-field
              v-model.number="filters.maxSizeGb"
              label="Max size (GB)"
              type="number"
              min="0"
              step="0.1"
              clearable
              hide-details
            />
          </v-col>
          <v-col cols="12" sm="6" md="3">
            <v-select
              v-model="filters.hasVideoLink"
              label="Video link"
              :items="[{ title: 'Linked', value: true }, { title: 'Unlinked', value: false }]"
              item-title="title"
              item-value="value"
              clearable
              hide-details
            />
          </v-col>
          <v-col cols="12" sm="6" md="3" class="d-flex align-center ga-2">
            <v-btn color="primary" @click="applyFilters">Apply</v-btn>
            <v-btn variant="text" @click="resetFilters">Reset</v-btn>
          </v-col>
        </v-row>
      </v-card-text>
    </v-card>

    <!-- Table -->
    <v-data-table-server
      v-model:items-per-page="pagination.pageSize"
      :headers="headers"
      :items="rows"
      :items-length="totalRows"
      :loading="loading"
      :page="pagination.page"
      item-value="id"
      hover
      @update:page="onPageChange"
      @update:items-per-page="onPageSizeChange"
    >
      <template #item.title="{ item }">
        <div class="d-flex align-center ga-2">
          <v-icon
            v-if="item.prdbVideoId"
            size="small"
            color="success"
            title="Linked to a video"
          >mdi-link-variant</v-icon>
          <v-icon
            v-else
            size="small"
            color="surface-variant"
            title="No video match"
          >mdi-link-variant-off</v-icon>
          <span class="text-truncate flex-grow-1" style="min-width: 0" :title="item.title">
            {{ item.title }}
          </span>
        </div>
      </template>
      <template #item.nzbSize="{ item }">
        {{ formatSize(item.nzbSize) }}
      </template>
      <template #item.fileSize="{ item }">
        {{ item.fileSize != null ? formatSize(item.fileSize) : '—' }}
      </template>
      <template #item.nzbPublishedAt="{ item }">
        {{ item.nzbPublishedAt ? formatDate(item.nzbPublishedAt) : '—' }}
      </template>
      <template #item.nzbUrl="{ item }">
        <a :href="item.nzbUrl" target="_blank" rel="noopener">
          <v-icon size="small">mdi-download</v-icon>
        </a>
      </template>
      <template #item.send="{ item }">
        <v-btn
          icon="mdi-send"
          size="small"
          variant="text"
          color="primary"
          :loading="sendingRowId === item.id"
          :disabled="sendingRowId !== null || usenetClients.length === 0"
          :title="usenetClients.length === 0 ? 'No enabled download clients' : 'Send to download client'"
          @click="handleSend(item)"
        />
      </template>
    </v-data-table-server>

    <!-- Client picker dialog -->
    <v-dialog v-model="pickerDialog" max-width="380" persistent>
      <v-card title="Send to Download Client">
        <v-card-text>
          <v-list lines="one" density="compact">
            <v-list-item
              v-for="client in usenetClients"
              :key="client.id"
              :title="client.title"
              :subtitle="clientTypeLabel(client.clientType)"
              rounded="lg"
              @click="sendToClient(client.id)"
            >
              <template #append>
                <v-icon>mdi-chevron-right</v-icon>
              </template>
            </v-list-item>
          </v-list>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="pickerDialog = false">Cancel</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Toast -->
    <v-snackbar v-model="snackbar" :color="snackbarColor" timeout="4000" location="bottom right">
      {{ snackbarText }}
    </v-snackbar>

    <!-- Backfill dialog -->
    <v-dialog v-model="backfillDialog" max-width="400" persistent>
      <v-card title="Backfill">
        <v-card-text>
          <v-text-field
            v-model.number="backfillPages"
            label="Number of pages"
            type="number"
            min="1"
            :rules="[v => v >= 1 || 'Must be at least 1']"
            autofocus
          />
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="backfillDialog = false">Cancel</v-btn>
          <v-btn color="primary" :loading="backfilling" @click="runBackfill">Run</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Clear confirmation dialog -->
    <v-dialog v-model="clearDialog" max-width="420" persistent>
      <v-card title="Clear Rows">
        <v-card-text>
          This will permanently delete <strong>all rows</strong> for the selected indexer. This cannot be undone.
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="clearDialog = false">Cancel</v-btn>
          <v-btn color="error" :loading="clearing" @click="clearRows">Delete All</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-container>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { api, type Indexer, type IndexerRow, type DownloadClient, ClientType } from '../../api'

const USENET_TYPES = new Set([ClientType.Sabnzbd, ClientType.Nzbget])

const router = useRouter()
const route = useRoute()

const indexers = ref<Indexer[]>([])
const rows = ref<IndexerRow[]>([])
const totalRows = ref(0)
const loading = ref(false)
const error = ref<string | null>(null)
const successMsg = ref<string | null>(null)
const availableCategories = ref<number[]>([])

const scraping = ref(false)
const backfilling = ref(false)
const clearing = ref(false)
const backfillDialog = ref(false)
const clearDialog = ref(false)
const backfillPages = ref(10)

const usenetClients = ref<DownloadClient[]>([])
const sendingRowId = ref<string | null>(null)
const pickerDialog = ref(false)
const pendingRow = ref<IndexerRow | null>(null)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')

const clientTypeLabels: Record<number, string> = {
  [ClientType.Sabnzbd]: 'SABnzbd',
  [ClientType.Nzbget]: 'NZBGet',
}
function clientTypeLabel(value: number): string {
  return clientTypeLabels[value] ?? String(value)
}

const filters = reactive({
  indexerId: (route.params.id as string) || null as string | null,
  categories: [] as number[],
  from: null as string | null,
  to: null as string | null,
  search: null as string | null,
  minSizeGb: null as number | null,
  maxSizeGb: null as number | null,
  hasVideoLink: null as boolean | null,
})

const pagination = reactive({ page: 1, pageSize: 50 })

const headers = [
  { title: 'Title', key: 'title', sortable: false },
  { title: 'Category', key: 'category', width: '110px', sortable: false },
  { title: 'NZB Size', key: 'nzbSize', width: '110px', sortable: false },
  { title: 'File Size', key: 'fileSize', width: '110px', sortable: false },
  { title: 'Published', key: 'nzbPublishedAt', width: '160px', sortable: false },
  { title: 'NZB', key: 'nzbUrl', width: '60px', sortable: false, align: 'center' as const },
  { title: '', key: 'send', width: '48px', sortable: false, align: 'center' as const },
]

function formatSize(bytes: number): string {
  if (bytes >= 1_073_741_824) return (bytes / 1_073_741_824).toFixed(2) + ' GB'
  if (bytes >= 1_048_576) return (bytes / 1_048_576).toFixed(1) + ' MB'
  return (bytes / 1024).toFixed(0) + ' KB'
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString()
}

async function fetchRows() {
  if (!filters.indexerId) {
    rows.value = []
    totalRows.value = 0
    return
  }
  loading.value = true
  try {
    const result = await api.indexers.rows(filters.indexerId, {
      page: pagination.page,
      pageSize: pagination.pageSize,
      search: filters.search || undefined,
      categories: filters.categories.length ? filters.categories : undefined,
      from: filters.from || undefined,
      to: filters.to || undefined,
      minSize: filters.minSizeGb ? Math.round(filters.minSizeGb * 1_073_741_824) : undefined,
      maxSize: filters.maxSizeGb ? Math.round(filters.maxSizeGb * 1_073_741_824) : undefined,
      hasVideoLink: filters.hasVideoLink ?? undefined,
    })
    rows.value = result.items
    totalRows.value = result.total
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load rows'
  } finally {
    loading.value = false
  }
}

async function loadCategories() {
  if (!filters.indexerId) return
  try {
    availableCategories.value = await api.indexers.rowCategories(filters.indexerId)
  } catch {
    // non-critical
  }
}

async function onIndexerChange() {
  pagination.page = 1
  filters.categories = []
  availableCategories.value = []
  await Promise.all([fetchRows(), loadCategories()])
}

function applyFilters() {
  pagination.page = 1
  fetchRows()
}

function resetFilters() {
  filters.categories = []
  filters.from = null
  filters.to = null
  filters.search = null
  filters.minSizeGb = null
  filters.maxSizeGb = null
  filters.hasVideoLink = null
  pagination.page = 1
  fetchRows()
}

function onPageChange(page: number) {
  pagination.page = page
  fetchRows()
}

function onPageSizeChange(size: number) {
  pagination.pageSize = size
  pagination.page = 1
  fetchRows()
}

async function scrapeLatest() {
  if (!filters.indexerId) return
  scraping.value = true
  error.value = null
  try {
    const result = await api.indexers.scrape(filters.indexerId)
    successMsg.value = `Scrape complete — ${result.newRows} new row${result.newRows === 1 ? '' : 's'} saved.`
    await Promise.all([fetchRows(), loadCategories()])
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Scrape failed'
  } finally {
    scraping.value = false
  }
}

async function runBackfill() {
  if (!filters.indexerId || backfillPages.value < 1) return
  backfilling.value = true
  error.value = null
  try {
    const result = await api.indexers.backfill(filters.indexerId, backfillPages.value)
    successMsg.value = `Backfill complete — ${result.newRows} new row${result.newRows === 1 ? '' : 's'} saved.`
    backfillDialog.value = false
    await Promise.all([fetchRows(), loadCategories()])
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Backfill failed'
  } finally {
    backfilling.value = false
  }
}

async function clearRows() {
  if (!filters.indexerId) return
  clearing.value = true
  error.value = null
  try {
    await api.indexers.clearRows(filters.indexerId)
    successMsg.value = 'All rows cleared.'
    clearDialog.value = false
    rows.value = []
    totalRows.value = 0
    availableCategories.value = []
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Clear failed'
  } finally {
    clearing.value = false
  }
}

function handleSend(row: IndexerRow) {
  if (usenetClients.value.length === 1) {
    sendToClient(usenetClients.value[0].id, row)
  } else {
    pendingRow.value = row
    pickerDialog.value = true
  }
}

async function sendToClient(clientId: string, row?: IndexerRow) {
  const target = row ?? pendingRow.value
  if (!target) return
  pickerDialog.value = false
  sendingRowId.value = target.id
  try {
    const result = await api.downloadClients.send(clientId, target.nzbUrl, target.title, target.indexerId, target.id)
    snackbarText.value = result.message
    snackbarColor.value = result.success ? 'success' : 'error'
    snackbar.value = true
  } catch (e) {
    snackbarText.value = e instanceof Error ? e.message : 'Send failed'
    snackbarColor.value = 'error'
    snackbar.value = true
  } finally {
    sendingRowId.value = null
    pendingRow.value = null
  }
}

onMounted(async () => {
  const [allIndexers, allClients] = await Promise.all([
    api.indexers.list(),
    api.downloadClients.list(),
  ])
  indexers.value = allIndexers
  usenetClients.value = allClients.filter(c => c.isEnabled && USENET_TYPES.has(c.clientType))
  if (filters.indexerId) {
    await Promise.all([fetchRows(), loadCategories()])
  }
})
</script>
