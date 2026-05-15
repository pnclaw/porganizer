import { createRouter, createWebHistory } from 'vue-router'
import LoginView from '../features/auth/LoginView.vue'
import SyncStatusView from '../features/admin/SyncStatusView.vue'
import AppLogsView from '../features/admin/AppLogsView.vue'
import DatabaseView from '../features/admin/DatabaseView.vue'
import IndexersView from '../features/indexers/IndexersView.vue'
import IndexerRowsView from '../features/indexers/IndexerRowsView.vue'
import IndexerStatsView from '../features/indexers/IndexerStatsView.vue'
import UsenetSearchView from '../features/indexers/UsenetSearchView.vue'
import DownloadClientsView from '../features/download-clients/DownloadClientsView.vue'
import DownloadsView from '../features/downloads/DownloadsView.vue'
import SettingsView from '../features/settings/SettingsView.vue'
import AdvancedSettingsView from '../features/settings/AdvancedSettingsView.vue'
import PrdbSitesView from '../features/prdb/PrdbSitesView.vue'
import PrdbSiteVideosView from '../features/prdb/PrdbSiteVideosView.vue'
import PrdbActorsView from '../features/prdb/PrdbActorsView.vue'
import PrdbWantedVideosView from '../features/prdb/PrdbWantedVideosView.vue'
import PrdbVideoDetailView from '../features/prdb/PrdbVideoDetailView.vue'
import PrdbVideosView from '../features/prdb/PrdbVideosView.vue'
import PrdbPreDbView from '../features/prdb/PrdbPreDbView.vue'
import LibraryView from '../features/library/LibraryView.vue'
import LibraryVideoDetailView from '../features/library/LibraryVideoDetailView.vue'
import LibraryFoldersView from '../features/library/LibraryFoldersView.vue'
import RescueView from '../features/rescue/RescueView.vue'
import { useAuth } from '../composables/useAuth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', component: LoginView, meta: { title: 'Sign In', public: true } },
    { path: '/', redirect: '/prdb/wanted' },
    { path: '/sync-status', component: SyncStatusView, meta: { title: 'Sync Status' } },
    { path: '/admin/logs', component: AppLogsView, meta: { title: 'Logs' } },
    { path: '/admin/database', component: DatabaseView, meta: { title: 'Database' } },
    { path: '/indexers', component: IndexersView, meta: { title: 'Indexers' } },
    { path: '/indexers/:id/rows', component: IndexerRowsView, meta: { title: 'Indexer Rows' } },
    { path: '/indexers/:id/stats', component: IndexerStatsView, meta: { title: 'Indexer Stats' } },
    { path: '/usenet/search', component: UsenetSearchView, meta: { title: 'Search' } },
    { path: '/download-clients', component: DownloadClientsView, meta: { title: 'Download Clients' } },
    { path: '/downloads', component: DownloadsView, meta: { title: 'Downloads' } },
    { path: '/settings', component: SettingsView, meta: { title: 'Settings' } },
    { path: '/settings/advanced', component: AdvancedSettingsView, meta: { title: 'Advanced Settings' } },
    { path: '/prdb/sites', component: PrdbSitesView, meta: { title: 'PRDB Sites' } },
    { path: '/prdb/sites/:id/videos', component: PrdbSiteVideosView, meta: { title: 'PRDB Site Videos' } },
    { path: '/prdb/actors', component: PrdbActorsView, meta: { title: 'PRDB Actors' } },
    { path: '/prdb/wanted', component: PrdbWantedVideosView, meta: { title: 'Wanted' } },
    { path: '/prdb/predb', component: PrdbPreDbView, meta: { title: 'PreDB' } },
    { path: '/prdb/videos', component: PrdbVideosView, meta: { title: 'Videos' } },
    { path: '/prdb/videos/:id', component: PrdbVideoDetailView, meta: { title: 'Video' } },
    { path: '/library', component: LibraryView, meta: { title: 'Library' } },
    { path: '/library/videos/:id', component: LibraryVideoDetailView, meta: { title: 'Library Video' } },
    { path: '/library/folders', component: LibraryFoldersView, meta: { title: 'Library Folders' } },
    { path: '/rescue', component: RescueView, meta: { title: 'Rescue' } },
  ],
})

const { authStatus, fetchStatus } = useAuth()

router.beforeEach(async (to) => {
  await fetchStatus()

  const status = authStatus.value
  if (!status) return true

  if (to.path === '/login') {
    if (!status.required || status.authenticated) return '/'
    return true
  }

  if (status.required && !status.authenticated) return '/login'
  return true
})

export default router
