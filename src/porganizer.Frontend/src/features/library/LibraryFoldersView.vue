<template>
  <v-container>
    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">
      {{ error }}
    </v-alert>

    <div class="d-flex align-center mb-4 ga-2">
      <v-btn
        prepend-icon="mdi-folder-plus-outline"
        color="primary"
        @click="openAddDialog"
      >
        Add folder
      </v-btn>
      <v-btn
        prepend-icon="mdi-refresh"
        variant="tonal"
        :loading="indexingAll"
        @click="triggerIndexAll"
      >
        Index all
      </v-btn>
    </div>

    <div v-if="loading" class="d-flex justify-center py-10">
      <v-progress-circular indeterminate />
    </div>

    <template v-else>
      <v-card v-if="folders.length === 0" variant="outlined" class="pa-8 text-center text-medium-emphasis">
        No library folders configured. Add a folder to start indexing.
      </v-card>

      <v-table v-else>
        <thead>
          <tr>
            <th>Path / Label</th>
            <th class="text-right">Files</th>
            <th class="text-right">Matched</th>
            <th>Last indexed</th>
            <th>Status</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="folder in folders" :key="folder.id">
            <td>
              <div class="text-body-2 font-weight-medium">{{ folder.label ?? folder.path }}</div>
              <div v-if="folder.label" class="text-caption text-medium-emphasis">{{ folder.path }}</div>
            </td>
            <td class="text-right">{{ folder.fileCount }}</td>
            <td class="text-right">{{ folder.matchedCount }}</td>
            <td class="text-caption">{{ folder.lastIndexedAtUtc ? formatDate(folder.lastIndexedAtUtc) : '—' }}</td>
            <td>
              <v-chip
                v-if="folder.indexingStartedAtUtc"
                size="small"
                color="primary"
                prepend-icon="mdi-loading"
                class="indexing-chip"
              >
                Indexing…
              </v-chip>
              <v-chip v-else-if="folder.lastIndexedAtUtc" size="small" color="success">
                Ready
              </v-chip>
              <v-chip v-else size="small" color="warning">
                Not indexed
              </v-chip>
            </td>
            <td class="text-right">
              <v-btn
                icon="mdi-refresh"
                size="small"
                variant="text"
                title="Index now"
                :loading="indexingFolders.has(folder.id)"
                @click="triggerIndex(folder)"
              />
              <v-btn
                icon="mdi-pencil-outline"
                size="small"
                variant="text"
                title="Edit label"
                @click="openEditDialog(folder)"
              />
              <v-btn
                icon="mdi-delete-outline"
                size="small"
                variant="text"
                color="error"
                title="Remove folder"
                @click="confirmDelete(folder)"
              />
            </td>
          </tr>
        </tbody>
      </v-table>
    </template>

    <!-- Add / Edit dialog -->
    <v-dialog v-model="dialog.open" max-width="540">
      <v-card :title="dialog.editId ? 'Edit folder' : 'Add library folder'">
        <v-card-text>
          <v-text-field
            v-model="dialog.path"
            label="Folder path"
            :disabled="!!dialog.editId"
            :hint="dialog.editId ? 'Path cannot be changed after creation.' : ''"
            persistent-hint
            autofocus
          />
          <v-text-field
            v-model="dialog.label"
            label="Label (optional)"
            class="mt-3"
            clearable
          />
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="dialog.open = false">Cancel</v-btn>
          <v-btn
            color="primary"
            :loading="dialog.saving"
            @click="saveDialog"
          >
            {{ dialog.editId ? 'Save' : 'Add' }}
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Delete confirm dialog -->
    <v-dialog v-model="deleteDialog.open" max-width="420">
      <v-card title="Remove folder?">
        <v-card-text>
          This will remove <strong>{{ deleteDialog.folder?.label ?? deleteDialog.folder?.path }}</strong>
          and all indexed file records. Files on disk are not affected.
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="deleteDialog.open = false">Cancel</v-btn>
          <v-btn color="error" :loading="deleteDialog.deleting" @click="doDelete">Remove</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { api, type LibraryFolder } from '../../api'

const folders       = ref<LibraryFolder[]>([])
const loading       = ref(false)
const error         = ref<string | null>(null)
const indexingAll   = ref(false)
const indexingFolders = ref(new Set<string>())

const dialog = ref({
  open: false,
  editId: null as string | null,
  path: '',
  label: '',
  saving: false,
})

const deleteDialog = ref({
  open: false,
  folder: null as LibraryFolder | null,
  deleting: false,
})

let pollTimer: ReturnType<typeof setInterval> | null = null

async function load() {
  loading.value = true
  error.value = null
  try {
    folders.value = await api.libraryFolders.list()
  } catch (e: any) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

async function pollIfIndexing() {
  if (folders.value.some(f => f.indexingStartedAtUtc)) {
    try {
      folders.value = await api.libraryFolders.list()
    } catch { /* ignore */ }
  }
}

function openAddDialog() {
  dialog.value = { open: true, editId: null, path: '', label: '', saving: false }
}

function openEditDialog(folder: LibraryFolder) {
  dialog.value = { open: true, editId: folder.id, path: folder.path, label: folder.label ?? '', saving: false }
}

async function saveDialog() {
  dialog.value.saving = true
  try {
    if (dialog.value.editId) {
      const updated = await api.libraryFolders.update(dialog.value.editId, {
        path: dialog.value.path,
        label: dialog.value.label || null,
      })
      const idx = folders.value.findIndex(f => f.id === dialog.value.editId)
      if (idx !== -1) folders.value[idx] = updated
    } else {
      const created = await api.libraryFolders.create({
        path: dialog.value.path,
        label: dialog.value.label || null,
      })
      folders.value.push(created)
    }
    dialog.value.open = false
  } catch (e: any) {
    error.value = e.message
  } finally {
    dialog.value.saving = false
  }
}

function confirmDelete(folder: LibraryFolder) {
  deleteDialog.value = { open: true, folder, deleting: false }
}

async function doDelete() {
  if (!deleteDialog.value.folder) return
  deleteDialog.value.deleting = true
  try {
    await api.libraryFolders.delete(deleteDialog.value.folder.id)
    folders.value = folders.value.filter(f => f.id !== deleteDialog.value.folder!.id)
    deleteDialog.value.open = false
  } catch (e: any) {
    error.value = e.message
  } finally {
    deleteDialog.value.deleting = false
  }
}

async function triggerIndex(folder: LibraryFolder) {
  indexingFolders.value.add(folder.id)
  try {
    await api.libraryFolders.indexFolder(folder.id)
    // Mark as indexing locally so UI updates immediately
    folder.indexingStartedAtUtc = new Date().toISOString()
  } catch (e: any) {
    error.value = e.message
  } finally {
    indexingFolders.value.delete(folder.id)
  }
}

async function triggerIndexAll() {
  indexingAll.value = true
  try {
    await api.libraryFolders.indexAll()
    folders.value = await api.libraryFolders.list()
  } catch (e: any) {
    error.value = e.message
  } finally {
    indexingAll.value = false
  }
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit',
  })
}

onMounted(() => {
  load()
  pollTimer = setInterval(pollIfIndexing, 3000)
})

onUnmounted(() => {
  if (pollTimer) clearInterval(pollTimer)
})
</script>

<style scoped>
@keyframes spin { to { transform: rotate(360deg); } }
.indexing-chip :deep(.v-icon) { animation: spin 1s linear infinite; }
</style>
