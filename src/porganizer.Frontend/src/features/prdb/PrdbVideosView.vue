<template>
  <v-container>
    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">
      {{ error }}
    </v-alert>

    <v-expand-transition>
      <v-row v-if="!mobile || filterPanelOpen" class="mb-4">
        <v-col cols="12" sm="6" md="4">
          <v-text-field
            v-model="search"
            prepend-inner-icon="mdi-magnify"
            label="Search"
            clearable
            hide-details
            @update:model-value="onFilterChange"
          />
        </v-col>
        <v-col cols="12" sm="6" md="4">
          <v-autocomplete
            v-model="selectedSiteId"
            :items="filterOptions.sites"
            item-value="id"
            item-title="title"
            label="Site"
            clearable
            hide-details
            :loading="loadingOptions"
            @update:model-value="onFilterChange"
          />
        </v-col>
      </v-row>
    </v-expand-transition>

    <div v-if="loading" class="d-flex justify-center py-10">
      <v-progress-circular indeterminate />
    </div>

    <template v-else>
      <v-row v-if="videos.length">
        <v-col v-for="item in videos" :key="item.id" cols="12" sm="6" md="4" lg="3">
          <v-card class="position-relative overflow-hidden" @click="activeOverlayId = item.id">

            <!-- Card action overlay -->
            <div
              v-if="activeOverlayId === item.id"
              class="position-absolute d-flex flex-column align-center justify-center ga-3"
              style="inset: 0; z-index: 2; background: rgba(0,0,0,0.75)"
              @click.stop="activeOverlayId = null"
            >
              <v-btn
                prepend-icon="mdi-play-circle-outline"
                variant="tonal"
                color="white"
                width="180"
                @click.stop="router.push(`/prdb/videos/${item.id}`)"
              >
                Show details
              </v-btn>
              <v-btn
                prepend-icon="mdi-web"
                variant="tonal"
                color="white"
                width="180"
                @click.stop="filterBySite(item)"
              >
                Filter by site
              </v-btn>
              <v-btn
                :prepend-icon="item.isWanted ? 'mdi-bookmark-remove-outline' : 'mdi-bookmark-plus-outline'"
                variant="tonal"
                :color="item.isWanted ? 'error' : 'white'"
                :loading="togglingWanted === item.id"
                width="180"
                @click.stop="toggleWanted(item)"
              >
                {{ item.isWanted ? 'Remove wanted' : 'Add to wanted' }}
              </v-btn>
              <template v-if="item.hasIndexerMatch">
                <v-btn
                  v-if="defaultClient && bestMatchFor(item.id) && canSend(bestMatchFor(item.id)!)"
                  prepend-icon="mdi-send"
                  variant="tonal"
                  color="white"
                  width="180"
                  :loading="sendingMatch === item.id"
                  @click.stop="sendBestMatch(item)"
                >
                  Send to client
                </v-btn>
                <v-btn
                  v-else-if="bestMatchFor(item.id)"
                  prepend-icon="mdi-download"
                  variant="tonal"
                  color="white"
                  width="180"
                  :href="bestMatchFor(item.id)!.nzbUrl"
                  download
                  @click.stop
                >
                  Download NZB
                </v-btn>
                <v-btn
                  v-else-if="loadingMatches === item.id"
                  prepend-icon="mdi-download"
                  variant="tonal"
                  color="white"
                  width="180"
                  loading
                  @click.stop
                >
                  Download NZB
                </v-btn>
              </template>
            </div>

            <div class="position-relative">
              <div
                v-if="item.isWanted"
                class="position-absolute text-caption font-weight-bold px-2 py-1"
                style="top: 0; left: 0; z-index: 1; border-bottom-right-radius: 6px"
                :style="item.isFulfilled
                  ? 'background: rgba(var(--v-theme-success), 0.85); color: rgb(var(--v-theme-on-success))'
                  : 'background: rgba(var(--v-theme-warning), 0.85); color: rgb(var(--v-theme-on-warning))'"
              >
                {{ item.isFulfilled ? 'Fulfilled' : 'Wanted' }}
              </div>
              <div
                v-if="item.hasIndexerMatch"
                class="position-absolute text-caption font-weight-bold px-2 py-1"
                style="top: 0; right: 0; z-index: 1; border-bottom-left-radius: 6px; background: rgba(120,120,120,0.7); color: #fff"
              >
                NZB
              </div>
              <v-img
                v-if="item.thumbnailCdnPath"
                :src="item.thumbnailCdnPath"
                :aspect-ratio="16 / 9"
                cover
                :style="sfwMode ? 'filter: blur(12px)' : ''"
              />
              <div
                v-else
                class="bg-surface-variant d-flex align-center justify-center"
                style="aspect-ratio: 16/9"
              >
                <v-icon size="large" color="medium-emphasis">mdi-image-off</v-icon>
              </div>
            </div>

            <v-card-text class="pb-3">
              <div class="text-caption text-medium-emphasis">{{ item.siteTitle }}</div>
              <div class="text-body-2 font-weight-medium">{{ item.title }}</div>
              <div class="text-caption text-medium-emphasis mt-1">
                {{ item.releaseDate ? formatDate(item.releaseDate) : 'Release date unknown' }}
                <span v-if="item.actorCount"> · {{ item.actorCount }} {{ item.actorCount === 1 ? 'actor' : 'actors' }}</span>
              </div>
            </v-card-text>
          </v-card>
        </v-col>
      </v-row>

      <div v-else class="text-center text-medium-emphasis py-10">
        No videos found.
      </div>

      <div v-if="totalPages > 1" class="d-flex justify-center mt-4">
        <v-pagination
          v-model="pagination.page"
          :length="totalPages"
          :total-visible="5"
          @update:model-value="onPageChange"
        />
      </div>
    </template>

    <v-snackbar v-model="snackbar.show" :color="snackbar.color" :timeout="3000">
      {{ snackbar.text }}
    </v-snackbar>
  </v-container>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted, watch } from 'vue'
