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
        <!-- Sprite sheet card -->
        <v-col v-if="video.spriteSheetCdnUrl && video.spriteTileCount && video.spriteColumns && video.spriteRows" cols="12" md="5" lg="4">
          <div
            class="rounded overflow-hidden"
            :style="[
              sfwMode ? 'filter: blur(12px)' : '',
              {
                aspectRatio: '16/9',
                backgroundImage: `url(${video.spriteSheetCdnUrl})`,
                backgroundSize: `${video.spriteColumns * 100}% ${video.spriteRows * 100}%`,
                backgroundPosition: spritePosition(activeTile, video.spriteColumns, video.spriteRows),
                backgroundRepeat: 'no-repeat',
              },
            ]"
          />
        </v-col>

        <!-- User images carousel -->
        <v-col v-if="video.userImageCdnPaths.length > 0" cols="12" md="5" lg="4">
          <div class="text-caption text-medium-emphasis mb-1">User images</div>
          <v-carousel
            :show-arrows="video.userImageCdnPaths.length > 1"
            :hide-delimiters="video.userImageCdnPaths.length <= 1"
            height="auto"
            class="rounded"
            :style="sfwMode ? 'filter: blur(12px)' : ''"
          >
            <v-carousel-item
              v-for="path in video.userImageCdnPaths"
              :key="path"
              :src="path"
              aspect-ratio="16/9"
              cover
            />
          </v-carousel>
        </v-col>

        <!-- Metadata column -->
        <v-col cols="12" md="7" lg="8">
          <div class="text-caption text-medium-emphasis mb-1">{{ video.siteTitle }}</div>
          <h1 class="text-h5 mb-4">{{ video.title }}</h1>

          <div v-if="video.releaseDate" class="text-body-2 mb-4">
            <span class="text-medium-emphasis">Released:</span> {{ formatDate(video.releaseDate) }}
          </div>

          <v-chip color="success" variant="tonal" prepend-icon="mdi-harddisk" class="mb-4">
            {{ video.localFiles.length }} local {{ video.localFiles.length === 1 ? 'file' : 'files' }}
          </v-chip>
        </v-col>
      </v-row>

      <!-- PRDB images carousel -->
      <div v-if="video.prdbImagePaths.length > 0" class="mb-6">
        <div class="text-subtitle-1 font-weight-medium mb-2">Video images</div>
        <v-carousel
          :show-arrows="video.prdbImagePaths.length > 1"
          :hide-delimiters="video.prdbImagePaths.length <= 1"
          height="auto"
          class="rounded"
          :style="sfwMode ? 'filter: blur(12px)' : ''"
        >
          <v-carousel-item
            v-for="path in video.prdbImagePaths"
            :key="path"
            :src="path"
            aspect-ratio="16/9"
            cover
          />
        </v-carousel>
      </div>

      <!-- Local files -->
      <div class="mb-6">
        <div class="text-subtitle-1 font-weight-medium mb-2">Local files</div>
        <div class="d-flex flex-column ga-2">
          <v-card
            v-for="file in video.localFiles"
            :key="file.id"
            variant="outlined"
          >
            <v-card-text class="py-2 px-3">
              <div class="text-body-2 mb-1" style="word-break: break-all; font-family: monospace">
                {{ file.folderPath }}/{{ file.relativePath }}
              </div>
              <div class="d-flex align-center flex-wrap ga-2 mt-1">
                <span class="text-caption text-medium-emphasis">{{ formatSize(file.fileSize) }}</span>
                <span v-if="file.osHash" class="text-caption text-medium-emphasis">
                  · OSHash: {{ file.osHash }}
                </span>
                <span class="text-caption text-medium-emphasis">
                  · Last seen {{ formatDate(file.lastSeenAtUtc) }}
                </span>
              </div>
            </v-card-text>
          </v-card>
        </div>
      </div>

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

      <div v-if="video.preNames.length > 0" class="mb-6">
        <div class="text-subtitle-1 font-weight-medium mb-2">Alternative titles</div>
        <div v-for="name in video.preNames" :key="name" class="text-body-2 mb-1">
          {{ name }}
        </div>
      </div>
    </template>
  </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useRoute } from 'vue-router'
import { api, type LibraryVideoDetail } from '../../api'
import { useSfwMode } from '../../composables/useSfwMode'

const { sfwMode } = useSfwMode()
const route = useRoute()
const id = route.params.id as string

const video   = ref<LibraryVideoDetail | null>(null)
const loading = ref(false)
const error   = ref<string | null>(null)
const activeTile = ref(0)
let spriteTimer: ReturnType<typeof setInterval> | null = null

function spritePosition(tile: number, cols: number, rows: number): string {
  const col = tile % cols
  const row = Math.floor(tile / cols)
  const x = cols > 1 ? col * 100 / (cols - 1) : 0
  const y = rows > 1 ? row * 100 / (rows - 1) : 0
  return `${x}% ${y}%`
}

function startSpriteAnimation() {
  if (!video.value?.spriteSheetCdnUrl || !video.value.spriteTileCount || video.value.spriteTileCount < 2) return
  const tileCount = video.value.spriteTileCount
  spriteTimer = setInterval(() => {
    activeTile.value = (activeTile.value + 1) % tileCount
  }, 800)
}

function stopSpriteAnimation() {
  if (spriteTimer !== null) {
    clearInterval(spriteTimer)
    spriteTimer = null
  }
}

async function load() {
  loading.value = true
  error.value = null
  try {
    video.value = await api.libraryVideos.get(id)
    startSpriteAnimation()
  } catch (e: any) {
    error.value = e.message
  } finally {
    loading.value = false
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

onMounted(load)
onUnmounted(stopSpriteAnimation)
</script>
