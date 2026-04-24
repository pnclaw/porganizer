<template>
  <v-container style="max-width: 900px">
    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">
      {{ error }}
    </v-alert>

    <v-alert v-if="scrapeResult !== null" type="success" class="mb-4" closable @click:close="scrapeResult = null">
      Scrape complete — {{ scrapeResult }} new row{{ scrapeResult === 1 ? '' : 's' }} saved.
    </v-alert>

    <v-row v-if="loading">
      <v-col cols="12" class="text-center py-8">
        <v-progress-circular indeterminate color="primary" />
      </v-col>
    </v-row>

    <v-row v-else-if="indexers.length === 0">
      <v-col cols="12" class="text-center py-8 text-medium-emphasis">
        No indexers configured.
      </v-col>
    </v-row>

    <v-row v-else>
      <v-col
        v-for="indexer in indexers"
        :key="indexer.id"
        cols="12"
        sm="6"
      >
        <v-card height="100%">
          <v-card-item class="pa-4">
            <v-card-title class="text-h6">{{ indexer.title }}</v-card-title>
            <v-card-subtitle class="text-truncate mt-1">{{ indexer.url }}</v-card-subtitle>
            <template #append>
              <v-chip :color="indexer.isEnabled ? 'success' : 'default'" size="small">
                {{ indexer.isEnabled ? 'Enabled' : 'Disabled' }}
              </v-chip>
            </template>
          </v-card-item>

          <v-card-text class="px-4 pb-2">
            <v-chip size="small" variant="outlined">{{ parsingTypeLabel(indexer.parsingType) }}</v-chip>
            <div class="text-body-2 text-medium-emphasis mt-3">
              Backfill window: {{ indexer.backfillDays }} day<span v-if="indexer.backfillDays !== 1">s</span>
            </div>
          </v-card-text>

          <v-card-actions class="px-4 pb-4">
            <v-btn
              icon="mdi-chart-bar"
              size="small"
              variant="text"
              color="secondary"
              title="Stats"
              @click="router.push(`/indexers/${indexer.id}/stats`)"
            />
            <v-btn
              icon="mdi-table"
              size="small"
              variant="text"
              color="secondary"
              title="View rows"
              @click="router.push(`/indexers/${indexer.id}/rows`)"
            />
            <v-btn
              icon="mdi-download"
              size="small"
              variant="text"
              color="primary"
              title="Scrape latest"
              :loading="scrapingId === indexer.id"
              :disabled="scrapingId !== null"
              @click="scrapeIndexer(indexer.id)"
            />
            <v-spacer />
            <v-btn
              icon="mdi-pencil"
              size="small"
              variant="text"
              @click="openEditDialog(indexer)"
            />
            <v-btn
              icon="mdi-delete"
              size="small"
              variant="text"
              color="error"
              @click="confirmDelete(indexer)"
            />
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>

    <v-dialog v-model="dialog" max-width="500" persistent>
      <v-card :title="editingId ? 'Edit Indexer' : 'New Indexer'">
        <v-card-text>
          <v-form ref="formRef" @submit.prevent="submitForm">
            <v-text-field
              v-model="form.title"
              label="Title"
              :rules="[required]"
              required
              autofocus
              class="mb-2"
            />
            <v-text-field
              v-model="form.url"
              label="URL"
              :rules="[required]"
              required
              class="mb-2"
            />
            <v-select
              v-model="form.parsingType"
              label="Parsing Type"
              :items="parsingTypeOptions"
              item-title="title"
              item-value="value"
              :rules="[requiredSelect]"
              required
              class="mb-2"
            />
            <v-text-field
              v-model="form.apiPath"
              label="API Path"
              placeholder="/api"
              class="mb-2"
            />
            <v-text-field
              v-model="form.apiKey"
              label="API Key"
              class="mb-2"
            />
            <v-text-field
              v-model.number="form.backfillDays"
              label="Backfill Days"
              type="number"
              min="1"
              :rules="[requiredNumber]"
              hint="One-time backfill window for this indexer. Increasing it reopens the backfill for this indexer only."
              persistent-hint
              class="mb-2"
            />
            <v-switch
              v-model="form.isEnabled"
              label="Enabled"
              color="primary"
              hide-details
            />
          </v-form>

          <v-alert
            v-if="testResult"
            :type="testResult.success ? 'success' : 'error'"
            class="mt-4"
            density="compact"
          >
            {{ testResult.message }}
          </v-alert>
        </v-card-text>
        <v-card-actions>
          <v-btn
            variant="outlined"
            :loading="testing"
            prepend-icon="mdi-connection"
            @click="runTest"
          >
            Test
          </v-btn>
          <v-spacer />
          <v-btn variant="text" @click="dialog = false">Cancel</v-btn>
          <v-btn color="primary" :loading="saving" @click="submitForm">
            {{ editingId ? 'Save' : 'Create' }}
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Delete confirmation dialog -->
    <v-dialog v-model="deleteDialog" max-width="400" persistent>
      <v-card title="Delete Indexer">
        <v-card-text>
          Delete <strong>{{ deletingIndexer?.title }}</strong>? This cannot be undone.
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="deleteDialog = false">Cancel</v-btn>
          <v-btn color="error" :loading="deleting" @click="deleteIndexer">Delete</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Save anyway dialog -->
    <v-dialog v-model="saveAnywayDialog" max-width="440" persistent>
      <v-card title="Test Failed">
        <v-card-text>
          <p class="mb-2">{{ testResult?.message }}</p>
          <p>The indexer has been <strong>disabled</strong>. Save anyway?</p>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="saveAnywayDialog = false">Cancel</v-btn>
          <v-btn color="warning" :loading="saving" @click="persistForm">Save Disabled</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { api, type Indexer, ParsingType } from '../../api'