import { useDisplay } from 'vuetify'
import { useRouter } from 'vue-router'
import { api, type PrdbVideoListItem, type PrdbVideoFilterOptions, type VideoIndexerMatch, type DownloadClient, type AppSettings, VideoQuality, DownloadStatus } from '../../api'
import { useSfwMode } from '../../composables/useSfwMode'
import { usePageAction } from '../../composables/usePageAction'
import { useFilterPanel } from '../../composables/useFilterPanel'

const { sfwMode } = useSfwMode()
const { mobile } = useDisplay()
const { setActions, clearAction } = usePageAction()
const { filterPanelOpen, toggle, closePanel } = useFilterPanel()
const router = useRouter()

const videos          = ref<PrdbVideoListItem[]>([])
const total           = ref(0)
const loading         = ref(false)
const error           = ref<string | null>(null)
const togglingWanted  = ref<string | null>(null)
const activeOverlayId = ref<string | null>(null)
const loadingOptions  = ref(false)
const filterOptions   = ref<PrdbVideoFilterOptions>({ sites: [] })
const settings        = ref<AppSettings | null>(null)
const defaultClient   = ref<DownloadClient | null>(null)
const videoMatchesMap = ref<Record<string, VideoIndexerMatch[]>>({})
const loadingMatches  = ref<string | null>(null)
const sendingMatch    = ref<string | null>(null)
const snackbar        = ref({ show: false, text: '', color: 'success' })

const search         = ref('')
const selectedSiteId = ref<string | null>(null)

const pagination = reactive({ page: 1, pageSize: 24 })

const totalPages = computed(() => Math.ceil(total.value / pagination.pageSize))

