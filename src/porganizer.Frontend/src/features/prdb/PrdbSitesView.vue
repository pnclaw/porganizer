<template>
  <v-container>
    <v-alert v-if="syncResult" type="success" class="mb-4" closable @click:close="syncResult = null">
      Sync complete — {{ syncResult.sitesUpserted }} sites, {{ syncResult.networksUpserted }} networks, {{ syncResult.favoriteSitesSynced }} favorite sites, {{ syncResult.favoriteActorsSynced }} favorite actors, {{ syncResult.videosUpserted }} videos upserted.
    </v-alert>

    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">
      {{ error }}
    </v-alert>

    <v-alert
      v-if="!loading && favoritesOnly && !search && total === 0"
      type="info"
      class="mb-4"
    >
      No favorite sites found. Try syncing, or turn off <strong>Favorites only</strong> to see all sites.
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
        <v-col cols="12" sm="4" md="3" class="d-flex align-center">
          <v-switch
            v-model="favoritesOnly"
            label="Favorites only"
            hide-details
            color="primary"
            @update:model-value="onFilterChange"
          />
        </v-col>
      </v-row>
    </v-expand-transition>

    <div v-if="loading" class="text-center py-8">
      <v-progress-circular indeterminate color="primary" />
    </div>

    <div v-else-if="sites.length === 0" class="text-center py-8 text-medium-emphasis">
      No sites found.
    </div>

    <template v-else>
      <v-row>
        <v-col v-for="site in sites" :key="site.id" cols="12" sm="6" md="4" lg="3">
          <v-card :to="`/prdb/sites/${site.id}/videos`">
            <div class="position-relative">
              <v-img
                v-if="site.thumbnailCdnPath"
                :src="site.thumbnailCdnPath"
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

              <v-btn
                :icon="site.isFavorite ? 'mdi-star' : 'mdi-star-outline'"
                size="small"
                :color="site.isFavorite ? 'amber' : 'white'"
                class="position-absolute"
                style="top: 6px; right: 6px; background-color: rgba(0,0,0,0.5)"
                :loading="togglingIds.includes(site.id)"
                @click.prevent="toggleFavorite(site)"
              />
            </div>

            <v-card-text class="pb-3">
              <div v-if="site.networkTitle" class="text-caption text-medium-emphasis">{{ site.networkTitle }}</div>
              <div class="text-body-2 font-weight-medium">{{ site.title }}</div>
              <div class="text-caption text-medium-emphasis mt-1">{{ site.videoCount }} videos</div>
            </v-card-text>
          </v-card>
        </v-col>
      </v-row>

      <div v-if="pageCount > 1" class="d-flex justify-center mt-4">
        <v-pagination
          :model-value="pagination.page"
          :length="pageCount"
          density="comfortable"
          @update:model-value="onPageChange"
        />
      </div>
    </template>
  </v-container>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'
import { useDisplay } from 'vuetify'
import { api, type PrdbSite } from '../../api'
import { useSfwMode } from '../../composables/useSfwMode'
import { usePageAction } from '../../composables/usePageAction'
import { useFilterPanel } from '../../composables/useFilterPanel'

const sites       = ref<PrdbSite[]>([])
const total       = ref(0)
const loading     = ref(false)
const syncing     = ref(false)
const error       = ref<string | null>(null)
const togglingIds = ref<string[]>([])
const syncResult  = ref<{ networksUpserted: number; sitesUpserted: number; favoriteSitesSynced: number; favoriteActorsSynced: number; videosUpserted: number } | null>(null)
const search      = ref('')
const favoritesOnly = ref(true)

const pagination = reactive({ page: 1, pageSize: 24 })

const pageCount = computed(() => Math.ceil(total.value / pagination.pageSize))

async function load() {
  loading.value = true
  error.value = null
  try {
    const result = await api.prdbSites.list({
      search: search.value || undefined,
      favoritesOnly: favoritesOnly.value || undefined,
      page: pagination.page,
      pageSize: pagination.pageSize,
    })
    sites.value = result.items
    total.value = result.total
  } catch (e: any) {
    error.value = e.message
  } finally {
    loading.value = false
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

async function toggleFavorite(item: PrdbSite) {
  if (togglingIds.value.includes(item.id)) return
  togglingIds.value = [...togglingIds.value, item.id]
  const newFavorite = !item.isFavorite
  try {
    await api.prdbSites.setFavorite(item.id, newFavorite)
    item.isFavorite = newFavorite
    if (!newFavorite && favoritesOnly.value)
      await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    togglingIds.value = togglingIds.value.filter(id => id !== item.id)
  }
}

async function sync() {
  syncing.value = true
  setActionLoading(true)
  syncResult.value = null
  error.value = null
  try {
    syncResult.value = await api.prdbSync.syncAll()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    syncing.value = false
    setActionLoading(false)
  }
}

const { sfwMode } = useSfwMode()
const { mobile } = useDisplay()
const { setActions, clearAction, setActionLoading } = usePageAction()
const { filterPanelOpen, toggle, closePanel } = useFilterPanel()

const filtersActive = computed(() => !!search.value || !favoritesOnly.value)

onMounted(() => {
  load()
  setActions(
    { icon: 'mdi-sync', title: 'Sync', onClick: sync },
    { icon: 'mdi-tune', title: 'Toggle filters', onClick: toggle, badgeActive: () => filtersActive.value, mobileOnly: true },
  )
})

onUnmounted(() => {
  clearAction()
  closePanel()
})
</script>
