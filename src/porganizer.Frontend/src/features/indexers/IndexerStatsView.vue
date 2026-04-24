<template>
  <v-container fluid>
    <v-row align="center" class="mb-4">
      <v-col cols="auto">
        <v-btn icon="mdi-arrow-left" variant="text" @click="router.push('/indexers')" />
      </v-col>
      <v-col>
        <p class="text-caption text-medium-emphasis">Last 30 days</p>
      </v-col>
    </v-row>

    <v-alert v-if="error" type="error" class="mb-4">{{ error }}</v-alert>

    <template v-if="stats">
      <!-- Summary cards -->
      <v-row class="mb-4">
        <v-col cols="12" sm="6" md="3">
          <v-card variant="tonal" color="primary">
            <v-card-text class="text-center">
              <div class="text-h3 font-weight-bold">{{ stats.totalSearchRequests }}</div>
              <div class="text-body-2 mt-1">Total Search Requests</div>
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="12" sm="6" md="3">
          <v-card variant="tonal" color="secondary">
            <v-card-text class="text-center">
              <div class="text-h3 font-weight-bold">{{ stats.totalGrabs }}</div>
              <div class="text-body-2 mt-1">Total Grabs</div>
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="12" sm="6" md="3">
          <v-card variant="tonal" color="success">
            <v-card-text class="text-center">
              <div class="text-h3 font-weight-bold">{{ stats.totalRows.toLocaleString() }}</div>
              <div class="text-body-2 mt-1">Rows Stored</div>
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="12" sm="6" md="3">
          <v-card variant="tonal" color="warning">
            <v-card-text class="text-center">
              <div class="text-h3 font-weight-bold">
                {{ stats.avgResponseTimeMs != null ? stats.avgResponseTimeMs + ' ms' : '—' }}
              </div>
              <div class="text-body-2 mt-1">Avg Response Time</div>
            </v-card-text>
          </v-card>
        </v-col>
      </v-row>

      <!-- Charts row 1 -->
      <v-row class="mb-4">
        <v-col cols="12" md="8">
          <v-card>
            <v-card-title class="text-body-1 font-weight-medium pt-4 px-4">Requests per Day</v-card-title>
            <v-card-text>
              <Bar :data="requestsPerDayData" :options="barOptions" style="height: 260px;" />
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="12" md="4">
          <v-card height="100%">
            <v-card-title class="text-body-1 font-weight-medium pt-4 px-4">Search Success Rate</v-card-title>
            <v-card-text class="d-flex align-center justify-center" style="height: 260px;">
              <template v-if="stats.searchSuccess + stats.searchFailure > 0">
                <Doughnut :data="successRateData" :options="doughnutOptions" style="max-height: 220px;" />
              </template>
              <span v-else class="text-medium-emphasis">No data</span>
            </v-card-text>
          </v-card>
        </v-col>
      </v-row>

      <!-- Charts row 2 -->
      <v-row>
        <v-col cols="12" md="6">
          <v-card>
            <v-card-title class="text-body-1 font-weight-medium pt-4 px-4">Avg Response Time per Day (ms)</v-card-title>
            <v-card-text>
              <Line :data="responseTimeData" :options="lineOptions" style="height: 240px;" />
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="12" md="6">
          <v-card>
            <v-card-title class="text-body-1 font-weight-medium pt-4 px-4">Rows by Category (top 10)</v-card-title>
            <v-card-text>
              <template v-if="stats.rowsByCategory.length > 0">
                <Bar :data="rowsByCategoryData" :options="horizontalBarOptions" style="height: 240px;" />
              </template>
              <span v-else class="text-medium-emphasis">No rows stored yet</span>
            </v-card-text>
          </v-card>
        </v-col>
      </v-row>
    </template>

    <v-row v-else-if="loading" class="mt-8" justify="center">
      <v-progress-circular indeterminate color="primary" />
    </v-row>
  </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { Bar, Doughnut, Line } from 'vue-chartjs'
