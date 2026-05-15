<template>
  <v-container max-width="800">
    <div class="text-h6 mb-4">Rescue</div>

    <v-card class="mb-6">
      <v-card-text>
        <p class="text-body-2 text-medium-emphasis mb-4">
          Point this at a folder containing stuck downloads. Each direct subfolder is matched
          against known indexer rows. Matched folders are moved to the configured target library
          folder exactly as they would be by the normal download pipeline.
        </p>

        <v-text-field
          v-model="folder"
          label="Folder path"
          placeholder="/downloads/incomplete"
          variant="outlined"
          density="compact"
          hide-details
          class="mb-4"
          @keydown.enter="runPreview"
        />

        <div class="d-flex align-center ga-2">
          <v-btn
            variant="outlined"
            :loading="previewing"
            :disabled="!folder.trim()"
            @click="runPreview"
          >
            Preview
          </v-btn>
          <v-btn
            color="primary"
            :loading="executing"
            :disabled="previewItems === null || matchedCount === 0"
            @click="confirmDialog = true"
          >
            Execute ({{ matchedCount }} matched)
          </v-btn>
        </div>
      </v-card-text>
    </v-card>

    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">
      {{ error }}
    </v-alert>

    <!-- Preview results -->
    <template v-if="previewItems !== null && !executeItems">
      <div class="text-subtitle-1 font-weight-medium mb-3">
        Preview — {{ previewItems.length }} folder{{ previewItems.length === 1 ? '' : 's' }} found,
        {{ matchedCount }} matched
      </div>

      <v-card
        v-for="item in previewItems"
        :key="item.sourcePath"
        variant="outlined"
        class="mb-2"
      >
        <v-card-text class="py-2 px-3">
          <div class="d-flex align-center ga-2">
            <v-icon
              :color="item.isMatched ? 'success' : 'medium-emphasis'"
              size="small"
            >
              {{ item.isMatched ? 'mdi-check-circle' : 'mdi-circle-outline' }}
            </v-icon>
            <div class="flex-grow-1 min-width-0">
              <div class="text-body-2 font-weight-medium text-truncate">{{ item.name }}</div>
              <div v-if="item.isMatched" class="text-caption text-medium-emphasis">
                {{ item.siteTitle }} · {{ item.videoTitle }}
                <span v-if="item.destinationFolder"> → {{ item.destinationFolder }}</span>
              </div>
              <div v-else class="text-caption text-medium-emphasis">No match found</div>
            </div>
            <v-chip size="x-small" variant="tonal">{{ item.videoFileCount }} file{{ item.videoFileCount === 1 ? '' : 's' }}</v-chip>
          </div>
        </v-card-text>
      </v-card>
    </template>

    <!-- Execute results -->
    <template v-if="executeItems">
      <div class="text-subtitle-1 font-weight-medium mb-3">Results</div>

      <v-card
        v-for="item in executeItems"
        :key="item.sourcePath"
        variant="outlined"
        class="mb-2"
      >
        <v-card-text class="py-2 px-3">
          <div class="text-body-2 font-weight-medium mb-1">{{ item.name }}</div>
          <div
            v-for="(entry, i) in item.log"
            :key="i"
            class="text-caption d-flex align-start ga-1"
          >
            <v-icon
              :color="entry.level === 'info' ? 'success' : entry.level === 'warning' ? 'warning' : 'error'"
              size="x-small"
              class="mt-1 flex-shrink-0"
            >
              {{ entry.level === 'info' ? 'mdi-check' : entry.level === 'warning' ? 'mdi-alert' : 'mdi-close-circle' }}
            </v-icon>
            <span :class="entry.level === 'error' ? 'text-error' : entry.level === 'warning' ? 'text-warning' : ''">
              {{ entry.message }}
            </span>
          </div>
        </v-card-text>
      </v-card>
    </template>

    <!-- Confirm dialog -->
    <v-dialog v-model="confirmDialog" max-width="480" persistent>
      <v-card title="Execute rescue?">
        <v-card-text>
          <p class="mb-0">
            This will move files from <strong>{{ matchedCount }}</strong>
            matched folder{{ matchedCount === 1 ? '' : 's' }} to the target library folder.
            This action cannot be undone.
          </p>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="confirmDialog = false">Cancel</v-btn>
          <v-btn color="primary" :loading="executing" @click="runExecute">Move files</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-container>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { api, type RescuePreviewItem, type RescueExecuteItem } from '../../api'

const folder       = ref('')
const previewing   = ref(false)
const executing    = ref(false)
const confirmDialog = ref(false)
const error        = ref<string | null>(null)
const previewItems = ref<RescuePreviewItem[] | null>(null)
const executeItems = ref<RescueExecuteItem[] | null>(null)

const matchedCount = computed(() => previewItems.value?.filter(i => i.isMatched).length ?? 0)

async function runPreview() {
  if (!folder.value.trim()) return
  previewing.value = true
  error.value = null
  previewItems.value = null
  executeItems.value = null
  try {
    const result = await api.rescue.preview(folder.value.trim())
    previewItems.value = result.items
  } catch (e: any) {
    error.value = e.message
  } finally {
    previewing.value = false
  }
}

async function runExecute() {
  confirmDialog.value = false
  executing.value = true
  error.value = null
  try {
    const result = await api.rescue.execute(folder.value.trim())
    executeItems.value = result.items
    previewItems.value = null
  } catch (e: any) {
    error.value = e.message
  } finally {
    executing.value = false
  }
}
</script>
