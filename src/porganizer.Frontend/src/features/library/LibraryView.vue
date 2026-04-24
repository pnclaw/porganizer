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
        <v-col cols="12" sm="6" md="4">
          <v-autocomplete
            v-model="selectedFolderId"
            :items="filterOptions.folders"
            item-value="id"
            item-title="label"
            label="Folder"
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
          <v-card
            class="position-relative overflow-hidden"
            :to="`/library/videos/${item.id}`"
            @mouseenter="startSprite(item)"
            @mouseleave="stopSprite(item.id)"
          >
            <!-- LOCAL badge -->
            <div
              class="position-absolute text-caption font-weight-bold px-2 py-1"
              style="top: 0; left: 0; z-index: 1; border-bottom-right-radius: 6px; background: rgba(var(--v-theme-success), 0.85); color: rgb(var(--v-theme-on-success))"
            >
              {{ item.localFileCount }} {{ item.localFileCount === 1 ? 'file' : 'files' }}
            </div>

            <!-- Image area: CDN thumbnail as base, sprite overlay on hover -->
            <div class="position-relative" style="aspect-ratio: 16/9; overflow: hidden">
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
                style="width: 100%; height: 100%"
              >
                <v-icon size="large" color="medium-emphasis">mdi-image-off</v-icon>
              </div>

              <!-- Sprite sheet overlay -->
              <div
                v-if="activeTiles[item.id] != null && item.spriteSheetCdnUrl"
                class="position-absolute"
                style="inset: 0; transition: none"
                :style="{
                  backgroundImage: `url(${item.spriteSheetCdnUrl})`,
                  backgroundSize: `${(item.spriteColumns ?? 10) * 100}% ${(item.spriteRows ?? 10) * 100}%`,
                  backgroundPosition: spritePosition(activeTiles[item.id]!, item.spriteColumns ?? 10, item.spriteRows ?? 10),
                  filter: sfwMode ? 'blur(12px)' : 'none',
                }"
              />
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
        No videos in your library yet. Add folders and trigger indexing in
        <router-link to="/library/folders">Library Folders</router-link>.
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
  </v-container>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'
import { useDisplay } from 'vuetify'
import { api, type LibraryVideoListItem, type LibraryVideoFilterOptions } from '../../api'
import { useSfwMode } from '../../composables/useSfwMode'
import { usePageAction } from '../../composables/usePageAction'
import { useFilterPanel } from '../../composables/useFilterPanel'

const { sfwMode } = useSfwMode()
const { mobile } = useDisplay()
const { setActions, clearAction } = usePageAction()
const { filterPanelOpen, toggle, closePanel } = useFilterPanel()

// Sprite sheet hover animation state: maps video id → current tile index (null = not hovering)
const activeTiles = reactive<Record<string, number | null>>({})
const spriteTimers: Record<string, ReturnType<typeof setInterval>> = {}

const SPRITE_FPS_MS = 250

function spritePosition(tile: number, cols: number, rows: number): string {
  const col = tile % cols
  const row = Math.floor(tile / cols)
  const x = cols > 1 ? col * 100 / (cols - 1) : 0
  const y = rows > 1 ? row * 100 / (rows - 1) : 0
  return `${x}% ${y}%`
}

function startSprite(item: LibraryVideoListItem) {
  if (!item.spriteSheetCdnUrl || !item.spriteTileCount || item.spriteTileCount < 2) return
  const tileCount = item.spriteTileCount
  activeTiles[item.id] = 0
  spriteTimers[item.id] = setInterval(() => {
    activeTiles[item.id] = ((activeTiles[item.id] ?? 0) + 1) % tileCount
  }, SPRITE_FPS_MS)
}

function stopSprite(id: string) {
  clearInterval(spriteTimers[id])
  delete spriteTimers[id]
  activeTiles[id] = null
}

const videos         = ref<LibraryVideoListItem[]>([])
const total          = ref(0)
const loading        = ref(false)
const error          = ref<string | null>(null)
const loadingOptions = ref(false)
const filterOptions    = ref<LibraryVideoFilterOptions>({ sites: [], folders: [] })

const search           = ref('')
const selectedSiteId   = ref<string | null>(null)
const selectedFolderId = ref<string | null>(null)
const pagination     = reactive({ page: 1, pageSize: 24 })
const totalPages     = computed(() => Math.ceil(total.value / pagination.pageSize))

async function load() {
  loading.value = true
  error.value = null
  try {
    const result = await api.libraryVideos.list({
      search:    search.value || undefined,
      siteId:    selectedSiteId.value ?? undefined,
      folderId:  selectedFolderId.value ?? undefined,
      page:      pagination.page,
      pageSize:  pagination.pageSize,
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
    filterOptions.value = await api.libraryVideos.filterOptions()
  } catch {
    // non-critical
  } finally {
    loadingOptions.value = false
  }
}

function onFilterChange() {
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

const filtersActive = computed(() => !!search.value || !!selectedSiteId.value || !!selectedFolderId.value)

onMounted(() => {
  loadFilterOptions()
  load()
  setActions({ icon: 'mdi-tune', title: 'Toggle filters', onClick: toggle, badgeActive: () => filtersActive.value, mobileOnly: true })
})

onUnmounted(() => {
  clearAction()
  closePanel()
  Object.keys(spriteTimers).forEach(id => clearInterval(spriteTimers[id]))
})
</script>