import {
  Chart as ChartJS,
  CategoryScale, LinearScale, BarElement, LineElement, PointElement,
  ArcElement, Tooltip, Legend, Filler,
} from 'chart.js'
import { api, type IndexerStats } from '../../api'

ChartJS.register(CategoryScale, LinearScale, BarElement, LineElement, PointElement, ArcElement, Tooltip, Legend, Filler)

const router = useRouter()
const route = useRoute()
const id = route.params.id as string

const stats = ref<IndexerStats | null>(null)
const indexerTitle = ref('')
const loading = ref(true)
const error = ref<string | null>(null)

// ── Chart data ────────────────────────────────────────────────────────────────

const requestsPerDayData = computed(() => ({
  labels: stats.value!.requestsPerDay.map(d => d.date.slice(5)), // MM-DD
  datasets: [
    {
      label: 'Search',
      data: stats.value!.requestsPerDay.map(d => d.search),
      backgroundColor: 'rgba(99, 179, 237, 0.8)',
      stack: 'requests',
    },
    {
      label: 'Grab',
      data: stats.value!.requestsPerDay.map(d => d.grab),
      backgroundColor: 'rgba(154, 230, 180, 0.8)',
      stack: 'requests',
    },
  ],
}))

const successRateData = computed(() => ({
  labels: ['Success', 'Failure'],
  datasets: [{
    data: [stats.value!.searchSuccess, stats.value!.searchFailure],
    backgroundColor: ['rgba(154, 230, 180, 0.85)', 'rgba(252, 129, 129, 0.85)'],
    borderWidth: 0,
  }],
}))

const responseTimeData = computed(() => ({
  labels: stats.value!.avgResponseTimePerDay.map(d => d.date.slice(5)),
  datasets: [{
    label: 'Avg ms',
    data: stats.value!.avgResponseTimePerDay.map(d => d.avgMs || null),
    borderColor: 'rgba(246, 173, 85, 0.9)',
    backgroundColor: 'rgba(246, 173, 85, 0.15)',
    fill: true,
    tension: 0.3,
    pointRadius: 2,
  }],
}))

const rowsByCategoryData = computed(() => ({
  labels: stats.value!.rowsByCategory.map(r => String(r.category)),
  datasets: [{
    label: 'Rows',
    data: stats.value!.rowsByCategory.map(r => r.count),
    backgroundColor: 'rgba(159, 122, 234, 0.8)',
  }],
}))

// ── Chart options ─────────────────────────────────────────────────────────────

const baseOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: { legend: { labels: { color: '#ccc' } } },
}

const barOptions = {
  ...baseOptions,
  scales: {
    x: { stacked: true, ticks: { color: '#999' }, grid: { color: '#333' } },
    y: { stacked: true, ticks: { color: '#999' }, grid: { color: '#333' } },
  },
}

const lineOptions = {
  ...baseOptions,
  scales: {
    x: { ticks: { color: '#999' }, grid: { color: '#333' } },
    y: { ticks: { color: '#999' }, grid: { color: '#333' }, beginAtZero: true },
  },
  plugins: { ...baseOptions.plugins, legend: { display: false } },
}

const horizontalBarOptions = {
  ...baseOptions,
  indexAxis: 'y' as const,
  scales: {
    x: { ticks: { color: '#999' }, grid: { color: '#333' } },
    y: { ticks: { color: '#999' }, grid: { color: '#333' } },
  },
  plugins: { ...baseOptions.plugins, legend: { display: false } },
}

const doughnutOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: { legend: { position: 'bottom' as const, labels: { color: '#ccc' } } },
}

// ── Data loading ──────────────────────────────────────────────────────────────

onMounted(async () => {
  try {
    const [indexer, data] = await Promise.all([
      api.indexers.get(id),
      api.indexers.stats(id),
    ])
    indexerTitle.value = indexer.title
    stats.value = data
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load stats'
  } finally {
    loading.value = false
  }
})
</script>
