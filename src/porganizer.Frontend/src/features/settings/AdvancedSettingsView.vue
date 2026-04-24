<template>
  <v-container max-width="600">
    <div class="d-flex align-center mb-4">
      <v-btn
        icon="mdi-arrow-left"
        variant="text"
        :to="{ path: '/settings', query: { tab: 'general' } }"
        class="mr-2"
      />
      <span class="text-h6">Advanced Settings</span>
    </div>

    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">
      {{ error }}
    </v-alert>

    <v-alert v-if="saved" type="success" class="mb-4" closable @click:close="saved = false">
      Settings saved.
    </v-alert>

    <v-skeleton-loader v-if="loading" type="card" />

    <template v-else>
      <v-tabs v-model="tab" class="mb-4">
        <v-tab value="general">General</v-tab>
        <v-tab value="library">Library</v-tab>
        <v-tab value="wanted">Wanted</v-tab>
      </v-tabs>

      <v-window v-model="tab">
        <!-- General tab -->
        <v-window-item value="general">
          <v-form ref="formRef" @submit.prevent="submit">
            <v-card class="mb-4">
              <v-card-title>Logging</v-card-title>
              <v-card-text>
                <v-select
                  v-model="form.minimumLogLevel"
                  :items="logLevels"
                  label="Minimum log level"
                  hint="Controls how verbose the application logs are. Changes take effect immediately without a restart."
                  persistent-hint
                  style="max-width: 220px"
                />
              </v-card-text>
            </v-card>

            <div class="text-right mb-6">
              <v-btn type="submit" color="primary" :loading="saving">Save</v-btn>
            </div>
          </v-form>

          <v-card>
            <v-card-title>Reset database</v-card-title>
            <v-card-text>
              <p class="text-body-2 text-medium-emphasis mb-4">
                Clears all synced and operational data and resets every sync cursor so a full
                re-sync runs on the next cycle. The following are preserved:
                download client settings, indexer settings, library folder paths,
                folder mappings, and all application settings.
              </p>
              <v-alert type="warning" variant="tonal" class="mb-4">
                <strong>This cannot be undone.</strong>
                All cached prdb.net data, download history, indexer rows, library file records,
                and the wanted list will be permanently deleted.
              </v-alert>
              <v-btn color="error" variant="outlined" @click="resetDialog = true">
                Reset database
              </v-btn>
            </v-card-text>
          </v-card>
        </v-window-item>

        <!-- Library tab -->
        <v-window-item value="library">
          <v-form ref="formRef" @submit.prevent="submit">
            <v-card class="mb-4">
              <v-card-title>Library cleanup</v-card-title>
              <v-card-text>
                <v-switch
                  v-model="form.autoDeleteAfterPreviewUpload"
                  label="Delete video file and cached previews after upload"
                  color="primary"
                  :disabled="!form.videoUserImageUploadEnabled"
                  hint="Deletes the video file, preview images, and sprite sheet from disk once all uploads to prdb.net complete successfully. Requires preview image upload to be enabled."
                  :persistent-hint="!form.videoUserImageUploadEnabled"
                />
              </v-card-text>
            </v-card>

            <div class="text-right mb-6">
              <v-btn type="submit" color="primary" :loading="saving">Save</v-btn>
            </div>
          </v-form>

          <!-- Manual cleanup -->
          <v-card>
            <v-card-title>Manual cleanup</v-card-title>
            <v-card-text>
              <p class="text-body-2 text-medium-emphasis mb-4">
                Find all library files that have been fully uploaded to prdb.net (all 5 preview
                images and the sprite sheet) and still have files on disk. Use "Preview" to see
                what will be removed, then "Delete" to free the disk space.
              </p>

              <div class="d-flex align-center ga-3 flex-wrap mb-4">
                <v-btn
                  variant="outlined"
                  :loading="previewing"
                  @click="runPreview"
                >
                  Preview
                </v-btn>
                <v-btn
                  color="error"
                  variant="outlined"
                  :disabled="previewResult === null || previewResult.totalCount === 0"
                  @click="confirmDeleteDialog = true"
                >
                  Delete
                </v-btn>
              </div>

              <template v-if="previewResult !== null">
                <v-alert
                  v-if="previewResult.totalCount === 0"
                  type="success"
                  variant="tonal"
                  density="compact"
                  class="mb-3"
                >
                  Nothing to clean up — no files found on disk.
                </v-alert>

                <template v-else>
                  <p class="text-body-2 mb-2">
                    <strong>{{ previewResult.totalCount }}</strong> file{{ previewResult.totalCount === 1 ? '' : 's' }}
                    &nbsp;&middot;&nbsp;
                    <strong>{{ formatBytes(previewResult.totalBytes) }}</strong> will be freed
                  </p>

                  <v-virtual-scroll
                    :items="previewResult.items"
                    height="260"
                    item-height="44"
                  >
                    <template #default="{ item }">
                      <v-list-item
                        :key="item.libraryFileId"
                        density="compact"
                        class="px-0"
                      >
                        <v-list-item-title class="text-body-2 text-truncate">
                          {{ item.relativePath }}
                        </v-list-item-title>
                        <v-list-item-subtitle class="text-caption">
                          {{ item.folderPath }}
                          &nbsp;&middot;&nbsp;
                          <span v-if="item.videoFileExists" class="text-warning">video</span>
                          <span v-if="item.previewDirExists" class="text-warning">
                            {{ item.videoFileExists ? ', ' : '' }}previews
                          </span>
                          <span v-if="item.thumbnailDirExists" class="text-warning">
                            {{ item.videoFileExists || item.previewDirExists ? ', ' : '' }}thumbnails
                          </span>
                        </v-list-item-subtitle>
                      </v-list-item>
                    </template>
                  </v-virtual-scroll>
                </template>
              </template>
            </v-card-text>
          </v-card>
        </v-window-item>

        <!-- Wanted tab -->
        <v-window-item value="wanted">
          <v-form ref="formRef" @submit.prevent="submit">
            <v-card class="mb-4">
              <v-card-title>Auto-add all new matched videos to wanted list</v-card-title>
              <v-card-text>
                <v-alert type="warning" variant="tonal" class="mb-4">
                  <strong>High download volume warning.</strong>
                  When enabled, every video added to prdb.net within the configured window that has
                  at least one indexer match is automatically added to your wanted list and queued
                  for download. This can result in a very large number of downloads each day.
                  Only enable this if you intend to download all new releases from all sites.
                </v-alert>
                <p class="text-body-2 text-medium-emphasis mb-4">
                  Runs every 5 minutes. Videos that are too new to have an indexer match yet will
                  be picked up automatically once a match appears, within the configured window.
                </p>
                <v-switch
                  v-model="form.autoAddAllNewVideos"
                  label="Enable"
                  color="warning"
                  class="mb-2"
                />
                <v-text-field
                  v-model.number="form.autoAddAllNewVideosDaysBack"
                  label="Days back"
                  type="number"
                  :min="1"
                  :max="14"
                  :rules="[v => (v >= 1 && v <= 14) || 'Must be between 1 and 14']"
                  hint="Videos added to prdb.net within this many days are considered. Maximum 14 days."
                  persistent-hint
                  :disabled="!form.autoAddAllNewVideos"
                  style="max-width: 200px"
                />
                <v-switch
                  v-model="form.autoAddAllNewVideosFulfillAllQualities"
                  label="Download all quality variants (720p, 1080p, 2160p)"
                  color="warning"
                  class="mt-4"
                  :disabled="!form.autoAddAllNewVideos"
                  hint="When enabled, auto-added videos are queued for download in every available quality, regardless of the preferred quality setting. Useful for collecting file hashes across all resolutions."
                  persistent-hint
                />
              </v-card-text>
            </v-card>

            <div class="text-right">
              <v-btn type="submit" color="primary" :loading="saving">Save</v-btn>
            </div>
          </v-form>
        </v-window-item>
      </v-window>
    </template>

    <!-- Delete confirmation dialog -->
    <v-dialog v-model="confirmDeleteDialog" max-width="480" persistent>
      <v-card title="Delete uploaded files?">
        <v-card-text>
          <v-alert type="warning" variant="tonal" class="mb-3">
            This action cannot be undone.
          </v-alert>
          <p class="mb-0">
            This will permanently delete the video file, preview images, and sprite sheet for
            <strong>{{ previewResult?.totalCount ?? 0 }}</strong> file{{ (previewResult?.totalCount ?? 0) === 1 ? '' : 's' }}
            and free <strong>{{ formatBytes(previewResult?.totalBytes ?? 0) }}</strong> of disk space.
          </p>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" :disabled="deleting" @click="confirmDeleteDialog = false">Cancel</v-btn>
          <v-btn color="error" :loading="deleting" @click="runDelete">Delete</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Delete result snackbar -->
    <v-snackbar v-model="deleteSnackbar" color="success" :timeout="4000">
      Deleted {{ deleteResult?.deletedCount }} file{{ deleteResult?.deletedCount === 1 ? '' : 's' }},
      freed {{ formatBytes(deleteResult?.freedBytes ?? 0) }}.
    </v-snackbar>

    <!-- Reset database confirmation dialog -->
    <v-dialog v-model="resetDialog" max-width="480" persistent>
      <v-card title="Reset database?">
        <v-card-text>
          <v-alert type="error" variant="tonal" class="mb-3">
            This action <strong>cannot be undone</strong>.
          </v-alert>
          <p class="mb-0">
            The following will be permanently deleted:
          </p>
          <ul class="text-body-2 mt-2 mb-0 pl-4">
            <li>All cached prdb.net data (videos, sites, actors, wanted list)</li>
            <li>All indexer rows and matches</li>
            <li>All download history</li>
            <li>All library file records</li>
            <li>All sync cursors (a full re-sync will run automatically)</li>
          </ul>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" :disabled="resetting" @click="resetDialog = false">Cancel</v-btn>
          <v-btn color="error" :loading="resetting" @click="runReset">Reset</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Reset success snackbar -->
    <v-snackbar v-model="resetSnackbar" color="success" :timeout="5000">
      Database reset complete. A full re-sync will begin on the next cycle.
    </v-snackbar>
  </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { api, type UpdateSettingsRequest, type CleanupPreviewResult, type CleanupDeleteResult, VideoQuality } from '../../api'
