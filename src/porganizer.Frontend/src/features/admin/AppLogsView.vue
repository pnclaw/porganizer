<template>
  <v-container>
    <!-- Delete confirmation dialog -->
    <v-dialog v-model="confirmDialog" max-width="420">
      <v-card>
        <v-card-title class="text-body-1 font-weight-medium pt-4 pb-1">Confirm deletion</v-card-title>
        <v-card-text class="text-body-2">{{ confirmMessage }}</v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="confirmDialog = false">Cancel</v-btn>
          <v-btn color="error" variant="tonal" :loading="deleting" @click="executeDelete">Delete</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">{{ error }}</v-alert>

    <!-- Toolbar -->
    <v-card class="mb-4">
      <v-card-text>
        <v-row align="center" dense>
          <v-col cols="12" md="3">
            <v-select
              v-model="selectedFile"
              :items="files"
              item-title="filename"
              item-value="filename"
              label="Log file"
              density="compact"
              hide-details
              :loading="filesLoading"
              no-data-text="No log files found"
              @update:model-value="fetchLines"
            />
          </v-col>

          <v-col cols="12" md="3">
            <v-text-field
              v-model="search"
              label="Search"
              density="compact"
              hide-details
              clearable
              prepend-inner-icon="mdi-magnify"
              @update:model-value="onSearchChange"
            />
          </v-col>

          <v-col cols="12" md="3">
            <div class="d-flex align-center ga-1 flex-wrap">
              <v-chip
                v-for="lv in LOG_LEVELS"
                :key="lv.code"
                :color="selectedLevels.includes(lv.code) ? lv.color : undefined"
                :variant="selectedLevels.includes(lv.code) ? 'tonal' : 'outlined'"
                size="small"
                class="level-chip"
                @click="toggleLevel(lv.code)"
              >{{ lv.label }}</v-chip>
            </div>
          </v-col>

          <v-col cols="12" md="3" class="d-flex ga-2 flex-wrap justify-end">
            <v-btn
              size="small"
              color="error"
              variant="tonal"
              :disabled="!files.length"
              @click="openConfirm('today')"
            >Keep today</v-btn>
            <v-btn
              size="small"
              color="error"
              variant="tonal"
              :disabled="!files.length"
              @click="openConfirm('last7')"
            >Keep 7 days</v-btn>
            <v-btn
              size="small"
              color="error"
              variant="tonal"
              :disabled="!files.length"
              @click="openConfirm('all')"
            >Delete all</v-btn>
          </v-col>
        </v-row>
      </v-card-text>
    </v-card>

    <!-- Lines -->
    <v-card v-if="selectedFile">
      <v-card-subtitle class="pt-3 pb-1">
        <template v-if="linesData">
          <span v-if="isFiltered">
            {{ linesData.matchedLines }} of {{ linesData.totalLines }} lines match
          </span>
          <span v-else>{{ linesData.totalLines }} lines</span>
          &nbsp;·&nbsp;{{ selectedFile }}
        </template>
      </v-card-subtitle>

      <v-card-text class="pa-0">
        <v-progress-linear v-if="linesLoading" indeterminate />
        <div v-else-if="linesData && linesData.lines.length === 0" class="pa-4 text-medium-emphasis text-body-2">
          No matching lines.
        </div>
        <div v-else-if="linesData" class="log-scroll">
          <div
            v-for="(line, i) in linesData.lines"
            :key="i"
            :class="['log-line', levelClass(line)]"
          >{{ line }}</div>
        </div>
      </v-card-text>
    </v-card>

    <v-card v-else-if="!filesLoading">
      <v-card-text class="text-medium-emphasis text-body-2">
        No log files found. Logs appear here once the application writes its first log entry.
      </v-card-text>
    </v-card>
  </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { api, type AppLogFile, type AppLogLines } from '../../api'

