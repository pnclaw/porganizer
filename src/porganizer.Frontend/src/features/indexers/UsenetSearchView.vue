<template>
  <v-container fluid>
    <!-- Filters -->
    <v-card class="mb-4" variant="outlined">
      <v-card-text>
        <v-row dense align="center">
          <v-col cols="12" sm="6" md="4">
            <v-text-field
              v-model="search"
              label="Search by title"
              prepend-inner-icon="mdi-magnify"
              clearable
              hide-details
              @keyup.enter="applyFilters"
              @click:clear="onClearSearch"
            />
          </v-col>
          <v-col cols="12" sm="6" md="4">
            <v-select
              v-model="selectedIndexerIds"
              label="Indexer"
              :items="indexers"
              item-title="title"
              item-value="id"
              multiple
              clearable
              hide-details
            />
          </v-col>
          <v-col cols="12" sm="auto" class="d-flex align-center ga-2">
            <v-btn-group density="compact" variant="outlined">
              <v-btn
                :color="!previewMode ? 'primary' : undefined"
                :variant="!previewMode ? 'flat' : 'text'"
                prepend-icon="mdi-format-list-text"
                @click="setMode(false)"
              >Text</v-btn>
              <v-btn
                :color="previewMode ? 'primary' : undefined"
                :variant="previewMode ? 'flat' : 'text'"
                prepend-icon="mdi-image-multiple"
                @click="setMode(true)"
              >Preview</v-btn>
            </v-btn-group>
            <v-btn color="primary" @click="applyFilters">Search</v-btn>
            <v-btn variant="text" @click="resetFilters">Reset</v-btn>
          </v-col>
        </v-row>
      </v-card-text>
    </v-card>

    <!-- Preview mode grid -->
    <template v-if="previewMode">
      <div v-if="loading" class="d-flex justify-center py-12">
        <v-progress-circular indeterminate color="primary" />
      </div>
      <v-row v-else-if="rows.length" dense>
        <v-col
          v-for="row in rows"
          :key="row.id"
          cols="6"
          sm="4"
          md="3"
          lg="2"
        >
          <v-card height="100%" :title="row.title" class="preview-card">
            <template #subtitle>
              <span class="text-caption">{{ row.indexerName }}</span>
            </template>
            <v-img
              v-if="row.previewImageUrl"
              :src="row.previewImageUrl"
              aspect-ratio="16/9"
              cover
              class="mb-1"
            />
            <v-sheet
              v-else
              color="surface-variant"
              :height="120"
              class="d-flex align-center justify-center mb-1"
            >
              <v-icon size="48" color="on-surface-variant">
                {{ row.hasFilehashLink ? 'mdi-fingerprint' : 'mdi-image-off' }}
              </v-icon>
            </v-sheet>
            <v-card-actions class="pa-1 ga-1 flex-wrap">
              <v-chip
                v-if="row.matchedVideoId"
                size="x-small"
                color="success"
                prepend-icon="mdi-link-variant"
                :title="row.matchedVideoTitle ?? undefined"
              >Video</v-chip>
              <v-chip
                v-if="row.hasFilehashLink"
                size="x-small"
                color="info"
                prepend-icon="mdi-fingerprint"
              >Hash</v-chip>
              <v-spacer />
              <v-btn
                icon="mdi-send"
                size="x-small"
                variant="text"
                color="primary"
                :loading="sendingRowId === row.id"
                :disabled="sendingRowId !== null || usenetClients.length === 0"
                :title="usenetClients.length === 0 ? 'No enabled download clients' : 'Send to download client'"
                @click="handleSend(row)"
              />
            </v-card-actions>
          </v-card>
        </v-col>
      </v-row>
      <v-sheet v-else-if="!loading" class="text-center py-12 text-medium-emphasis">
        No results found.
      </v-sheet>

      <!-- Preview mode pagination -->
      <div v-if="totalRows > 0" class="d-flex justify-center mt-4">
        <v-pagination
          v-model="page"
          :length="Math.ceil(totalRows / pageSize)"
          :total-visible="7"
          @update:model-value="fetchRows"
        />
      </div>
    </template>

    <!-- Text mode table -->
    <template v-else>
      <v-data-table-server
        v-model:items-per-page="pageSize"
        :headers="headers"
        :items="rows"
        :items-length="totalRows"
        :loading="loading"
        :page="page"
        item-value="id"
        hover
        @update:page="onPageChange"
        @update:items-per-page="onPageSizeChange"
      >
        <template #item.title="{ item }">
          <div class="d-flex align-center ga-1">
            <v-icon
              v-if="item.matchedVideoId"
              size="small"
              color="success"
              :title="item.matchedVideoTitle ?? 'Linked to video'"
            >mdi-link-variant</v-icon>
            <v-icon
              v-if="item.hasFilehashLink"
              size="small"
              color="info"
              title="Filehash link"
            >mdi-fingerprint</v-icon>
            <span class="text-truncate flex-grow-1" style="min-width: 0" :title="item.title">
              {{ item.title }}
            </span>
          </div>
        </template>
        <template #item.indexerName="{ item }">
          <span class="text-caption">{{ item.indexerName }}</span>
        </template>
        <template #item.nzbSize="{ item }">
          {{ formatSize(item.nzbSize) }}
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
    </template>

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
  </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { api, type UsenetSearchRow, type Indexer, type DownloadClient, ClientType } from '../../api'

