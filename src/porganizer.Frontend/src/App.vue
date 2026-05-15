<template>
  <v-app>
    <v-app-bar>
      <v-app-bar-nav-icon @click="drawer = !drawer" />
      <v-app-bar-title>{{ mobile ? pageTitle : `Porganizer — ${pageTitle}` }}</v-app-bar-title>
      <template #append>
        <v-btn
          v-for="(action, i) in pageActions"
          v-show="!action.mobileOnly || mobile"
          :key="i"
          :title="action.title"
          :loading="i === 0 && pageActionLoading"
          icon
          @click="action.onClick()"
        >
          <v-badge
            :model-value="action.badgeActive?.() ?? false"
            dot
            color="primary"
            floating
          >
            <v-icon>{{ action.icon }}</v-icon>
          </v-badge>
        </v-btn>
      </template>
    </v-app-bar>

    <v-navigation-drawer v-model="drawer">
      <v-list v-model:opened="openGroups" open-strategy="multiple">
        <v-list-group value="usenet">
          <template #activator="{ props }">
            <v-list-item v-bind="props" title="Usenet" rounded="lg" />
          </template>
          <v-list-item prepend-icon="mdi-magnify" title="Search" to="/usenet/search" rounded="lg" />
          <v-list-item prepend-icon="mdi-database-search" title="Indexers" to="/indexers" rounded="lg" />
          <v-list-item prepend-icon="mdi-download-network" title="Download Clients" to="/download-clients" rounded="lg" />
          <v-list-item prepend-icon="mdi-download" title="Downloads" to="/downloads" rounded="lg" />
        </v-list-group>

        <v-list-group value="prdb">
          <template #activator="{ props }">
            <v-list-item v-bind="props" title="PRDB" rounded="lg" />
          </template>
          <v-list-item prepend-icon="mdi-bookmark-multiple" title="Wanted" to="/prdb/wanted" rounded="lg" />
          <v-list-item prepend-icon="mdi-video" title="Videos" to="/prdb/videos" rounded="lg" />
          <v-list-item prepend-icon="mdi-web" title="Sites" to="/prdb/sites" rounded="lg" />
          <v-list-item prepend-icon="mdi-account-group" title="Actors" to="/prdb/actors" rounded="lg" />
          <v-list-item prepend-icon="mdi-format-list-bulleted-square" title="PreDB" to="/prdb/predb" rounded="lg" />
        </v-list-group>

        <v-list-group value="library">
          <template #activator="{ props }">
            <v-list-item v-bind="props" title="Library" rounded="lg" />
          </template>
          <v-list-item prepend-icon="mdi-filmstrip-box-multiple" title="Library" to="/library" rounded="lg" />
          <v-list-item prepend-icon="mdi-folder-multiple-outline" title="Folders" to="/library/folders" rounded="lg" />
        </v-list-group>
      </v-list>

      <template #append>
        <v-list v-model:opened="openGroups" open-strategy="multiple">
          <v-divider />
          <v-list-group value="admin">
            <template #activator="{ props }">
              <v-list-item v-bind="props" title="Admin" rounded="lg" />
            </template>
            <v-list-item prepend-icon="mdi-cog" title="Settings" to="/settings" rounded="lg" />
            <v-list-item prepend-icon="mdi-chart-box" title="Sync Status" to="/sync-status" rounded="lg" />
            <v-list-item prepend-icon="mdi-text-box-search" title="Logs" to="/admin/logs" rounded="lg" />
            <v-list-item prepend-icon="mdi-database" title="Database" to="/admin/database" rounded="lg" />
            <v-list-item prepend-icon="mdi-ambulance" title="Rescue" to="/rescue" rounded="lg" />
          </v-list-group>
          <template v-if="authStatus?.required">
            <v-list-item
              prepend-icon="mdi-logout"
              title="Logout"
              rounded="lg"
              @click="logout"
            />
          </template>
        </v-list>
      </template>
    </v-navigation-drawer>

    <v-main>
      <router-view />
    </v-main>
  </v-app>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useDisplay } from 'vuetify'
import { api } from './api'
import { useSfwMode } from './composables/useSfwMode'
import { usePageAction } from './composables/usePageAction'
import { useAuth } from './composables/useAuth'

const { sfwMode } = useSfwMode()
const { pageActions, pageActionLoading } = usePageAction()
const { authStatus, invalidate } = useAuth()
const route = useRoute()
const router = useRouter()
const { mobile } = useDisplay()

async function logout() {
  await api.auth.logout()
  invalidate()
  router.push('/login')
}

const drawer = ref(true)

const OPEN_GROUPS_KEY = 'porganizer:nav:openGroups'
const defaultOpenGroups = ['usenet', 'prdb', 'library']

function loadOpenGroups(): string[] {
  try {
    const stored = localStorage.getItem(OPEN_GROUPS_KEY)
    return stored ? JSON.parse(stored) : defaultOpenGroups
  } catch {
    return defaultOpenGroups
  }
}

const openGroups = ref<string[]>(loadOpenGroups())

watch(openGroups, (val) => {
  localStorage.setItem(OPEN_GROUPS_KEY, JSON.stringify(val))
}, { deep: true })

const pageTitle = computed(() => (route.meta.title as string) ?? 'Porganizer')

// On mobile, close the drawer after navigating
watch(
  () => route.path,
  () => { if (mobile.value) drawer.value = false }
)

onMounted(async () => {
  if (mobile.value) drawer.value = false
  try {
    const settings = await api.settings.get()
    sfwMode.value = settings.safeForWork
  } catch {
    // non-critical — SFW defaults to off
  }
})
</script>

<style scoped>
:deep(.v-list-group__items .v-list-item) {
  --indent-padding: 0px;
}
</style>
