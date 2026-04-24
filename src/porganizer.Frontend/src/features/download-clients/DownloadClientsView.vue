<template>
  <v-container style="max-width: 900px">
    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">
      {{ error }}
    </v-alert>

    <v-row v-if="loading">
      <v-col cols="12" class="text-center py-8">
        <v-progress-circular indeterminate color="primary" />
      </v-col>
    </v-row>

    <v-row v-else-if="clients.length === 0">
      <v-col cols="12" class="text-center py-8 text-medium-emphasis">
        No download clients configured.
      </v-col>
    </v-row>

    <v-row v-else>
      <v-col
        v-for="client in clients"
        :key="client.id"
        cols="12"
        sm="6"
      >
        <v-card height="100%">
          <v-card-item class="pa-4">
            <v-card-title class="text-h6">{{ client.title }}</v-card-title>
            <v-card-subtitle class="mt-1">
              <v-icon
                :color="client.useSsl ? 'success' : 'default'"
                size="small"
                class="mr-1"
              >{{ client.useSsl ? 'mdi-lock' : 'mdi-lock-open-outline' }}</v-icon>
              {{ client.host }}:{{ client.port }}
            </v-card-subtitle>
            <template #append>
              <v-chip :color="client.isEnabled ? 'success' : 'default'" size="small">
                {{ client.isEnabled ? 'Enabled' : 'Disabled' }}
              </v-chip>
            </template>
          </v-card-item>

          <v-card-text class="px-4 pb-2">
            <v-chip size="small" variant="outlined">{{ clientTypeLabel(client.clientType) }}</v-chip>
          </v-card-text>

          <v-card-actions class="px-4 pb-4">
            <v-spacer />
            <v-btn icon="mdi-pencil" size="small" variant="text" @click="openEditDialog(client)" />
            <v-btn icon="mdi-delete" size="small" variant="text" color="error" @click="confirmDelete(client)" />
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>

    <!-- Create / Edit dialog -->
    <v-dialog v-model="dialog" max-width="520" persistent>
      <v-card :title="editingId ? 'Edit Download Client' : 'New Download Client'">
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
            <v-select
              v-model="form.clientType"
              label="Client Type"
              :items="clientTypeOptions"
              item-title="title"
              item-value="value"
              :rules="[requiredSelect]"
              required
              class="mb-2"
            />
            <v-row dense class="mb-2">
              <v-col cols="8">
                <v-text-field
                  v-model="form.host"
                  label="Host"
                  :rules="[required]"
                  required
                  hide-details
                />
              </v-col>
              <v-col cols="4">
                <v-text-field
                  v-model.number="form.port"
                  label="Port"
                  type="number"
                  :rules="[required]"
                  required
                  hide-details
                />
              </v-col>
            </v-row>

            <!-- SABnzbd: API key -->
            <v-text-field
              v-if="form.clientType === ClientType.Sabnzbd"
              v-model="form.apiKey"
              label="API Key"
              class="mb-2"
            />

            <!-- NZBGet: username + password -->
            <template v-if="form.clientType === ClientType.Nzbget">
              <v-text-field
                v-model="form.username"
                label="Username"
                class="mb-2"
              />
              <v-text-field
                v-model="form.password"
                label="Password"
                type="password"
                class="mb-2"
              />
            </template>

            <v-text-field
              v-model="form.category"
              label="Category"
              class="mb-2"
            />
            <v-row dense>
              <v-col cols="6">
                <v-switch
                  v-model="form.useSsl"
                  label="Use SSL"
                  color="primary"
                  hide-details
                />
              </v-col>
              <v-col cols="6">
                <v-switch
                  v-model="form.isEnabled"
                  label="Enabled"
                  color="primary"
                  hide-details
                />
              </v-col>
            </v-row>
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
            prepend-icon="mdi-connection"
            :loading="testing"
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
      <v-card title="Delete Download Client">
        <v-card-text>
          Delete <strong>{{ deletingClient?.title }}</strong>? This cannot be undone.
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="deleteDialog = false">Cancel</v-btn>
          <v-btn color="error" :loading="deleting" @click="deleteClient">Delete</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Save-anyway dialog -->
    <v-dialog v-model="saveAnywayDialog" max-width="440" persistent>
      <v-card title="Test Failed">
        <v-card-text>
          <p class="mb-2">{{ testResult?.message }}</p>
          <p>The client has been <strong>disabled</strong>. Save anyway?</p>
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
import { api, type DownloadClient, ClientType } from '../../api'
import { usePageAction } from '../../composables/usePageAction'

const { setAction, clearAction } = usePageAction()

const clientTypeOptions = [
  { title: 'SABnzbd', value: ClientType.Sabnzbd },
  { title: 'NZBGet', value: ClientType.Nzbget },
]

function clientTypeLabel(value: number): string {
  return clientTypeOptions.find(o => o.value === value)?.title ?? String(value)
}

const clients = ref<DownloadClient[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const dialog = ref(false)
const saving = ref(false)
const testing = ref(false)
const saveAnywayDialog = ref(false)
const testResult = ref<{ success: boolean; message: string } | null>(null)
const editingId = ref<string | null>(null)
const formRef = ref()
const deleteDialog = ref(false)
const deleting = ref(false)
const deletingClient = ref<DownloadClient | null>(null)

const emptyForm = () => ({
  title: '',
  clientType: ClientType.Sabnzbd,
  host: '',
  port: 8080,
  useSsl: false,
  apiKey: '',
  username: '',
  password: '',
  category: '',
  isEnabled: true,
})

const form = ref(emptyForm())

const required = (v: string | number) => !!v || 'Required'
const requiredSelect = (v: number | null) => v !== null && v !== undefined ? true : 'Required'

async function fetchClients() {
  loading.value = true
  error.value = null
  try {
    clients.value = await api.downloadClients.list()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load clients'
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

function openEditDialog(client: DownloadClient) {
  editingId.value = client.id
  form.value = {
    title: client.title,
    clientType: client.clientType,
    host: client.host,
    port: client.port,
    useSsl: client.useSsl,
    apiKey: client.apiKey,
    username: client.username,
    password: client.password,
    category: client.category,
    isEnabled: client.isEnabled,
  }
  testResult.value = null
  dialog.value = true
}

async function runTest() {
  testing.value = true
  testResult.value = null
  try {
    testResult.value = await api.downloadClients.test({
      clientType: form.value.clientType,
      host: form.value.host,
      port: form.value.port,
      useSsl: form.value.useSsl,
      apiKey: form.value.apiKey,
      username: form.value.username,
      password: form.value.password,
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

  testing.value = true
  try {
    testResult.value = await api.downloadClients.test({
      clientType: form.value.clientType,
      host: form.value.host,
      port: form.value.port,
      useSsl: form.value.useSsl,
      apiKey: form.value.apiKey,
      username: form.value.username,
      password: form.value.password,
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
      await api.downloadClients.update(editingId.value, form.value)
    } else {
      await api.downloadClients.create(form.value)
    }
    dialog.value = false
    await fetchClients()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to save client'
  } finally {
    saving.value = false
  }
}

function confirmDelete(client: DownloadClient) {
  deletingClient.value = client
  deleteDialog.value = true
}

async function deleteClient() {
  if (!deletingClient.value) return
  deleting.value = true
  error.value = null
  try {
    await api.downloadClients.delete(deletingClient.value.id)
    deleteDialog.value = false
    await fetchClients()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to delete client'
  } finally {
    deleting.value = false
  }
}

onMounted(() => {
  fetchClients()
  setAction('mdi-plus', 'New Client', openCreateDialog)
})

onUnmounted(clearAction)
</script>
