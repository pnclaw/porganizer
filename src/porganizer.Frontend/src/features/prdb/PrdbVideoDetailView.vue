<template>
  <v-container>
    <v-btn
      variant="text"
      prepend-icon="mdi-arrow-left"
      class="mb-4 px-0"
      @click="$router.back()"
    >
      Back
    </v-btn>

    <div v-if="loading" class="d-flex justify-center py-12">
      <v-progress-circular indeterminate />
    </div>

    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">
      {{ error }}
    </v-alert>

    <template v-if="video">
      <v-row class="mb-6">
        <v-col cols="12" md="5" lg="4">
          <v-carousel
            v-if="video.imageCdnPaths.length > 0"
            :show-arrows="video.imageCdnPaths.length > 1"
            :hide-delimiters="video.imageCdnPaths.length <= 1"
            height="auto"
            class="rounded"
            :style="sfwMode ? 'filter: blur(12px)' : ''"
          >
            <v-carousel-item
              v-for="path in video.imageCdnPaths"
              :key="path"
              :src="path"
              aspect-ratio="16/9"
              cover
            />
          </v-carousel>
          <div
            v-else
            class="bg-surface-variant rounded d-flex align-center justify-center"
            style="aspect-ratio: 16/9"
          >
            <v-icon size="x-large" color="medium-emphasis">mdi-image-off</v-icon>
          </div>
        </v-col>

        <v-col cols="12" md="7" lg="8">
          <div class="text-caption text-medium-emphasis mb-1">{{ video.siteTitle }}</div>
          <h1 class="text-h5 mb-4">{{ video.title }}</h1>

          <v-chip
            v-if="video.isFulfilled !== null"
            :color="video.isFulfilled ? 'success' : 'warning'"
            variant="tonal"
            class="mb-4"
          >
            {{ video.isFulfilled ? 'Fulfilled' : 'Wanted' }}
          </v-chip>

          <div v-if="video.releaseDate" class="text-body-2 mb-4">
            <span class="text-medium-emphasis">Released:</span> {{ formatDate(video.releaseDate) }}
          </div>

          <!-- Quick download: best match for preferred quality -->
          <template v-if="bestMatch">
            <div class="text-caption text-medium-emphasis text-truncate mb-2">
              {{ bestMatch.title }}
              <span class="ml-1">({{ formatSize(bestMatch.nzbSize) }})</span>
            </div>
            <div class="d-flex align-center ga-1">
              <v-chip
                v-if="bestMatch.downloadStatus !== null"
                :color="statusColor(bestMatch.downloadStatus!)"
                variant="tonal"
                size="small"
              >
                {{ statusLabel(bestMatch.downloadStatus!) }}
              </v-chip>
              <v-tooltip v-if="defaultClient && canSend(bestMatch)" :text="`Send to ${defaultClient.title}`" location="top">
                <template #activator="{ props }">
                  <v-btn
                    v-bind="props"
                    icon="mdi-send"
                    size="small"
                    variant="tonal"
                    :loading="sending === bestMatch.indexerRowId"
                    @click="sendToClient(bestMatch)"
                  />
                </template>
              </v-tooltip>
              <v-tooltip text="Download NZB" location="top">
                <template #activator="{ props }">
                  <v-btn
                    v-bind="props"
                    icon="mdi-download"
                    size="small"
                    variant="text"
                    :href="bestMatch.nzbUrl"
                    download
                  />
                </template>
              </v-tooltip>
            </div>
          </template>
        </v-col>
      </v-row>

      <div v-if="video.actors.length > 0" class="mb-6">
        <div class="text-subtitle-1 font-weight-medium mb-2">Cast</div>
        <div class="d-flex flex-wrap ga-2">
          <v-chip
            v-for="actor in video.actors"
            :key="actor.id"
            size="small"
          >
            {{ actor.name }}
          </v-chip>
        </div>
      </div>

      <!-- Download location (fulfilled videos with a completed download log) -->
      <div v-if="video.isFulfilled && fulfilledMatch" class="mb-6">
        <div class="text-subtitle-1 font-weight-medium mb-2">Download location</div>
        <v-card variant="outlined">
          <v-card-text class="py-2 px-3">
            <div
              class="text-body-2 text-medium-emphasis mb-2"
              style="word-break: break-all; font-family: monospace"
            >
              {{ applyFolderMapping(fulfilledMatch.storagePath!) }}
            </div>
            <div class="d-flex flex-wrap ga-2">
              <v-btn
                prepend-icon="mdi-folder-open-outline"
                variant="tonal"
                size="small"
                @click="shellOpen(applyFolderMapping(fulfilledMatch!.storagePath!))"
              >
                Open folder
              </v-btn>
              <v-btn
                v-if="findVideoFile(fulfilledMatch.fileNames)"
                prepend-icon="mdi-play-circle-outline"
                variant="tonal"
                size="small"
                @click="shellOpen(applyFolderMapping(fulfilledMatch!.storagePath!) + '/' + findVideoFile(fulfilledMatch!.fileNames))"
              >
                Open video
              </v-btn>
            </div>
          </v-card-text>
        </v-card>
      </div>

      <div v-if="matches.length > 0" class="mb-6">
        <div class="text-subtitle-1 font-weight-medium mb-2">Indexer matches</div>
        <div class="d-flex flex-column ga-2">
          <v-card
            v-for="match in matches"
            :key="match.indexerRowId"
            variant="outlined"
          >
            <v-card-text class="py-2 px-3">
              <div class="d-flex align-start ga-2">
                <div class="flex-grow-1 min-width-0">
                  <div class="text-body-2 mb-1" style="word-break: break-word">{{ match.title }}</div>
                  <div class="d-flex align-center flex-wrap ga-1">
                    <span class="text-caption text-medium-emphasis">{{ formatSize(match.nzbSize) }}</span>
                    <span class="text-caption text-medium-emphasis">·</span>
                    <span class="text-caption text-medium-emphasis">{{ match.nzbPublishedAt ? formatDate(match.nzbPublishedAt) : '—' }}</span>
                    <v-chip
                      v-if="match.downloadStatus !== null"
                      :color="statusColor(match.downloadStatus!)"
                      size="x-small"
                      variant="tonal"
                    >
                      {{ statusLabel(match.downloadStatus!) }}
                    </v-chip>
                  </div>
                </div>
                <div class="d-flex align-center ga-1 flex-shrink-0">
                  <v-tooltip v-if="defaultClient && canSend(match)" :text="`Send to ${defaultClient.title}`" location="top">
                    <template #activator="{ props }">
                      <v-btn
                        v-bind="props"
                        icon="mdi-send"
                        size="small"
                        variant="tonal"
                        :loading="sending === match.indexerRowId"
                        @click="sendToClient(match)"
                      />
                    </template>
                  </v-tooltip>
                  <v-tooltip text="Download NZB" location="top">
                    <template #activator="{ props }">
                      <v-btn
                        v-bind="props"
                        icon="mdi-download"
                        size="small"
                        variant="text"
                        :href="match.nzbUrl"
                        download
                      />
                    </template>
                  </v-tooltip>
                </div>
              </div>
            </v-card-text>
          </v-card>
        </div>
      </div>

      <div v-if="video.preNames.length > 0" class="mb-6">
        <div class="text-subtitle-1 font-weight-medium mb-2">Alternative titles</div>
        <div v-for="name in video.preNames" :key="name" class="text-body-2 mb-1">
          {{ name }}
        </div>
      </div>
    </template>

    <v-snackbar v-model="snackbar.show" :color="snackbar.color" :timeout="3000">
      {{ snackbar.text }}
    </v-snackbar>
  </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { api, type PrdbVideoDetail, type VideoIndexerMatch, type DownloadClient, type AppSettings, type FolderMapping, DownloadStatus, VideoQuality } from '../../api'
