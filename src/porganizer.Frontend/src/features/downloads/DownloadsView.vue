<template>
  <v-container style="max-width: 900px">
    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">
      {{ error }}
    </v-alert>

    <v-expand-transition>
      <v-row v-if="!mobile || filterPanelOpen" class="mb-2" align="center">
        <v-col cols="12" sm="5" md="4">
          <v-text-field
            v-model="search"
            prepend-inner-icon="mdi-magnify"
            label="Search"
            clearable
            hide-details
          />
        </v-col>
        <v-col cols="12" sm="4" md="3">
          <v-select
            v-model="statusFilter"
            :items="statusOptions"
            label="Status"
            hide-details
          />
        </v-col>
        <v-col cols="auto">
          <v-switch
            v-model="activeOnly"
            label="Active only"
            color="primary"
            hide-details
          />
        </v-col>
        <v-col cols="auto" class="d-flex ga-2">
          <v-btn
            size="small"
            variant="tonal"
            prepend-icon="mdi-refresh"
            :loading="polling"
            @click="pollNow"
          >
            Poll Now
          </v-btn>
          <v-btn
            size="small"
            variant="tonal"
            color="warning"
            prepend-icon="mdi-delete-sweep"
            :loading="deletingFailed"
            @click="removeFailed"
          >
            Remove Failed
          </v-btn>
          <v-btn
            size="small"
            variant="tonal"
            color="error"
            prepend-icon="mdi-delete-forever"
            :loading="deletingAll"
            @click="confirmResetAll = true"
          >
            Reset All
          </v-btn>
        </v-col>
      </v-row>
    </v-expand-transition>

    <v-dialog v-model="confirmResetAll" max-width="400">
      <v-card>
        <v-card-title class="pt-4">Reset Download Log</v-card-title>
        <v-card-text>This will permanently delete all download log entries. This cannot be undone.</v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="confirmResetAll = false">Cancel</v-btn>
          <v-btn color="error" variant="tonal" :loading="deletingAll" @click="resetAll">Delete All</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <div v-if="loading" class="text-center py-8">
      <v-progress-circular indeterminate color="primary" />
    </div>

    <div v-else-if="logs.length === 0" class="text-center py-8 text-medium-emphasis">
      No downloads found.
    </div>

    <v-list v-else lines="two" class="pa-0">
      <template v-for="(item, index) in logs" :key="item.id">
        <v-list-item
          :ripple="true"
          class="py-3"
          @click="openDetail($event, { item })"
        >
          <template #prepend>
            <v-icon :color="statusColor(item.status)" size="small" class="mr-2">
              mdi-circle
            </v-icon>
          </template>

          <v-list-item-title class="text-truncate font-weight-medium">
            {{ item.nzbName }}
          </v-list-item-title>

          <v-list-item-subtitle class="mt-1">
            {{ item.downloadClientTitle }} · {{ formatDate(item.createdAt) }}
          </v-list-item-subtitle>

          <template
            v-if="item.status === DownloadStatus.Downloading || item.status === DownloadStatus.PostProcessing"
          >
            <v-progress-linear
              :model-value="progressPct(item)"
              color="primary"
              height="4"
              rounded
              class="mt-2"
            />
            <div class="text-caption text-medium-emphasis mt-1">
              {{ formatBytes(item.downloadedBytes) }} / {{ formatBytes(item.totalSizeBytes) }}
            </div>
          </template>

          <template #append>
            <div class="d-flex flex-column align-end ga-1">
              <v-chip :color="statusColor(item.status)" size="x-small" variant="tonal">
                {{ statusLabel(item.status) }}
              </v-chip>
              <span class="text-caption text-medium-emphasis">
                {{ item.totalSizeBytes ? formatBytes(item.totalSizeBytes) : '—' }}
              </span>
            </div>
          </template>
        </v-list-item>

        <v-divider v-if="index < logs.length - 1" />
      </template>
    </v-list>

    <div v-if="totalPages > 1" class="d-flex justify-center mt-4">
      <v-pagination
        v-model="page"
        :length="totalPages"
        :total-visible="5"
        @update:model-value="load"
      />
    </div>

    <!-- Detail dialog -->
    <v-dialog v-model="detailOpen" max-width="560">
      <v-card v-if="detailItem">
        <v-card-title class="pt-4 d-flex align-center justify-space-between">
          <span class="text-truncate mr-2">{{ detailItem.nzbName }}</span>
          <v-chip :color="statusColor(detailItem.status)" size="small" variant="tonal" class="flex-shrink-0">
            {{ statusLabel(detailItem.status) }}
          </v-chip>
        </v-card-title>
        <v-card-subtitle>{{ detailItem.downloadClientTitle }}</v-card-subtitle>

        <v-card-text>
          <v-list density="compact" lines="one">
            <v-list-item v-if="detailItem.clientItemId" title="Client ID" :subtitle="detailItem.clientItemId" />

            <template v-if="detailItem.status === DownloadStatus.Downloading || detailItem.status === DownloadStatus.PostProcessing">
              <v-list-item title="Progress">
                <template #subtitle>
                  <v-progress-linear
                    :model-value="progressPct(detailItem)"
                    color="primary"
                    height="6"
                    rounded
                    class="mt-1 mb-1"
                  />
                  <span class="text-caption">
                    {{ formatBytes(detailItem.downloadedBytes) }} / {{ formatBytes(detailItem.totalSizeBytes) }}
                  </span>
                </template>
              </v-list-item>
            </template>
            <v-list-item v-else-if="detailItem.totalSizeBytes" title="Size" :subtitle="formatBytes(detailItem.totalSizeBytes)" />

            <v-list-item v-if="detailItem.storagePath" title="Storage Path" :lines="false">
              <template #subtitle>
                <span style="word-break: break-all; white-space: normal">{{ detailItem.storagePath }}</span>
              </template>
            </v-list-item>

            <v-list-item v-if="detailItem.errorMessage" title="Error">
              <template #subtitle>
                <span class="text-error">{{ detailItem.errorMessage }}</span>
              </template>
            </v-list-item>

            <v-list-item title="Started" :subtitle="formatDateTime(detailItem.createdAt)" />
            <v-list-item v-if="detailItem.completedAt" title="Completed" :subtitle="formatDateTime(detailItem.completedAt)" />
            <v-list-item v-if="detailItem.lastPolledAt" title="Last polled" :subtitle="formatDateTime(detailItem.lastPolledAt)" />
          </v-list>

          <template v-if="detailItem.files?.length">
            <div class="text-subtitle-2 mt-3 mb-1">Extracted Files</div>
            <v-list density="compact" class="bg-surface-variant rounded">
              <v-list-item
                v-for="file in detailItem.files"
                :key="file.id"
                :title="file.fileName"
                prepend-icon="mdi-file-outline"
                density="compact"
              />
            </v-list>
          </template>
        </v-card-text>

        <template v-if="moveEntries.length">
          <v-divider />
          <v-list density="compact" class="mx-4 my-2 rounded bg-surface-variant" style="max-height: 220px; overflow-y: auto">
            <v-list-item
              v-for="(entry, i) in moveEntries"
              :key="i"
              :prepend-icon="moveEntryIcon(entry.level)"
              :base-color="moveEntryColor(entry.level)"
              density="compact"
            >
              <v-list-item-title class="text-wrap text-body-2">{{ entry.message }}</v-list-item-title>
            </v-list-item>
          </v-list>
        </template>

        <v-card-actions>
          <v-btn
            v-if="detailItem.status === DownloadStatus.Failed && detailItem.clientItemId"
            variant="tonal"
            color="warning"
            prepend-icon="mdi-refresh"
            :loading="rechecking"
            @click="recheck"
          >
            Recheck in client
          </v-btn>
          <v-btn
            v-if="detailItem.status === DownloadStatus.Completed && !detailItem.filesMovedAtUtc"
            variant="tonal"
            color="primary"
            prepend-icon="mdi-folder-move"
            :loading="moving"
            @click="moveFiles"
          >
            Move files
          </v-btn>
          <v-spacer />
          <v-btn variant="text" @click="detailOpen = false">Close</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-container>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { useDisplay } from 'vuetify'