import { usePageAction } from '../../composables/usePageAction'

const router = useRouter()
const { setAction, clearAction } = usePageAction()

const parsingTypeOptions = [
  { title: 'Newznab', value: ParsingType.Newznab },
]

const indexers = ref<Indexer[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const scrapeResult = ref<number | null>(null)
const scrapingId = ref<string | null>(null)
const dialog = ref(false)
const saving = ref(false)
const testing = ref(false)
const saveAnywayDialog = ref(false)
const testResult = ref<{ success: boolean; message: string } | null>(null)
const editingId = ref<string | null>(null)
const formRef = ref()
const deleteDialog = ref(false)
const deleting = ref(false)
const deletingIndexer = ref<Indexer | null>(null)

const emptyForm = () => ({
  title: '',
  url: '',
  apiPath: '',
  parsingType: ParsingType.Newznab,
  isEnabled: true,
  apiKey: '',
  backfillDays: 30,
})

const form = ref(emptyForm())

const required = (v: string) => !!v || 'Required'
const requiredSelect = (v: number | null) => v !== null && v !== undefined ? true : 'Required'
const requiredNumber = (v: number | null) => (v != null && v >= 1) || 'Must be at least 1'

function parsingTypeLabel(value: number): string {
  return parsingTypeOptions.find((o) => o.value === value)?.title ?? String(value)
}

async function fetchIndexers() {
  loading.value = true
  error.value = null
  try {
    indexers.value = await api.indexers.list()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load indexers'
  } finally {
    loading.value = false
  }
}

function openCreateDialog() {
  editingId.value = null
  form.value = emptyForm()
  testResult.value = null
  dialog.value = true
}

function openEditDialog(indexer: Indexer) {
  editingId.value = indexer.id
  form.value = {
    title: indexer.title,
    url: indexer.url,
    apiPath: indexer.apiPath,
    parsingType: indexer.parsingType,
    isEnabled: indexer.isEnabled,
    apiKey: indexer.apiKey,
    backfillDays: indexer.backfillDays,
  }
  testResult.value = null
  dialog.value = true
}

async function runTest() {
  testing.value = true
  testResult.value = null
  try {
    testResult.value = await api.indexers.test({
      url: form.value.url,
      apiPath: form.value.apiPath,
      apiKey: form.value.apiKey,
    })
  } catch (e) {
    testResult.value = { success: false, message: e instanceof Error ? e.message : 'Test failed' }
  } finally {
    testing.value = false
  }
}

async function submitForm() {
  const { valid } = await formRef.value.validate()
  if (!valid) return

  // Always test before saving
  testing.value = true
  try {
    testResult.value = await api.indexers.test({
      url: form.value.url,
      apiPath: form.value.apiPath,
      apiKey: form.value.apiKey,
    })
  } catch (e) {
    testResult.value = { success: false, message: e instanceof Error ? e.message : 'Test failed' }
  } finally {
    testing.value = false
  }

  if (!testResult.value?.success) {
    form.value.isEnabled = false
    saveAnywayDialog.value = true
    return
  }

  await persistForm()
}

async function persistForm() {
  saveAnywayDialog.value = false
  saving.value = true
  try {
    if (editingId.value) {
      await api.indexers.update(editingId.value, form.value)
    } else {
      await api.indexers.create(form.value)
    }
    dialog.value = false
    await fetchIndexers()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to save indexer'
  } finally {
    saving.value = false
  }
}

async function scrapeIndexer(id: string) {
  scrapeResult.value = null
  error.value = null
  scrapingId.value = id
  try {
    const result = await api.indexers.scrape(id)
    scrapeResult.value = result.newRows
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Scrape failed'
  } finally {
    scrapingId.value = null
  }
}

function confirmDelete(indexer: Indexer) {
  deletingIndexer.value = indexer
  deleteDialog.value = true
}

async function deleteIndexer() {
  if (!deletingIndexer.value) return
  deleting.value = true
  error.value = null
  try {
    await api.indexers.delete(deletingIndexer.value.id)
    deleteDialog.value = false
    await fetchIndexers()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to delete indexer'
  } finally {
    deleting.value = false
  }
}

onMounted(() => {
  fetchIndexers()
  setAction('mdi-plus', 'New Indexer', openCreateDialog)
})

onUnmounted(clearAction)
</script>
