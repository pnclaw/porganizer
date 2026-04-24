<template>
  <v-container max-width="600">
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
        <v-tab value="downloads">Downloads</v-tab>
        <v-tab value="library">Library</v-tab>
        <v-tab value="prdb">PRDB.net</v-tab>
        <v-tab value="wanted">Wanted</v-tab>
        <v-tab value="folders">Folder Mapping</v-tab>
      </v-tabs>

      <v-window v-model="tab">
        <!-- General tab -->
        <v-window-item value="general">
          <v-form ref="formRef" @submit.prevent="submit">
            <v-card class="mb-4">
              <v-card-title>Quality</v-card-title>
              <v-card-text>
                <v-select
                  v-model="form.preferredVideoQuality"
                  :items="qualityItems"
                  label="Preferred Video Quality"
                />
              </v-card-text>
            </v-card>

            <v-card class="mb-4">
              <v-card-title>Display</v-card-title>
              <v-card-text>
                <v-switch
                  v-model="form.safeForWork"
                  label="Safe for work"
                  hint="Blurs all images from the prdb API"
                  persistent-hint
                  color="primary"
                />
              </v-card-text>
            </v-card>

            <div class="d-flex align-center justify-end ga-3">
              <v-btn variant="outlined" :to="{ path: '/settings/advanced' }">Advanced settings</v-btn>
              <v-btn type="submit" color="primary" :loading="saving">Save</v-btn>
            </div>
          </v-form>
        </v-window-item>

        <!-- Downloads tab -->
        <v-window-item value="downloads">
          <v-form ref="formRef" @submit.prevent="submit">
            <v-card class="mb-4">
              <v-card-title>Downloads</v-card-title>
              <v-card-text>
                <v-switch
                  v-model="form.deleteNonVideoFilesOnCompletion"
                  label="Delete non-video files on completion"
                  hint="Automatically deletes non-video files (e.g. NFOs, subtitles, images) from the download folder when a download completes"
                  persistent-hint
                  color="primary"
                  class="mb-4"
                />

                <v-switch
                  v-model="form.organizeCompletedBySite"
                  label="Move completed downloads into site folders"
                  hint="Moves video files into a subfolder named after the linked prdb.net site inside the target folder below. Requires a target folder to be set."
                  persistent-hint
                  color="primary"
                  class="mb-4"
                />

                <v-text-field
                  v-model="form.completedDownloadsTargetFolder"
                  label="Completed downloads target folder"
                  hint="Root folder where completed downloads are moved. Must already exist on disk."
                  persistent-hint
                  :disabled="!form.organizeCompletedBySite"
                  clearable
                  class="mb-4"
                />

                <v-switch
                  v-model="form.renameCompletedFiles"
                  label="Rename files on move"
                  hint="Renames video files to &quot;{Site} - {Title} - {ReleaseDate} - {Quality}&quot; when moving. When off, original filenames are kept."
                  persistent-hint
                  color="primary"
                  :disabled="!form.organizeCompletedBySite"
                  class="mb-4"
                />

                <v-text-field
                  v-model="form.downloadLibraryPath"
                  label="Download library path"
                  hint="Root folder where downloads land when they are not moved (move disabled or unmatched). When set, this folder is automatically registered as a library source and re-indexed after each completed download."
                  persistent-hint
                  clearable
                />
              </v-card-text>
            </v-card>

            <div class="text-right">
              <v-btn type="submit" color="primary" :loading="saving">Save</v-btn>
            </div>
          </v-form>
        </v-window-item>

        <!-- Library tab -->
        <v-window-item value="library">
          <v-form ref="formRef" @submit.prevent="submit">
            <v-card class="mb-4">
              <v-card-title>Preview Thumbnails</v-card-title>
              <v-card-text>
                <p class="text-body-2 text-medium-emphasis mb-4">
                  When enabled, ffmpeg generates a sprite-sheet thumbnail for every file in your
                  library. Thumbnails appear as hover previews on video cards. Sprite sheets are
                  stored automatically in the app data directory.
                </p>
                <v-switch
                  v-model="form.thumbnailGenerationEnabled"
                  label="Enable preview thumbnail generation"
                  color="primary"
                  class="mb-4"
                />
                <div class="d-flex align-center gap-3 mb-1">
                  <v-text-field
                    v-model="form.ffmpegPath"
                    label="ffmpeg path"
                    hide-details
                    :disabled="!form.thumbnailGenerationEnabled"
                    class="flex-grow-1"
                    @update:model-value="ffmpegValidation = null"
                  />
                  <v-btn
                    variant="outlined"
                    :loading="ffmpegValidating"
                    :disabled="!form.thumbnailGenerationEnabled"
                    @click="validateFfmpeg"
                  >
                    Test
                  </v-btn>
                </div>
                <div class="text-caption text-medium-emphasis ps-4 mb-3">
                  Use "ffmpeg" if it is on your PATH, or provide the full path to the binary.
                </div>
                <v-alert
                  v-if="ffmpegValidation"
                  :type="ffmpegValidation.ok ? 'success' : 'error'"
                  variant="tonal"
                  density="compact"
                  class="mt-2 mb-4 text-caption"
                >
                  {{ ffmpegValidation.message }}
                </v-alert>
                <v-switch
                  v-model="form.thumbnailGenerationMatchedOnly"
                  label="Only generate thumbnails for matched videos"
                  hint="When on, thumbnails are only generated for library files that have been matched to a prdb.net video. When off, thumbnails are generated for every file in the library."
                  persistent-hint
                  color="primary"
                  :disabled="!form.thumbnailGenerationEnabled"
                />
              </v-card-text>
            </v-card>

            <v-card class="mb-4">
              <v-card-title>Preview Images</v-card-title>
              <v-card-text>
                <p class="text-body-2 text-medium-emphasis mb-4">
                  When enabled, ffmpeg extracts 5 high-quality JPEG frames per video at 10%, 25%,
                  50%, 75%, and 90% of its duration. Images are scaled to a maximum width of
                  1920px and stored separately from sprite sheets.
                </p>
                <v-switch
                  v-model="form.previewImageGenerationEnabled"
                  label="Enable preview image generation"
                  color="primary"
                  class="mb-2"
                />
                <v-switch
                  v-model="form.previewImageGenerationMatchedOnly"
                  label="Only generate previews for matched videos"
                  hint="When on, preview images are only generated for library files matched to a prdb.net video. When off, previews are generated for every file in the library."
                  persistent-hint
                  color="primary"
                  :disabled="!form.previewImageGenerationEnabled"
                />
              </v-card-text>
            </v-card>

            <v-card class="mb-4">
              <v-card-title>Upload to prdb.net</v-card-title>
              <v-card-text>
                <p class="text-body-2 text-medium-emphasis mb-4">
                  When enabled, locally generated preview images are automatically uploaded to
                  prdb.net after generation. Matched files (linked to a prdb.net video) upload
                  5 single frames and the sprite sheet. Unmatched files upload 5 single frames
                  only, grouped by file hash. Uploads are skipped when prdb.net already has images
                  for the file. Requires preview image generation to be enabled and a valid
                  prdb.net API key.
                </p>
                <v-switch
                  v-model="form.videoUserImageUploadEnabled"
                  label="Enable preview image upload to prdb.net"
                  color="primary"
                  :disabled="!form.previewImageGenerationEnabled"
                  hint="Requires preview image generation to be enabled."
                  :persistent-hint="!form.previewImageGenerationEnabled"
                />
              </v-card-text>
            </v-card>

            <div class="text-right">
              <v-btn type="submit" color="primary" :loading="saving">Save</v-btn>
            </div>
          </v-form>

          <v-card class="mt-4">
            <v-card-title>Upload to prdb.net</v-card-title>
            <v-card-text>
              <p class="text-body-2 mb-3">
                Enqueues all library files whose preview images and sprite sheet have been generated
                but have not yet been uploaded to prdb.net. Useful if upload was enabled after
                generation already ran.
              </p>
              <v-btn
                color="primary"
                variant="outlined"
                :loading="uploadingAll"
                @click="uploadAllPreviews"
              >
                Upload All Now
              </v-btn>
              <v-chip v-if="uploadAllResult !== null" class="ml-3" color="success" variant="tonal">
                {{ uploadAllResult }} file{{ uploadAllResult === 1 ? '' : 's' }} enqueued
              </v-chip>
            </v-card-text>
          </v-card>

          <v-card class="mt-4">
            <v-card-title>Danger Zone</v-card-title>
            <v-card-text>
              <p class="text-body-2 mb-3">
                Deletes all generated sprite sheet files from disk and clears the generation
                status for every library file. Thumbnails will need to be regenerated from scratch,
                either automatically by the background worker or via the generate-all action.
              </p>
              <v-btn color="error" variant="outlined" @click="resetThumbnailsDialog = true">
                Reset All Thumbnails
              </v-btn>
            </v-card-text>
            <v-card-text>
              <p class="text-body-2 mb-3">
                Deletes all generated preview image files from disk and clears the generation
                status for every library file. Preview images will need to be regenerated from
                scratch, either automatically by the background worker or via the generate-all action.
              </p>
              <v-btn color="error" variant="outlined" @click="resetPreviewsDialog = true">
                Reset All Preview Images
              </v-btn>
            </v-card-text>
          </v-card>
        </v-window-item>

        <!-- PRDB.net tab -->
        <v-window-item value="prdb">
          <v-form ref="formRef" @submit.prevent="submit">
            <v-card class="mb-4">
              <v-card-title>prdb.net</v-card-title>
              <v-card-text>
                <v-text-field
                  v-model="form.prdbApiKey"
                  label="prdb.net ApiKey"
                  class="mb-2"
                />
                <v-text-field
                  v-model="form.prdbApiUrl"
                  label="prdb.net Url"
                  :rules="[required]"
                  class="mb-4"
                />
              </v-card-text>
            </v-card>

            <div class="text-right">
              <v-btn type="submit" color="primary" :loading="saving">Save</v-btn>
            </div>
          </v-form>

          <v-card class="mt-4">
            <v-card-title>Danger Zone</v-card-title>
            <v-card-text>
              <p class="text-body-2 mb-3">
                Reset all data cached from prdb.net, including synced videos, sites, actors,
                wanted list entries, and usenet download logs. Sync will restart from scratch.
                API credentials and other settings are not affected.
              </p>
              <v-btn color="error" variant="outlined" @click="resetDialog = true">
                Reset DB
              </v-btn>
            </v-card-text>
          </v-card>
        </v-window-item>

        <!-- Wanted tab -->
        <v-window-item value="wanted">
          <v-form ref="formRef" @submit.prevent="submit">
            <v-card class="mb-4">
              <v-card-title>Auto-add favorites to wanted list</v-card-title>
              <v-card-text>
                <p class="text-body-2 text-medium-emphasis mb-4">
                  When enabled, videos added to prdb.net within the configured window that belong
                  to a favorite site or have at least one favorite actor are automatically added
                  to the wanted list on every sync.
                </p>
                <v-switch
                  v-model="form.favoritesWantedEnabled"
                  label="Enable"
                  color="primary"
                  class="mb-2"
                />
                <v-text-field
                  v-model.number="form.favoritesWantedDaysBack"
                  label="Days back"
                  type="number"
                  :min="1"
                  :max="365"
                  :rules="[v => (v >= 1 && v <= 365) || 'Must be between 1 and 365']"
                  hint="Videos added to prdb.net within this many days are considered"
                  persistent-hint
                  :disabled="!form.favoritesWantedEnabled"
                  style="max-width: 200px"
                />
              </v-card-text>
            </v-card>

            <div class="text-right">
              <v-btn type="submit" color="primary" :loading="saving">Save</v-btn>
            </div>
          </v-form>
        </v-window-item>

        <!-- Folder Mapping tab -->
        <v-window-item value="folders">
          <v-alert v-if="folderError" type="error" class="mb-4" closable @click:close="folderError = null">
            {{ folderError }}
          </v-alert>

          <div class="d-flex align-center mb-3">
            <v-btn
              :icon="showFolderInfo ? 'mdi-information' : 'mdi-information-outline'"
              variant="text"
              :color="showFolderInfo ? 'info' : undefined"
              @click="showFolderInfo = !showFolderInfo"
            />
            <v-spacer />
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openAddDialog">
              Add Mapping
            </v-btn>
          </div>

          <v-expand-transition>
            <v-alert v-if="showFolderInfo" type="info" variant="tonal" class="mb-4">
              <p class="mb-1">
                Folder mappings translate paths used by your download client (e.g. SABnzbd or NZBGet)
                into paths accessible on the machine running this app.
              </p>
              <p class="mb-0">
                This is typically needed when your download client runs in Docker or on a remote
                machine and mounts network shares under different paths than your local system.
                For example, the download client may write to <code>/downloads/complete</code> while
                the same share is available locally as <code>Z:\downloads\complete</code>.
                If both apps run on the same machine you can usually leave this empty.
              </p>
            </v-alert>
          </v-expand-transition>

          <v-data-table
            :headers="folderHeaders"
            :items="folderMappings"
            :loading="folderLoading"
            item-value="id"
            hover
            no-data-text="No folder mappings configured."
          >
            <template #item.actions="{ item }">
              <v-btn
                icon="mdi-pencil"
                size="small"
                variant="text"
                class="mr-1"
                @click="openEditDialog(item)"
              />
              <v-btn
                icon="mdi-delete"
                size="small"
                variant="text"
                color="error"
                @click="deleteMapping(item.id)"
              />
            </template>
          </v-data-table>
        </v-window-item>
      </v-window>
    </template>

    <!-- Reset thumbnails confirm dialog -->
    <v-dialog v-model="resetThumbnailsDialog" max-width="480" persistent>
      <v-card title="Reset all thumbnails?">
        <v-card-text>
          <v-alert type="warning" variant="tonal" class="mb-3">
            This action cannot be undone.
          </v-alert>
          <p class="mb-2">The following will be permanently deleted:</p>
          <ul class="mb-3 pl-4 text-body-2">
            <li>All sprite sheet JPEG files from the thumbnail cache on disk</li>
            <li>All WebVTT thumbnail track files</li>
          </ul>
          <p class="mb-0">
            The generation status will be cleared for every library file. Thumbnails will need to
            be regenerated — the background worker will pick them up automatically, or you can
            trigger it manually.
          </p>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" :disabled="resettingThumbnails" @click="resetThumbnailsDialog = false">Cancel</v-btn>
          <v-btn color="error" :loading="resettingThumbnails" @click="confirmResetThumbnails">Reset All Thumbnails</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Reset previews confirm dialog -->
    <v-dialog v-model="resetPreviewsDialog" max-width="480" persistent>
      <v-card title="Reset all preview images?">
        <v-card-text>
          <v-alert type="warning" variant="tonal" class="mb-3">
            This action cannot be undone.
          </v-alert>
          <p class="mb-2">The following will be permanently deleted:</p>
          <ul class="mb-3 pl-4 text-body-2">
            <li>All preview JPEG files from the preview image cache on disk</li>
          </ul>
          <p class="mb-0">
            The generation status will be cleared for every library file. Preview images will need
            to be regenerated — the background worker will pick them up automatically, or you can
            trigger it manually.
          </p>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" :disabled="resettingPreviews" @click="resetPreviewsDialog = false">Cancel</v-btn>
          <v-btn color="error" :loading="resettingPreviews" @click="confirmResetPreviews">Reset All Preview Images</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Reset DB confirm dialog -->
    <v-dialog v-model="resetDialog" max-width="480" persistent>
      <v-card title="Reset prdb.net data?">
        <v-card-text>
          <p class="mb-2">This will permanently delete all data cached from prdb.net:</p>
          <ul class="mb-3 pl-4 text-body-2">
            <li>All synced videos, sites, networks, and actors</li>
            <li>Wanted list entries</li>
            <li>Usenet download logs and indexer row matches</li>
          </ul>
          <p class="mb-0">Your API key, download clients, and folder mappings are <strong>not</strong> affected. The sync will restart from scratch on the next run.</p>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" :disabled="resetting" @click="resetDialog = false">Cancel</v-btn>
          <v-btn color="error" :loading="resetting" @click="confirmReset">Reset DB</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- Add / Edit dialog -->
    <v-dialog v-model="mappingDialog" max-width="540" persistent>
      <v-card :title="editingId ? 'Edit Mapping' : 'Add Mapping'">
        <v-card-text>
          <v-form ref="mappingFormRef" @submit.prevent="submitMapping">
            <v-text-field
              v-model="mappingForm.originalFolder"
              label="Original Folder (download client path)"
              hint="The path as seen by SABnzbd / NZBGet"
              persistent-hint
              :rules="[required]"
              class="mb-4"
              autofocus
            />
            <v-text-field
              v-model="mappingForm.mappedToFolder"
              label="Mapped To Folder (local path)"
              hint="The equivalent path on the machine running this app"
              persistent-hint
              :rules="[required]"
            />
          </v-form>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="mappingDialog = false">Cancel</v-btn>
          <v-btn color="primary" :loading="mappingSaving" @click="submitMapping">
            {{ editingId ? 'Save' : 'Add' }}
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { api, VideoQuality, VideoQualityLabels, type UpdateSettingsRequest, type FolderMapping, type FolderMappingRequest } from '../../api'
import { useSfwMode } from '../../composables/useSfwMode'

