<template>
  <v-container style="max-width: 900px">
    <v-btn
      variant="text"
      prepend-icon="mdi-arrow-left"
      to="/prdb/sites"
      class="mb-4 px-0"
    >
      Sites
    </v-btn>

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
      </v-row>
    </v-expand-transition>

    <div v-if="loading" class="text-center py-8">
      <v-progress-circular indeterminate color="primary" />
    </div>

    <div v-else-if="videos.length === 0" class="text-center py-8 text-medium-emphasis">
      No videos found.
    </div>

    <template v-else>
      <v-list lines="two" class="pa-0">
        <template v-for="(video, index) in videos" :key="video.id">
          <v-list-item
            class="py-2"
            :ripple="true"
            @click="router.push(`/prdb/videos/${video.id}`)"
          >
            <v-list-item-title class="font-weight-medium">{{ video.title }}</v-list-item-title>
            <v-list-item-subtitle>
              {{ video.releaseDate ?? '—' }} · {{ video.actorCount }} actors
            </v-list-item-subtitle>

            <template #append>
              <div class="d-flex align-center ga-2">
                <v-chip
                  v-if="video.preNames.length > 0"
                  size="x-small"
                  variant="tonal"
                >
                  {{ video.preNames.length }} pre-names
                </v-chip>
                <v-icon size="small" color="medium-emphasis">mdi-chevron-right</v-icon>
              </div>
            </template>
          </v-list-item>

          <v-divider v-if="index < videos.length - 1" />
        </template>
      </v-list>

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
import { useRoute, useRouter } from 'vue-router'
import { api, type PrdbVideo } from '../../api'
import { usePageAction } from '../../composables/usePageAction'
import { useFilterPanel } from '../../composables/useFilterPanel'

const { mobile } = useDisplay()
const { setActions, clearAction } = usePageAction()
const { filterPanelOpen, toggle, closePanel } = useFilterPanel()

const route  = useRoute()
const router = useRouter()
const siteId = route.params.id as string

const videos  = ref<PrdbVideo[]>([])
const total   = ref(0)
const loading = ref(false)
const error   = ref<string | null>(null)
const search  = ref('')

const pagination = reactive({ page: 1, pageSize: 50 })

const pageCount = computed(() => Math.ceil(total.value / pagination.pageSize))

async function load() {
  loading.value = true
  error.value = null
  try {
    const result = await api.prdbSites.videos(siteId, {
      search: search.value || undefined,
      page: pagination.page,
      pageSize: pagination.pageSize,
    })
    videos.value = result.items
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

const filtersActive = computed(() => !!search.value)

onMounted(() => {
  load()
  setActions({ icon: 'mdi-tune', title: 'Toggle filters', onClick: toggle, badgeActive: () => filtersActive.value, mobileOnly: true })
})

onUnmounted(() => {
  clearAction()
  closePanel()
})
</script>