import { api, DownloadStatus, DownloadStatusLabels, type DownloadLog, type MoveLogEntry } from '../../api'
import { usePageAction } from '../../composables/usePageAction'
import { useFilterPanel } from '../../composables/useFilterPanel'

const { mobile } = useDisplay()
const { setActions, clearAction } = usePageAction()
const { filterPanelOpen, toggle, closePanel } = useFilterPanel()

const logs           = ref<DownloadLog[]>([])
const total          = ref(0)
const page           = ref(1)
const pageSize       = 50
const loading        = ref(false)
const polling        = ref(false)
const deletingFailed = ref(false)
const deletingAll    = ref(false)
const confirmResetAll = ref(false)
const error          = ref<string | null>(null)

const search       = ref('')
const statusFilter = ref<number | 'all'>('all')
const activeOnly   = ref(false)

const detailOpen    = ref(false)
const detailItem    = ref<DownloadLog | null>(null)
const rechecking    = ref(false)
const moving        = ref(false)
const moveEntries   = ref<MoveLogEntry[]>([])

let pollTimer: ReturnType<typeof setInterval> | null = null

const statusOptions = [
  { title: 'All',              value: 'all' as const },
  { title: 'Queued',           value: DownloadStatus.Queued },
  { title: 'Downloading',      value: DownloadStatus.Downloading },
  { title: 'Post-processing',  value: DownloadStatus.PostProcessing },
  { title: 'Completed',        value: DownloadStatus.Completed },
  { title: 'Failed',           value: DownloadStatus.Failed },
]