import { useSfwMode } from '../../composables/useSfwMode'

const { sfwMode } = useSfwMode()
const route = useRoute()

const logLevels = ['Verbose', 'Debug', 'Information', 'Warning', 'Error', 'Fatal']

const loading = ref(true)
const saving  = ref(false)
const error   = ref<string | null>(null)
const saved   = ref(false)
const formRef = ref()
const tab     = ref((route.query.tab as string) || 'general')

const form = ref<UpdateSettingsRequest>({
  prdbApiKey: '',
  prdbApiUrl: '',
  preferredVideoQuality: VideoQuality.P2160,
  safeForWork: false,
  deleteNonVideoFilesOnCompletion: false,
  completedDownloadsTargetFolder: null,
  organizeCompletedBySite: false,
  renameCompletedFiles: false,
  favoritesWantedEnabled: false,
  favoritesWantedDaysBack: 7,
  autoAddAllNewVideos: false,
  autoAddAllNewVideosDaysBack: 2,
  autoAddAllNewVideosFulfillAllQualities: false,
  ffmpegPath: 'ffmpeg',
  thumbnailGenerationEnabled: false,
  thumbnailGenerationMatchedOnly: false,
  previewImageGenerationEnabled: false,
  previewImageGenerationMatchedOnly: false,
  videoUserImageUploadEnabled: false,
  autoDeleteAfterPreviewUpload: false,
  minimumLogLevel: 'Information',
  downloadLibraryPath: null,
})

