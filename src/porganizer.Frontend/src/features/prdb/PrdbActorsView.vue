<template>
  <v-container style="max-width: 1200px">
    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">
      {{ error }}
    </v-alert>

    <v-alert
      v-if="!loading && favoritesOnly && !search && total === 0"
      type="info"
      class="mb-4"
    >
      No favorite actors found. Try syncing, or turn off <strong>Favorites only</strong> to see all actors.
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

    <div v-else-if="actors.length === 0" class="text-center py-8 text-medium-emphasis">
      No actors found.
    </div>

    <template v-else>
      <v-row>
        <v-col
          v-for="actor in actors"
          :key="actor.id"
          cols="4"
          sm="3"
          md="2"
        >
          <v-card height="100%" class="text-center">
            <v-btn
              icon
              size="x-small"
              variant="text"
              class="position-absolute ma-1"
              style="top: 0; right: 0; z-index: 1"
              :loading="togglingIds.includes(actor.id)"
              @click="toggleFavorite(actor)"
            >
              <v-icon :color="actor.isFavorite ? 'amber' : 'default'" size="small">
                {{ actor.isFavorite ? 'mdi-star' : 'mdi-star-outline' }}
              </v-icon>
            </v-btn>

            <v-card-text class="pa-3 pb-3">
              <v-avatar size="96" class="mb-2" :style="sfwMode ? 'filter: blur(12px)' : ''">
                <v-img
                  v-if="actor.profileImageUrl"
                  :src="actor.profileImageUrl"
                  cover
                >
                  <template #error>
                    <v-icon size="48" color="medium-emphasis">mdi-account</v-icon>
                  </template>
                </v-img>
                <v-icon v-else size="48" color="medium-emphasis">mdi-account</v-icon>
              </v-avatar>

              <div class="text-body-2 font-weight-bold text-wrap">{{ actor.name }}</div>
              <div class="text-caption text-medium-emphasis mt-1">{{ genderLabel(actor.gender) }}</div>
              <div v-if="actor.birthday" class="text-caption text-medium-emphasis">
                {{ actor.birthday }}
              </div>
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
import { api, type PrdbActor } from '../../api'
import { useSfwMode } from '../../composables/useSfwMode'
import { usePageAction } from '../../composables/usePageAction'
import { useFilterPanel } from '../../composables/useFilterPanel'

const { mobile } = useDisplay()
const { sfwMode } = useSfwMode()
const { setActions, clearAction } = usePageAction()
const { filterPanelOpen, toggle, closePanel } = useFilterPanel()

const actors      = ref<PrdbActor[]>([])
const total       = ref(0)
const loading     = ref(false)
const error       = ref<string | null>(null)
const search      = ref('')
const favoritesOnly = ref(true)
const togglingIds = ref<string[]>([])

const pagination = reactive({ page: 1, pageSize: 50 })

const pageCount = computed(() => Math.ceil(total.value / pagination.pageSize))

async function load() {
  loading.value = true
  error.value = null
  try {
    const result = await api.prdbActors.list({
      search: search.value || undefined,
      favoritesOnly: favoritesOnly.value || undefined,
      page: pagination.page,
      pageSize: pagination.pageSize,
    })
    actors.value = result.items
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

async function toggleFavorite(item: PrdbActor) {
  if (togglingIds.value.includes(item.id)) return
  togglingIds.value = [...togglingIds.value, item.id]
  const newFavorite = !item.isFavorite
  try {
    await api.prdbActors.setFavorite(item.id, newFavorite)
    item.isFavorite = newFavorite
    if (!newFavorite && favoritesOnly.value)
      await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    togglingIds.value = togglingIds.value.filter(id => id !== item.id)
  }
}

const genderLabels: Record<number, string> = { 1: 'Female', 2: 'Male', 3: 'Trans Female', 4: 'Trans Male' }
function genderLabel(gender: number): string {
  return genderLabels[gender] ?? ''
}

const filtersActive = computed(() => !!search.value || !favoritesOnly.value)

onMounted(() => {
  load()
  setActions({ icon: 'mdi-tune', title: 'Toggle filters', onClick: toggle, badgeActive: () => filtersActive.value, mobileOnly: true })
})

onUnmounted(() => {
  clearAction()
  closePanel()
})
</script>