async function load() {
  loading.value = true
  error.value = null
  try {
    const result = await api.prdbVideos.list({
      search:   search.value || undefined,
      siteId:   selectedSiteId.value ?? undefined,
      page:     pagination.page,
      pageSize: pagination.pageSize,
    })
    videos.value = result.items
    total.value  = result.total
  } catch (e: any) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

async function loadFilterOptions() {
  loadingOptions.value = true
  try {
    filterOptions.value = await api.prdbVideos.filterOptions()
  } catch {
    // non-critical — dropdown stays empty
  } finally {
    loadingOptions.value = false
  }
}

function onFilterChange() {
  pagination.page = 1
  load()
}

async function toggleWanted(item: PrdbVideoListItem) {
  togglingWanted.value = item.id
  try {
    if (item.isWanted) {
      await api.prdbWantedVideos.remove(item.id)
      item.isWanted = false
      item.isFulfilled = null
    } else {
      await api.prdbWantedVideos.add(item.id)
      item.isWanted = true
      item.isFulfilled = false
      // Auto-send best NZB to download client if one is configured
      if (item.hasIndexerMatch && defaultClient.value) {
        if (!videoMatchesMap.value[item.id]) {
          try {
            videoMatchesMap.value[item.id] = await api.prdbVideos.getIndexerMatches(item.id)
          } catch { /* ignore */ }
        }
        const match = bestMatchFor(item.id)
        if (match && canSend(match)) {
          const result = await api.downloadClients.send(
            defaultClient.value.id,
            match.nzbUrl,
            match.title,
            match.indexerId,
            match.indexerRowId,
          )
          if (result.success) {
            snackbar.value = { show: true, text: 'Added to wanted and sent to download client', color: 'success' }
            videoMatchesMap.value[item.id] = await api.prdbVideos.getIndexerMatches(item.id)
          } else {
            snackbar.value = { show: true, text: `Added to wanted, but failed to send to client: ${result.message}`, color: 'warning' }
          }
        }
      }
    }
  } catch (e: any) {
    error.value = e.message
  } finally {
    togglingWanted.value = null
  }
}

function filterBySite(item: PrdbVideoListItem) {
  activeOverlayId.value = null
  selectedSiteId.value = item.siteId
  pagination.page = 1
  load()
}

function onPageChange(page: number) {
  pagination.page = page
  load()
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}

// Mirror of WantedVideoFulfillmentService.ParseQuality
function parseQuality(title: string): VideoQuality | null {
  const t = title.toLowerCase()
  if (t.includes('2160p') || t.includes('4k') || t.includes('uhd')) return VideoQuality.P2160
  if (t.includes('1080p') || t.includes('1080i'))                    return VideoQuality.P1080
  if (t.includes('720p')  || t.includes('720i'))                     return VideoQuality.P720
  return null
}

function bestMatchFor(videoId: string): VideoIndexerMatch | null {
  const matches = videoMatchesMap.value[videoId]
  if (!matches || matches.length === 0) return null
  const preferred = settings.value?.preferredVideoQuality ?? VideoQuality.P1080
  const exact = matches.find(m => parseQuality(m.title) === preferred)
  if (exact) return exact
  return [...matches].sort((a, b) => {
    const qa = parseQuality(a.title) ?? -1
    const qb = parseQuality(b.title) ?? -1
    return qb - qa
  })[0]
}

function canSend(match: VideoIndexerMatch): boolean {
  return match.downloadStatus === null || match.downloadStatus === DownloadStatus.Failed
}

async function loadSettingsAndClients() {
  try {
    const [clients, s] = await Promise.all([api.downloadClients.list(), api.settings.get()])
    defaultClient.value = clients.find(c => c.isEnabled) ?? null
    settings.value = s
  } catch {
    // non-critical
  }
}

async function sendBestMatch(item: PrdbVideoListItem) {
  if (!defaultClient.value) return
  const match = bestMatchFor(item.id)
  if (!match) return
  sendingMatch.value = item.id
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
      videoMatchesMap.value[item.id] = await api.prdbVideos.getIndexerMatches(item.id)
    } else {
      snackbar.value = { show: true, text: result.message, color: 'error' }
    }
  } catch (e: any) {
    snackbar.value = { show: true, text: e.message, color: 'error' }
  } finally {
    sendingMatch.value = null
  }
}

watch(activeOverlayId, async (newId) => {
  if (!newId) return
  const item = videos.value.find(v => v.id === newId)
  if (!item?.hasIndexerMatch) return
  if (videoMatchesMap.value[newId]) return // already loaded
  loadingMatches.value = newId
  try {
    videoMatchesMap.value[newId] = await api.prdbVideos.getIndexerMatches(newId)
  } catch {
    // non-critical
  } finally {
    loadingMatches.value = null
  }
})

const filtersActive = computed(() => !!search.value || !!selectedSiteId.value)

onMounted(() => {
  loadFilterOptions()
  load()
  loadSettingsAndClients()
  setActions({ icon: 'mdi-tune', title: 'Toggle filters', onClick: toggle, badgeActive: () => filtersActive.value, mobileOnly: true })
})

onUnmounted(() => {
  clearAction()
  closePanel()
})
</script>