onMounted(async () => {
  try {
    const settings = await api.settings.get()
    form.value = {
      prdbApiKey: settings.prdbApiKey,
      prdbApiUrl: settings.prdbApiUrl,
      preferredVideoQuality: settings.preferredVideoQuality,
      safeForWork: settings.safeForWork,
      deleteNonVideoFilesOnCompletion: settings.deleteNonVideoFilesOnCompletion,
      completedDownloadsTargetFolder: settings.completedDownloadsTargetFolder,
      organizeCompletedBySite: settings.organizeCompletedBySite,
      renameCompletedFiles: settings.renameCompletedFiles,
      favoritesWantedEnabled: settings.favoritesWantedEnabled,
      favoritesWantedDaysBack: settings.favoritesWantedDaysBack,
      autoAddAllNewVideos: settings.autoAddAllNewVideos,
      autoAddAllNewVideosDaysBack: settings.autoAddAllNewVideosDaysBack,
      autoAddAllNewVideosFulfillAllQualities: settings.autoAddAllNewVideosFulfillAllQualities ?? false,
      ffmpegPath: settings.ffmpegPath ?? 'ffmpeg',
      thumbnailGenerationEnabled: settings.thumbnailGenerationEnabled ?? false,
      thumbnailGenerationMatchedOnly: settings.thumbnailGenerationMatchedOnly ?? false,
      previewImageGenerationEnabled: settings.previewImageGenerationEnabled ?? false,
      previewImageGenerationMatchedOnly: settings.previewImageGenerationMatchedOnly ?? false,
      videoUserImageUploadEnabled: settings.videoUserImageUploadEnabled ?? false,
      autoDeleteAfterPreviewUpload: settings.autoDeleteAfterPreviewUpload ?? false,
      minimumLogLevel: settings.minimumLogLevel ?? 'Information',
      downloadLibraryPath: settings.downloadLibraryPath ?? null,
    }
  } catch (e: any) {
    error.value = e.message
  } finally {
    loading.value = false
  }
})

