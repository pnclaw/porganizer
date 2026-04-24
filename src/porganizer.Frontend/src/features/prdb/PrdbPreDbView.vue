<template>
  <v-container>
    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">
      {{ error }}
    </v-alert>

    <v-expand-transition>
      <v-row v-if="!mobile || filterPanelOpen" class="mb-4">
        <v-col cols="12" md="5">
          <v-text-field
            v-model="search"
            prepend-inner-icon="mdi-magnify"
            label="Search PreDB, video, or site"
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
        <v-col cols="12" sm="6" md="3">
          <v-select
            v-model="linkStatus"
            :items="linkStatusOptions"
            item-title="title"
            item-value="value"
            label="Link status"
            hide-details
            @update:model-value="onFilterChange"
          />
        </v-col>
      </v-row>
    </v-expand-transition>

    <div v-if="loading" class="d-flex justify-center py-10">
      <v-progress-circular indeterminate />
    </div>

    <template v-else>
      <div class="d-flex align-center justify-space-between flex-wrap ga-3 mb-4">
        <div class="text-body-2 text-medium-emphasis">
          {{ total.toLocaleString() }} {{ total === 1 ? 'entry' : 'entries' }}
        </div>
        <v-chip-group>
          <v-chip size="small" variant="tonal">
            Sorted by latest PreDB timestamp
          </v-chip>
          <v-chip
            v-if="linkStatus === 'linked'"
            size="small"
            color="success"
            variant="tonal"
          >
            Linked videos only
          </v-chip>
          <v-chip
            v-else-if="linkStatus === 'unlinked'"
            size="small"
            color="warning"
            variant="tonal"
          >
            Unlinked only
          </v-chip>
        </v-chip-group>
      </div>

      <v-card v-if="entries.length" rounded="xl">
        <v-list lines="three" density="comfortable">
          <template v-for="(item, index) in entries" :key="item.id">
            <v-list-item class="py-2">
              <template #prepend>
                <v-avatar
                  :color="item.hasLinkedVideo ? 'success' : 'warning'"
                  variant="tonal"
                  class="mr-3"
                >
                  <v-icon>
                    {{ item.hasLinkedVideo ? 'mdi-link-variant' : 'mdi-link-variant-off' }}
                  </v-icon>
                </v-avatar>
              </template>

              <v-list-item-title class="text-body-2 font-weight-medium text-wrap">
                {{ item.title }}
              </v-list-item-title>

              <v-list-item-subtitle class="text-wrap">
                <div class="d-flex flex-wrap ga-2 mt-1">
                  <v-chip size="x-small" variant="outlined">
                    {{ formatDateTime(item.createdAtUtc) }}
                  </v-chip>
                  <v-chip
                    v-if="item.siteTitle"
                    size="x-small"
                    variant="outlined"
                  >
                    {{ item.siteTitle }}
                  </v-chip>
                  <v-chip
                    :color="item.hasLinkedVideo ? 'success' : 'warning'"
                    size="x-small"
                    variant="tonal"
                  >
                    {{ item.hasLinkedVideo ? 'Linked' : 'Unlinked' }}
                  </v-chip>
                </div>
                <div class="text-caption text-medium-emphasis mt-2">
                  {{ videoSummary(item) }}
                </div>
              </v-list-item-subtitle>

              <template #append>
                <v-btn
                  v-if="item.videoId"
                  icon="mdi-open-in-new"
                  variant="text"
                  color="primary"
                  :title="`Open ${item.videoTitle ?? 'video'} detail`"
                  @click="router.push(`/prdb/videos/${item.videoId}`)"
                />
              </template>
            </v-list-item>

            <v-divider v-if="index < entries.length - 1" />
          </template>
        </v-list>
      </v-card>

      <div v-else class="text-center text-medium-emphasis py-10">
        No PreDB entries found.
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
import { useRouter } from 'vue-router'
import { useDisplay } from 'vuetify'
import { api, type PrdbPreDbEntry, type PrdbPreDbFilterOptions } from '../../api'
import { usePageAction } from '../../composables/usePageAction'
import { useFilterPanel } from '../../composables/useFilterPanel'

type LinkStatus = 'all' | 'linked' | 'unlinked'

const router = useRouter()
const { mobile } = useDisplay()
const { setActions, clearAction } = usePageAction()
const { filterPanelOpen, toggle, closePanel } = useFilterPanel()

const entries = ref<PrdbPreDbEntry[]>([])
const total = ref(0)
const loading = ref(false)
const error = ref<string | null>(null)
const loadingOptions = ref(false)
const filterOptions = ref<PrdbPreDbFilterOptions>({ sites: [] })

const search = ref('')
const selectedSiteId = ref<string | null>(null)
const linkStatus = ref<LinkStatus>('all')

const pagination = reactive({ page: 1, pageSize: 50 })

const totalPages = computed(() => Math.ceil(total.value / pagination.pageSize))

const linkStatusOptions: { title: string; value: LinkStatus }[] = [
  { title: 'Linked to video', value: 'linked' },
  { title: 'All entries', value: 'all' },
  { title: 'Unlinked only', value: 'unlinked' },
]

const filtersActive = computed(() =>
  !!search.value || !!selectedSiteId.value || linkStatus.value !== 'all'
)

function hasLinkedVideoParam(): boolean | undefined {
  if (linkStatus.value === 'linked') return true
  if (linkStatus.value === 'unlinked') return false
  return undefined
}

async function load() {
  loading.value = true
  error.value = null
  try {
    const result = await api.prdbPreDb.list({
      search: search.value || undefined,
      siteId: selectedSiteId.value ?? undefined,
      hasLinkedVideo: hasLinkedVideoParam(),
      page: pagination.page,
      pageSize: pagination.pageSize,
    })
    entries.value = result.items
    total.value = result.total
  } catch (e: any) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

async function loadFilterOptions() {
  loadingOptions.value = true
  try {
    filterOptions.value = await api.prdbPreDb.filterOptions()
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

function onPageChange(page: number) {
  pagination.page = page
  load()
}

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatDate(iso: string | null): string {
  if (!iso) return 'unknown release date'
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

function videoSummary(item: PrdbPreDbEntry): string {
  if (!item.videoTitle) return 'No linked video in the local library yet.'
  return `${item.videoTitle} · ${formatDate(item.releaseDate)}`
}

onMounted(() => {
  loadFilterOptions()
  load()
  setActions({
    icon: 'mdi-tune',
    title: 'Toggle filters',
    onClick: toggle,
    badgeActive: () => filtersActive.value,
    mobileOnly: true,
  })
})

onUnmounted(() => {
  clearAction()
  closePanel()
})
</script>