const LOG_LEVELS = [
  { code: 'VRB', label: 'VRB', color: 'grey' },
  { code: 'DBG', label: 'DBG', color: 'blue' },
  { code: 'INF', label: 'INF', color: 'green' },
  { code: 'WRN', label: 'WRN', color: 'orange' },
  { code: 'ERR', label: 'ERR', color: 'red' },
  { code: 'FTL', label: 'FTL', color: 'deep-purple' },
]

const files          = ref<AppLogFile[]>([])
const selectedFile   = ref<string | null>(null)
const search         = ref('')
const selectedLevels = ref<string[]>([])
const linesData      = ref<AppLogLines | null>(null)
const filesLoading   = ref(false)
const linesLoading   = ref(false)
const deleting       = ref(false)
const error          = ref<string | null>(null)
const confirmDialog  = ref(false)
const confirmMessage = ref('')
const pendingRetain  = ref<'all' | 'last7' | 'today' | null>(null)

const isFiltered = computed(() => !!search.value || selectedLevels.value.length > 0)

let debounceTimer: ReturnType<typeof setTimeout> | null = null

onMounted(fetchFiles)

async function fetchFiles() {
  filesLoading.value = true
  error.value = null
  try {
    files.value = await api.appLogs.list()
    if (files.value.length && !selectedFile.value) {
      selectedFile.value = files.value[0].filename
      await fetchLines()
    }
  } catch {
    error.value = 'Failed to load log files.'
  } finally {
    filesLoading.value = false
  }
}

async function fetchLines() {
  if (!selectedFile.value) return
  linesLoading.value = true
  error.value = null
  try {
    linesData.value = await api.appLogs.getLines(
      selectedFile.value,
      search.value || undefined,
      selectedLevels.value.length ? selectedLevels.value : undefined,
    )
  } catch {
    error.value = 'Failed to load log lines.'
  } finally {
    linesLoading.value = false
  }
}

function onSearchChange() {
  if (debounceTimer) clearTimeout(debounceTimer)
  debounceTimer = setTimeout(fetchLines, 400)
}

const LEVEL_RE = /\[(VRB|DBG|INF|WRN|ERR|FTL)\]/

function levelClass(line: string): string {
  const m = line.match(LEVEL_RE)
  return m ? `log-${m[1].toLowerCase()}` : ''
}

function toggleLevel(code: string) {
  const idx = selectedLevels.value.indexOf(code)
  if (idx === -1) selectedLevels.value.push(code)
  else selectedLevels.value.splice(idx, 1)
  fetchLines()
}

function openConfirm(retain: 'all' | 'last7' | 'today') {
  pendingRetain.value = retain
  confirmMessage.value = {
    all:   'This will delete every log file.',
    last7: 'This will delete all log files older than 7 days.',
    today: 'This will delete all log files except today\'s.',
  }[retain]
  confirmDialog.value = true
}

async function executeDelete() {
  if (!pendingRetain.value) return
  deleting.value = true
  error.value = null
  try {
    await api.appLogs.delete(pendingRetain.value)
    confirmDialog.value = false
    linesData.value = null
    selectedFile.value = null
    search.value = ''
    selectedLevels.value = []
    await fetchFiles()
  } catch {
    error.value = 'Failed to delete log files.'
  } finally {
    deleting.value = false
  }
}
</script>

<style scoped>
.log-scroll {
  overflow-y: auto;
  max-height: 70vh;
}

.log-line {
  font-family: monospace;
  font-size: 0.75rem;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-all;
  padding: 1px 12px;
  border-left: 3px solid transparent;
}

.log-vrb { border-left-color: #9e9e9e; background: rgba(158, 158, 158, 0.04); }
.log-dbg { border-left-color: #2196f3; background: rgba( 33, 150, 243, 0.05); }
.log-inf { border-left-color: #4caf50; background: rgba( 76, 175,  80, 0.04); }
.log-wrn { border-left-color: #ff9800; background: rgba(255, 152,   0, 0.08); }
.log-err { border-left-color: #f44336; background: rgba(244,  67,  54, 0.10); }
.log-ftl { border-left-color: #7b1fa2; background: rgba(123,  31, 162, 0.12); }

.level-chip {
  cursor: pointer;
}
</style>