const USENET_TYPES = new Set([ClientType.Sabnzbd, ClientType.Nzbget])

const indexers = ref<Indexer[]>([])
const usenetClients = ref<DownloadClient[]>([])

const rows = ref<UsenetSearchRow[]>([])
const totalRows = ref(0)
const loading = ref(false)

const search = ref<string>('')
const selectedIndexerIds = ref<string[]>([])
const previewMode = ref(false)
const page = ref(1)
const pageSize = ref(50)

const sendingRowId = ref<string | null>(null)
const pickerDialog = ref(false)
const pendingRow = ref<UsenetSearchRow | null>(null)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')

const headers = [
  { title: 'Title', key: 'title', sortable: false },
  { title: 'Indexer', key: 'indexerName', width: '140px', sortable: false },
  { title: 'Size', key: 'nzbSize', width: '110px', sortable: false },
  { title: 'Published', key: 'nzbPublishedAt', width: '160px', sortable: false },
  { title: 'NZB', key: 'nzbUrl', width: '60px', sortable: false, align: 'center' as const },
  { title: '', key: 'send', width: '48px', sortable: false, align: 'center' as const },
]

const clientTypeLabels: Record<number, string> = {
  [ClientType.Sabnzbd]: 'SABnzbd',
  [ClientType.Nzbget]: 'NZBGet',
}
function clientTypeLabel(value: number): string {
  return clientTypeLabels[value] ?? String(value)
}

function formatSize(bytes: number): string {
  if (bytes >= 1_073_741_824) return (bytes / 1_073_741_824).toFixed(2) + ' GB'
  if (bytes >= 1_048_576) return (bytes / 1_048_576).toFixed(1) + ' MB'
  return (bytes / 1024).toFixed(0) + ' KB'
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString()
}

async function fetchRows() {
  loading.value = true
  try {
    const result = await api.usenetSearch.search({
      page: page.value,
      pageSize: pageSize.value,
      search: search.value || undefined,
      indexerIds: selectedIndexerIds.value.length ? selectedIndexerIds.value : undefined,
      previewMode: previewMode.value || undefined,
    })
    rows.value = result.items
    totalRows.value = result.total
  } catch {
    rows.value = []
    totalRows.value = 0
  } finally {
    loading.value = false
  }
}

function applyFilters() {
  page.value = 1
  fetchRows()
}

function resetFilters() {
  search.value = ''
  selectedIndexerIds.value = []
  page.value = 1
  fetchRows()
}

function onClearSearch() {
  search.value = ''
  applyFilters()
}

function setMode(preview: boolean) {
  previewMode.value = preview
  page.value = 1
  fetchRows()
}

function onPageChange(p: number) {
  page.value = p
  fetchRows()
}

function onPageSizeChange(size: number) {
  pageSize.value = size
  page.value = 1
  fetchRows()
}

function handleSend(row: UsenetSearchRow) {
  if (usenetClients.value.length === 1) {
    sendToClient(usenetClients.value[0].id, row)
  } else {
    pendingRow.value = row
    pickerDialog.value = true
  }
}

async function sendToClient(clientId: string, row?: UsenetSearchRow) {
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
  await fetchRows()
})
</script>

<style scoped>
.preview-card {
  font-size: 0.8rem;
}
:deep(.preview-card .v-card-title) {
  font-size: 0.8rem;
  line-height: 1.2;
  white-space: normal;
  overflow: hidden;
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}
</style>