const { sfwMode } = useSfwMode()
const route = useRoute()

// Settings form state
const loading  = ref(true)
const saving   = ref(false)
const error    = ref<string | null>(null)
const saved    = ref(false)
const formRef  = ref()
const tab      = ref((route.query.tab as string) || 'general')

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

const qualityItems = Object.values(VideoQuality)
  .filter((v): v is VideoQuality => typeof v === 'number')
  .map(v => ({ title: VideoQualityLabels[v], value: v }))

const required = (v: string) => !!v || 'Required'
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

  await fetchMappings()
})

async function submit() {
  const { valid } = await formRef.value.validate()
  if (!valid) return

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

// ffmpeg validation
const ffmpegValidating = ref(false)
const ffmpegValidation = ref<{ ok: boolean; message: string } | null>(null)

async function validateFfmpeg() {
  ffmpegValidating.value = true
  ffmpegValidation.value = null
  try {
    ffmpegValidation.value = await api.libraryThumbnails.validateFfmpeg(form.value.ffmpegPath || 'ffmpeg')
  } catch (e: any) {
    ffmpegValidation.value = { ok: false, message: e.message }
  } finally {
    ffmpegValidating.value = false
  }
}

// Reset thumbnails state
const resetThumbnailsDialog  = ref(false)
const resettingThumbnails    = ref(false)

async function confirmResetThumbnails() {
  resettingThumbnails.value = true
  error.value = null
  try {
    await api.libraryThumbnails.resetAll()
    resetThumbnailsDialog.value = false
    saved.value = true
  } catch (e: any) {
    error.value = e.message
    resetThumbnailsDialog.value = false
  } finally {
    resettingThumbnails.value = false
  }
}

// Reset previews state
const resetPreviewsDialog = ref(false)
const resettingPreviews   = ref(false)

async function confirmResetPreviews() {
  resettingPreviews.value = true
  error.value = null
  try {
    await api.libraryPreviews.resetAll()
    resetPreviewsDialog.value = false
    saved.value = true
  } catch (e: any) {
    error.value = e.message
    resetPreviewsDialog.value = false
  } finally {
    resettingPreviews.value = false
  }
}

// Upload all state
const uploadingAll    = ref(false)
const uploadAllResult = ref<number | null>(null)

async function uploadAllPreviews() {
  uploadingAll.value = true
  uploadAllResult.value = null
  error.value = null
  try {
    const result = await api.libraryPreviews.uploadAll()
    uploadAllResult.value = result.enqueued
  } catch (e: any) {
    error.value = e.message
  } finally {
    uploadingAll.value = false
  }
}

// Reset DB state
const resetDialog = ref(false)
const resetting   = ref(false)

async function confirmReset() {
  resetting.value = true
  error.value = null
  try {
    await api.settings.resetPrdbData()
    resetDialog.value = false
    saved.value = true
  } catch (e: any) {
    error.value = e.message
    resetDialog.value = false
  } finally {
    resetting.value = false
  }
}

// Folder mapping state
const folderMappings  = ref<FolderMapping[]>([])
const folderLoading   = ref(false)
const folderError     = ref<string | null>(null)
const showFolderInfo  = ref(false)
const mappingDialog   = ref(false)
const mappingSaving  = ref(false)
const mappingFormRef = ref()
const editingId      = ref<string | null>(null)
const mappingForm    = ref<FolderMappingRequest>({ originalFolder: '', mappedToFolder: '' })

const folderHeaders = [
  { title: 'Original Folder', key: 'originalFolder' },
  { title: 'Mapped To Folder', key: 'mappedToFolder' },
  { title: '', key: 'actions', sortable: false, align: 'end' as const, width: '100px' },
]

async function fetchMappings() {
  folderLoading.value = true
  try {
    folderMappings.value = await api.folderMappings.list()
  } catch (e: any) {
    folderError.value = e.message
  } finally {
    folderLoading.value = false
  }
}

function openAddDialog() {
  editingId.value = null
  mappingForm.value = { originalFolder: '', mappedToFolder: '' }
  mappingDialog.value = true
}

function openEditDialog(item: FolderMapping) {
  editingId.value = item.id
  mappingForm.value = { originalFolder: item.originalFolder, mappedToFolder: item.mappedToFolder }
  mappingDialog.value = true
}

async function submitMapping() {
  const { valid } = await mappingFormRef.value.validate()
  if (!valid) return

  mappingSaving.value = true
  folderError.value = null
  try {
    if (editingId.value) {
      await api.folderMappings.update(editingId.value, mappingForm.value)
    } else {
      await api.folderMappings.create(mappingForm.value)
    }
    mappingDialog.value = false
    await fetchMappings()
  } catch (e: any) {
    folderError.value = e.message
    mappingDialog.value = false
  } finally {
    mappingSaving.value = false
  }
}

async function deleteMapping(id: string) {
  folderError.value = null
  try {
    await api.folderMappings.delete(id)
    await fetchMappings()
  } catch (e: any) {
    folderError.value = e.message
  }
}
</script>