const totalPages = computed(() => Math.ceil(total.value / pageSize))

watch([search, statusFilter, activeOnly], () => {
  page.value = 1
  load()
})

async function pollNow() {
  polling.value = true
  error.value = null
  try {
    await api.downloadLogs.poll()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    polling.value = false
  }
}

async function removeFailed() {
  deletingFailed.value = true
  error.value = null
  try {
    await api.downloadLogs.deleteFailed()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    deletingFailed.value = false
  }
}

async function resetAll() {
  deletingAll.value = true
  error.value = null
  try {
    await api.downloadLogs.deleteAll()
    confirmResetAll.value = false
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    deletingAll.value = false
  }
}

async function load() {
  loading.value = true
  error.value = null
  try {
    const result = await api.downloadLogs.list({
      search:     search.value || undefined,
      status:     statusFilter.value !== 'all' ? statusFilter.value : undefined,
      activeOnly: activeOnly.value || undefined,
      page:       page.value,
      pageSize,
    })
    logs.value  = result.items
    total.value = result.total
  } catch (e: any) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

async function recheck() {
  if (!detailItem.value) return
  rechecking.value = true
  error.value = null
  try {
    const updated = await api.downloadLogs.recheck(detailItem.value.id)
    detailItem.value = updated
    const idx = logs.value.findIndex(l => l.id === updated.id)
    if (idx !== -1) logs.value[idx] = updated
  } catch (e: any) {
    error.value = e.message
  } finally {
    rechecking.value = false
  }
}

async function moveFiles() {
  if (!detailItem.value) return
  moving.value = true
  moveEntries.value = []
  error.value = null
  try {
    const result = await api.downloadLogs.move(detailItem.value.id)
    detailItem.value = result.log
    moveEntries.value = result.entries
    const idx = logs.value.findIndex(l => l.id === result.log.id)
    if (idx !== -1) logs.value[idx] = result.log
  } catch (e: any) {
    error.value = e.message
  } finally {
    moving.value = false
  }
}

function moveEntryIcon(level: number): string {
  if (level === 2) return 'mdi-alert-circle'
  if (level === 1) return 'mdi-alert'
  return 'mdi-information'
}

function moveEntryColor(level: number): string {
  if (level === 2) return 'error'
  if (level === 1) return 'warning'
  return 'info'
}

function openDetail(_: Event, { item }: { item: DownloadLog }) {
  detailItem.value = item
  moveEntries.value = []
  detailOpen.value = true
}

function statusLabel(status: DownloadStatus): string {
  return DownloadStatusLabels[status] ?? String(status)
}

function statusColor(status: DownloadStatus): string {
  switch (status) {
    case DownloadStatus.Queued:         return 'default'
    case DownloadStatus.Downloading:    return 'info'
    case DownloadStatus.PostProcessing: return 'warning'
    case DownloadStatus.Completed:      return 'success'
    case DownloadStatus.Failed:         return 'error'
    default:                            return 'default'
  }
}

function progressPct(log: DownloadLog): number {
  if (!log.totalSizeBytes || !log.downloadedBytes) return 0
  return Math.min(100, Math.round((log.downloadedBytes / log.totalSizeBytes) * 100))
}

function formatBytes(bytes: number | null): string {
  if (bytes == null) return '—'
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`
  return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
}

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString()
}

const filtersActive = computed(() =>
  !!search.value || statusFilter.value !== 'all' || activeOnly.value
)

onMounted(() => {
  load()
  pollTimer = setInterval(load, 20_000)
  setActions({ icon: 'mdi-tune', title: 'Toggle filters', onClick: toggle, badgeActive: () => filtersActive.value, mobileOnly: true })
})

onUnmounted(() => {
  if (pollTimer !== null) clearInterval(pollTimer)
  clearAction()
  closePanel()
})
</script>