async function submit() {
  saving.value = true
  error.value = null
  saved.value = false
  try {
    await api.settings.update(form.value)
    sfwMode.value = form.value.safeForWork
    saved.value = true
  } catch (e: any) {
    error.value = e.message
  } finally {
    saving.value = false
  }
}

// Manual cleanup
const previewing          = ref(false)
const previewResult       = ref<CleanupPreviewResult | null>(null)
const confirmDeleteDialog = ref(false)
const deleting            = ref(false)
const deleteResult        = ref<CleanupDeleteResult | null>(null)
const deleteSnackbar      = ref(false)

async function runPreview() {
  previewing.value = true
  error.value = null
  previewResult.value = null
  try {
    previewResult.value = await api.libraryCleanup.preview()
  } catch (e: any) {
    error.value = e.message
  } finally {
    previewing.value = false
  }
}

async function runDelete() {
  deleting.value = true
  error.value = null
  try {
    deleteResult.value = await api.libraryCleanup.deleteUploaded()
    confirmDeleteDialog.value = false
    previewResult.value = null
    deleteSnackbar.value = true
  } catch (e: any) {
    error.value = e.message
    confirmDeleteDialog.value = false
  } finally {
    deleting.value = false
  }
}

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(1024))
  return `${(bytes / Math.pow(1024, i)).toFixed(1)} ${units[i]}`
}

// Database reset
const resetDialog   = ref(false)
const resetting     = ref(false)
const resetSnackbar = ref(false)

async function runReset() {
  resetting.value = true
  error.value = null
  try {
    await api.settings.resetPrdbData()
    resetDialog.value = false
    resetSnackbar.value = true
  } catch (e: any) {
    error.value = e.message
    resetDialog.value = false
  } finally {
    resetting.value = false
  }
}
</script>