import { useSfwMode } from '../../composables/useSfwMode'

const { sfwMode } = useSfwMode()
const route = useRoute()
const id = route.params.id as string

const video          = ref<PrdbVideoDetail | null>(null)
const matches        = ref<VideoIndexerMatch[]>([])
const defaultClient  = ref<DownloadClient | null>(null)
const settings       = ref<AppSettings | null>(null)
const folderMappings = ref<FolderMapping[]>([])
const loading        = ref(false)
const error          = ref<string | null>(null)
const sending        = ref<string | null>(null)
const snackbar       = ref({ show: false, text: '', color: 'success' })

// Mirror of WantedVideoFulfillmentService.ParseQuality
function parseQuality(title: string): VideoQuality | null {
  const t = title.toLowerCase()
  if (t.includes('2160p') || t.includes('4k') || t.includes('uhd')) return VideoQuality.P2160
  if (t.includes('1080p') || t.includes('1080i'))                    return VideoQuality.P1080
  if (t.includes('720p')  || t.includes('720i'))                     return VideoQuality.P720
  return null
}

// Best match: prefer exact quality, else highest quality available
const bestMatch = computed<VideoIndexerMatch | null>(() => {
  if (matches.value.length === 0) return null
  const preferred = settings.value?.preferredVideoQuality ?? VideoQuality.P1080
  const exact = matches.value.find(m => parseQuality(m.title) === preferred)
  if (exact) return exact
  return [...matches.value].sort((a, b) => {
    const qa = parseQuality(a.title) ?? -1
    const qb = parseQuality(b.title) ?? -1
    return qb - qa
  })[0]
})

async function load() {
  loading.value = true
  error.value = null
  try {
    const [v, m, clients, s, fm] = await Promise.all([
      api.prdbVideos.get(id),
      api.prdbVideos.getIndexerMatches(id),
      api.downloadClients.list(),
      api.settings.get(),
      api.folderMappings.list(),
    ])
    video.value = v
    matches.value = m
    defaultClient.value = clients.find(c => c.isEnabled) ?? null
    settings.value = s
    folderMappings.value = fm
  } catch (e: any) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

async function sendToClient(match: VideoIndexerMatch) {
  if (!defaultClient.value) return
  sending.value = match.indexerRowId
  try {
    const result = await api.downloadClients.send(
      defaultClient.value.id,
      match.nzbUrl,
      match.title,
      match.indexerId,
      match.indexerRowId,
    )
    if (result.success) {
      snackbar.value = { show: true, text: 'Sent to download client', color: 'success' }
      matches.value = await api.prdbVideos.getIndexerMatches(id)
    } else {
      snackbar.value = { show: true, text: result.message, color: 'error' }
    }
  } catch (e: any) {
    snackbar.value = { show: true, text: e.message, color: 'error' }
  } finally {
    sending.value = null
  }
}

function canSend(match: VideoIndexerMatch): boolean {
  return match.downloadStatus === null || match.downloadStatus === DownloadStatus.Failed
}

function statusLabel(status: DownloadStatus): string {
  switch (status) {
    case DownloadStatus.Queued:         return 'Queued'
    case DownloadStatus.Downloading:    return 'Downloading'
    case DownloadStatus.PostProcessing: return 'Post-processing'
    case DownloadStatus.Completed:      return 'Completed'
    case DownloadStatus.Failed:         return 'Failed'
  }
}

function statusColor(status: DownloadStatus): string {
  switch (status) {
    case DownloadStatus.Queued:         return 'info'
    case DownloadStatus.Downloading:    return 'primary'
    case DownloadStatus.PostProcessing: return 'warning'
    case DownloadStatus.Completed:      return 'success'
    case DownloadStatus.Failed:         return 'error'
  }
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}

function formatSize(bytes: number): string {
  if (bytes >= 1_073_741_824) return `${(bytes / 1_073_741_824).toFixed(1)} GB`
  if (bytes >= 1_048_576)     return `${(bytes / 1_048_576).toFixed(0)} MB`
  return `${(bytes / 1_024).toFixed(0)} KB`
}

// The match that fulfilled this video (completed download with a storage path)
const fulfilledMatch = computed<VideoIndexerMatch | null>(() =>
  matches.value.find(m => m.downloadStatus === DownloadStatus.Completed && m.storagePath != null) ?? null
)

function applyFolderMapping(path: string): string {
  for (const mapping of folderMappings.value) {
    if (path.startsWith(mapping.originalFolder)) {
      return mapping.mappedToFolder + path.slice(mapping.originalFolder.length)
    }
  }
  return path
}

async function shellOpen(path: string) {
  try {
    await api.shell.open(path)
  } catch (e: any) {
    snackbar.value = { show: true, text: e.message, color: 'error' }
  }
}

const VIDEO_EXTENSIONS = ['.mkv', '.mp4', '.avi', '.wmv', '.mov', '.m4v', '.ts', '.m2ts', '.webm', '.flv']

function findVideoFile(fileNames: string[] | null): string | null {
  return fileNames?.find(f => VIDEO_EXTENSIONS.some(ext => f.toLowerCase().endsWith(ext))) ?? null
}

onMounted(load)
</script>
