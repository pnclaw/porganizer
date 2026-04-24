<template>
  <v-container>
    <v-dialog v-model="infoDialog" max-width="420">
      <v-card>
        <v-card-title class="text-body-1 font-weight-medium pt-4 pb-1">Background Sync Service</v-card-title>
        <v-card-text class="text-body-2">
          <p class="mb-2">A background service runs automatically every <strong>15 minutes</strong> and performs the following in order:</p>
          <ol class="pl-4">
            <li class="mb-1"><strong>Actor summary backfill</strong> — pages through all actors on prdb.net and inserts any not yet in the local DB (5 000 per run until complete, then checks for new actors each tick).</li>
            <li class="mb-1"><strong>Video detail sync</strong> — fetches full detail for videos that haven't been processed yet, populating cast, images, and pre-names.</li>
            <li class="mb-1"><strong>Actor detail backfill</strong> — batch-fetches full actor details (50 per API call, 1 000 per run) for all actors lacking detail.</li>
            <li class="mb-1"><strong>Filehash sync</strong> — pages through all video filehashes on prdb.net during initial backfill (oldest-first, 1 000 per run), then re-fetches the last 7 days each tick to pick up new and updated entries.</li>
            <li class="mb-1"><strong>Indexer filehash sync</strong> — backfills indexer-based filehashes from prdb.net, then stays in sync incrementally via the seek-paged changes feed.</li>
            <li class="mb-1"><strong>Wanted list sync</strong> — consumes the wanted-video change feed from prdb.net, applying created, updated, and deleted wanted entries incrementally.</li>
            <li><strong>Indexer row match</strong> — checks NZB titles from the last 7 days against known video prenames and links any exact (case-insensitive) matches.</li>
          </ol>
          <p class="mt-2 text-medium-emphasis">Individual steps can also be triggered manually using the Run Now buttons on each card.</p>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="infoDialog = false">Close</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-alert v-if="error" type="error" class="mb-4" closable @click:close="error = null">
      {{ error }}
    </v-alert>

    <v-row>
      <!-- prdb.net API Health -->
      <v-col cols="12" md="6">
        <v-card :loading="healthLoading">
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-api</v-icon>
            prdb.net API Health
          </v-card-title>
          <v-card-subtitle>
            Reachability check against the prdb.net API (<code>GET /health</code>), no authentication required.
          </v-card-subtitle>
          <v-card-text>
            <div v-if="health" class="d-flex align-center ga-4 mt-1">
              <v-chip
                :color="health.status === 'ok' ? 'success' : 'error'"
                size="large"
                :prepend-icon="health.status === 'ok' ? 'mdi-check-circle' : 'mdi-alert-circle'"
              >
                {{ health.status.toUpperCase() }}
              </v-chip>
              <span class="text-body-2 text-medium-emphasis">
                Last checked: {{ formatDate(health.timestamp) }}
              </span>
            </div>
            <v-skeleton-loader v-else-if="healthLoading" type="chip" />
            <v-alert v-if="healthError" type="error" class="mt-4">
              {{ healthError }}
            </v-alert>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <v-row v-if="status">
      <!-- Actor Summary Backfill -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-account-sync</v-icon>
            Actor Summary Backfill
            <v-spacer />
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-play"
              :loading="runningBackfill"
              @click="runBackfill"
            >
              Run Now
            </v-btn>
          </v-card-title>
          <v-card-text>
            <div v-if="status.actorBackfill.isComplete" class="d-flex align-center ga-2 mb-3">
              <v-icon color="success">mdi-check-circle</v-icon>
              <span class="text-success">Backfill complete</span>
            </div>
            <div v-else class="mb-3">
              <div class="d-flex justify-space-between mb-1">
                <span class="text-body-2">Progress</span>
                <span class="text-body-2">{{ backfillProgressLabel }}</span>
              </div>
              <v-progress-linear
                :model-value="backfillPercent"
                color="primary"
                height="8"
                rounded
              />
            </div>

            <v-table density="compact">
              <tbody>
                <tr>
                  <td class="text-medium-emphasis">Actors in DB</td>
                  <td>{{ status.actorBackfill.actorsInDb.toLocaleString() }}</td>
                </tr>
                <tr v-if="status.actorBackfill.totalActors">
                  <td class="text-medium-emphasis">Total on prdb</td>
                  <td>{{ status.actorBackfill.totalActors.toLocaleString() }}</td>
                </tr>
                <tr v-if="!status.actorBackfill.isComplete && status.actorBackfill.currentPage">
                  <td class="text-medium-emphasis">Next page</td>
                  <td>{{ status.actorBackfill.currentPage }}</td>
                </tr>
                <tr v-if="status.actorBackfill.lastSyncedAt">
                  <td class="text-medium-emphasis">Last synced</td>
                  <td>{{ formatDate(status.actorBackfill.lastSyncedAt) }}</td>
                </tr>
              </tbody>
            </v-table>
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Actor Detail Sync -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-account-details</v-icon>
            Actor Detail Sync
            <v-spacer />
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-play"
              :loading="runningVideoDetailSync"
              @click="runVideoDetailSync"
            >
              Run Now
            </v-btn>
          </v-card-title>
          <v-card-text>
            <div v-if="status.actorDetailSync.actorsPending === 0" class="d-flex align-center ga-2 mb-3">
              <v-icon color="success">mdi-check-circle</v-icon>
              <span class="text-success">All actors have full detail</span>
            </div>
            <div v-else class="mb-3">
              <div class="d-flex justify-space-between mb-1">
                <span class="text-body-2">Progress</span>
                <span class="text-body-2">{{ actorDetailProgressLabel }}</span>
              </div>
              <v-progress-linear
                :model-value="actorDetailPercent"
                color="primary"
                height="8"
                rounded
              />
            </div>

            <v-table density="compact">
              <tbody>
                <tr>
                  <td class="text-medium-emphasis">With detail</td>
                  <td>{{ status.actorDetailSync.actorsWithDetail.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Pending</td>
                  <td>{{ status.actorDetailSync.actorsPending.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Favourites</td>
                  <td>{{ status.actorDetailSync.favoriteActors.toLocaleString() }}</td>
                </tr>
              </tbody>
            </v-table>
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Video Detail Sync -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-video-check</v-icon>
            Video Detail Sync
            <v-spacer />
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-play"
              :loading="runningVideoDetailSync"
              @click="runVideoDetailSync"
            >
              Run Now
            </v-btn>
          </v-card-title>
          <v-card-text>
            <div v-if="status.videoDetailSync.videosPending === 0" class="d-flex align-center ga-2 mb-3">
              <v-icon color="success">mdi-check-circle</v-icon>
              <span class="text-success">All videos have full detail</span>
            </div>
            <div v-else class="mb-3">
              <div class="d-flex justify-space-between mb-1">
                <span class="text-body-2">Progress</span>
                <span class="text-body-2">{{ videoDetailProgressLabel }}</span>
              </div>
              <v-progress-linear
                :model-value="videoDetailPercent"
                color="primary"
                height="8"
                rounded
              />
            </div>

            <v-table density="compact">
              <tbody>
                <tr>
                  <td class="text-medium-emphasis">With detail</td>
                  <td>{{ status.videoDetailSync.videosWithDetail.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Pending</td>
                  <td>{{ status.videoDetailSync.videosPending.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">With cast linked</td>
                  <td>{{ status.videoDetailSync.videosWithCast.toLocaleString() }}</td>
                </tr>
              </tbody>
            </v-table>
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Prename Sync -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-alphabetical-variant</v-icon>
            Prename Sync
            <v-spacer />
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-restore"
              :loading="resettingPreNameCursor"
              @click="resetPreNameCursor"
            >
              Reset Cursor
            </v-btn>
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-play"
              :loading="runningPreNameSync"
              @click="runPreNameSync"
            >
              Run Now
            </v-btn>
          </v-card-title>
          <v-card-text>
            <v-table density="compact">
              <tbody>
                <tr>
                  <td class="text-medium-emphasis">Total in DB</td>
                  <td>{{ status.preNameSync.totalPreNames.toLocaleString() }}</td>
                </tr>
                <template v-if="status.preNameSync.isBackfilling">
                  <tr>
                    <td class="text-medium-emphasis">Status</td>
                    <td class="text-warning">
                      Backfill in progress — page {{ status.preNameSync.backfillPage ?? 1 }}
                      <template v-if="status.preNameSync.backfillTotalCount">
                        of {{ Math.ceil(status.preNameSync.backfillTotalCount / 500) }}
                      </template>
                    </td>
                  </tr>
                </template>
                <template v-else>
                  <tr v-if="status.preNameSync.lastSyncedAt">
                    <td class="text-medium-emphasis">Next sync fetches from</td>
                    <td>{{ formatDate(status.preNameSync.lastSyncedAt) }}</td>
                  </tr>
                </template>
              </tbody>
            </v-table>
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Filehash Sync -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-fingerprint</v-icon>
            Filehash Sync
            <v-spacer />
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-restore"
              :loading="resettingFilehashCursor"
              @click="resetFilehashCursor"
            >
              Reset Cursor
            </v-btn>
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-play"
              :loading="runningFilehashSync"
              @click="runFilehashSync"
            >
              Run Now
            </v-btn>
          </v-card-title>
          <v-card-text>
            <v-table density="compact">
              <tbody>
                <tr>
                  <td class="text-medium-emphasis">Total in DB</td>
                  <td>{{ status.filehashSync.totalInDb.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Verified</td>
                  <td>{{ status.filehashSync.verified.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Unverified</td>
                  <td>{{ (status.filehashSync.totalInDb - status.filehashSync.verified).toLocaleString() }}</td>
                </tr>
                <template v-if="status.filehashSync.isBackfilling">
                  <tr>
                    <td class="text-medium-emphasis">Status</td>
                    <td class="text-warning">
                      Backfill in progress — page {{ status.filehashSync.backfillPage ?? 1 }}
                      <template v-if="status.filehashSync.backfillTotalCount">
                        of {{ Math.ceil(status.filehashSync.backfillTotalCount / 100) }}
                      </template>
                    </td>
                  </tr>
                </template>
                <template v-else>
                  <tr>
                    <td class="text-medium-emphasis">Mode</td>
                    <td class="text-medium-emphasis">7-day rolling window</td>
                  </tr>
                  <tr v-if="status.filehashSync.lastSyncedAt">
                    <td class="text-medium-emphasis">Last synced</td>
                    <td>{{ formatDate(status.filehashSync.lastSyncedAt) }}</td>
                  </tr>
                </template>
              </tbody>
            </v-table>
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Indexer Filehash Sync -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-database-search</v-icon>
            Indexer Filehash Sync
            <v-spacer />
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-restore"
              :loading="resettingIndexerFilehashCursor"
              @click="resetIndexerFilehashCursor"
            >
              Reset Cursor
            </v-btn>
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-play"
              :loading="runningIndexerFilehashSync"
              @click="runIndexerFilehashSync"
            >
              Run Now
            </v-btn>
          </v-card-title>
          <v-card-text>
            <v-table density="compact">
              <tbody>
                <tr>
                  <td class="text-medium-emphasis">Total in DB</td>
                  <td>{{ status.indexerFilehashSync.totalInDb.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Verified</td>
                  <td>{{ status.indexerFilehashSync.verified.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Unverified</td>
                  <td>{{ (status.indexerFilehashSync.totalInDb - status.indexerFilehashSync.verified).toLocaleString() }}</td>
                </tr>
                <template v-if="status.indexerFilehashSync.isBackfilling">
                  <tr>
                    <td class="text-medium-emphasis">Status</td>
                    <td class="text-warning">
                      Backfill in progress — page {{ status.indexerFilehashSync.backfillPage ?? 1 }}
                      <template v-if="status.indexerFilehashSync.backfillTotalCount">
                        of {{ Math.ceil(status.indexerFilehashSync.backfillTotalCount / 1000) }}
                      </template>
                    </td>
                  </tr>
                </template>
                <template v-else>
                  <tr>
                    <td class="text-medium-emphasis">Mode</td>
                    <td class="text-medium-emphasis">Incremental (seek cursor)</td>
                  </tr>
                  <tr v-if="status.indexerFilehashSync.lastSyncedAt">
                    <td class="text-medium-emphasis">Last synced</td>
                    <td>{{ formatDate(status.indexerFilehashSync.lastSyncedAt) }}</td>
                  </tr>
                </template>
              </tbody>
            </v-table>
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Wanted List Sync -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-bookmark-check</v-icon>
            Wanted List Sync
            <v-spacer />
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-restore"
              :loading="resettingWantedVideoCursor"
              @click="resetWantedVideoCursor"
            >
              Reset Cursor
            </v-btn>
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-download"
              :loading="runningWantedFulfillment"
              @click="runWantedFulfillment"
            >
              Fulfill Now
            </v-btn>
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-play"
              :loading="runningWantedVideoSync"
              @click="runWantedVideoSync"
            >
              Run Now
            </v-btn>
          </v-card-title>
          <v-card-text>
            <v-table density="compact">
              <tbody>
                <tr>
                  <td class="text-medium-emphasis">Total on list</td>
                  <td>{{ status.wantedVideoSync.total.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Unfulfilled</td>
                  <td>{{ status.wantedVideoSync.unfulfilled.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Fulfilled</td>
                  <td>{{ status.wantedVideoSync.fulfilled.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Pending video detail</td>
                  <td>{{ status.wantedVideoSync.pendingDetail.toLocaleString() }}</td>
                </tr>
                <tr v-if="status.wantedVideoSync.lastSyncedAt">
                  <td class="text-medium-emphasis">Last synced</td>
                  <td>{{ formatDate(status.wantedVideoSync.lastSyncedAt) }}</td>
                </tr>
                <tr v-else>
                  <td class="text-medium-emphasis">Last synced</td>
                  <td class="text-medium-emphasis">Never</td>
                </tr>
              </tbody>
            </v-table>
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Favorites → Wanted Sync -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-heart-plus</v-icon>
            Favorites → Wanted Sync
            <v-spacer />
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-play"
              :loading="runningFavoritesWantedSync"
              :disabled="!status.favoritesWantedSync.isEnabled"
              @click="runFavoritesWantedSync"
            >
              Run Now
            </v-btn>
          </v-card-title>
          <v-card-text>
            <div class="d-flex align-center ga-2 mb-3">
              <v-icon :color="status.favoritesWantedSync.isEnabled ? 'success' : 'medium-emphasis'">
                {{ status.favoritesWantedSync.isEnabled ? 'mdi-check-circle' : 'mdi-pause-circle' }}
              </v-icon>
              <span :class="status.favoritesWantedSync.isEnabled ? 'text-success' : 'text-medium-emphasis'">
                {{ status.favoritesWantedSync.isEnabled ? 'Enabled' : 'Disabled' }}
              </span>
            </div>
            <v-table density="compact">
              <tbody>
                <tr>
                  <td class="text-medium-emphasis">Days back</td>
                  <td>{{ status.favoritesWantedSync.daysBack }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Last run</td>
                  <td>{{ status.favoritesWantedSync.lastRunAt ? formatDate(status.favoritesWantedSync.lastRunAt) : 'Never' }}</td>
                </tr>
              </tbody>
            </v-table>
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Downloaded-from-Indexer Sync -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-cloud-upload</v-icon>
            Downloaded-from-Indexer Sync
            <v-spacer />
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-play"
              :loading="runningDownloadedFromIndexerSync"
              @click="runDownloadedFromIndexerSync"
            >
              Run Now
            </v-btn>
          </v-card-title>
          <v-card-text>
            <v-table density="compact">
              <tbody>
                <tr>
                  <td class="text-medium-emphasis">Synced</td>
                  <td>{{ status.downloadedFromIndexerSync.synced.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Pending</td>
                  <td>{{ status.downloadedFromIndexerSync.pending.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Errors</td>
                  <td>
                    <span :class="status.downloadedFromIndexerSync.errors > 0 ? 'text-error' : ''">
                      {{ status.downloadedFromIndexerSync.errors.toLocaleString() }}
                    </span>
                  </td>
                </tr>
              </tbody>
            </v-table>
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Indexer Backfill -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-history</v-icon>
            Indexer Backfill
          </v-card-title>
          <v-card-text>
            <div v-if="status.indexerBackfills.length === 0" class="text-medium-emphasis">
              No indexers configured.
            </div>
            <v-table v-else density="compact">
              <thead>
                <tr>
                  <th class="text-left">Indexer</th>
                  <th class="text-left">Window</th>
                  <th class="text-left">Status</th>
                  <th class="text-left">Next page</th>
                  <th class="text-left">Last run</th>
                  <th class="text-left">Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="indexer in status.indexerBackfills" :key="indexer.indexerId">
                  <td>
                    <div>{{ indexer.indexerTitle }}</div>
                    <div class="text-caption text-medium-emphasis">
                      {{ indexer.isEnabled ? 'Enabled' : 'Disabled' }}
                    </div>
                    <div v-if="indexer.cutoffUtc" class="text-caption text-medium-emphasis">
                      Cutoff: {{ formatDate(indexer.cutoffUtc) }}
                    </div>
                  </td>
                  <td>{{ indexer.days.toLocaleString() }} day<span v-if="indexer.days !== 1">s</span></td>
                  <td>
                    <span v-if="!indexer.isEnabled" class="text-medium-emphasis">Disabled</span>
                    <span v-else-if="indexer.isComplete" class="text-success">
                      Complete
                      <span v-if="indexer.completedAtUtc" class="text-medium-emphasis">
                        · {{ formatDate(indexer.completedAtUtc) }}
                      </span>
                    </span>
                    <span v-else class="text-warning">
                      {{ indexer.startedAtUtc ? 'In progress' : 'Pending first run' }}
                    </span>
                  </td>
                  <td>
                    {{ indexer.currentOffset != null ? Math.floor(indexer.currentOffset / 100) + 1 : '—' }}
                  </td>
                  <td>
                    {{ indexer.lastRunAtUtc ? formatDate(indexer.lastRunAtUtc) : 'Never' }}
                  </td>
                  <td>
                    <v-btn
                      size="small"
                      variant="tonal"
                      prepend-icon="mdi-play"
                      :loading="runningIndexerBackfillId === indexer.indexerId"
                      :disabled="!indexer.isEnabled || indexer.isComplete || runningIndexerBackfillId !== null"
                      @click="runIndexerBackfill(indexer.indexerId)"
                    >
                      Run Now
                    </v-btn>
                  </td>
                </tr>
              </tbody>
            </v-table>
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Indexer Row Match Sync -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-link-variant</v-icon>
            Indexer Row Match
            <v-spacer />
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-bug"
              :loading="runningDebug"
              @click="runDebug"
            >
              Debug
            </v-btn>
            <v-btn
              size="small"
              variant="tonal"
              prepend-icon="mdi-play"
              :loading="runningIndexerRowMatch"
              @click="runIndexerRowMatch"
            >
              Run Now
            </v-btn>
          </v-card-title>
          <v-card-text>
            <v-table density="compact">
              <tbody>
                <tr>
                  <td class="text-medium-emphasis">Total matches</td>
                  <td>{{ status.indexerRowMatchSync.totalMatches.toLocaleString() }}</td>
                </tr>
                <tr v-if="status.indexerRowMatchSync.lastRunAt">
                  <td class="text-medium-emphasis">Last run</td>
                  <td>{{ formatDate(status.indexerRowMatchSync.lastRunAt) }}</td>
                </tr>
                <tr v-else>
                  <td class="text-medium-emphasis">Last run</td>
                  <td class="text-medium-emphasis">Never</td>
                </tr>
              </tbody>
            </v-table>

            <template v-if="status.indexerRowMatchSync.topIndexers.length">
              <div class="text-caption text-medium-emphasis mt-3 mb-1">Top indexers by row count</div>
              <v-table density="compact">
                <thead>
                  <tr>
                    <th class="text-left">Indexer</th>
                    <th class="text-right">Total</th>
                    <th class="text-right">Last 7 days</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="idx in status.indexerRowMatchSync.topIndexers" :key="idx.title">
                    <td>{{ idx.title }}</td>
                    <td class="text-right">{{ idx.totalRows.toLocaleString() }}</td>
                    <td class="text-right">{{ idx.rowsLastWeek.toLocaleString() }}</td>
                  </tr>
                </tbody>
              </v-table>
            </template>
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Preview Image Upload -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-image-multiple-outline</v-icon>
            Preview Image Upload
          </v-card-title>
          <v-card-text>
            <div class="d-flex align-center ga-2 mb-3">
              <v-icon :color="status.previewImageUpload.isEnabled ? 'success' : 'medium-emphasis'">
                {{ status.previewImageUpload.isEnabled ? 'mdi-check-circle' : 'mdi-pause-circle' }}
              </v-icon>
              <span :class="status.previewImageUpload.isEnabled ? 'text-success' : 'text-medium-emphasis'">
                {{ status.previewImageUpload.isEnabled ? 'Enabled' : 'Disabled' }}
              </span>
            </div>
            <v-table density="compact">
              <tbody>
                <tr>
                  <td class="text-medium-emphasis">Files uploaded</td>
                  <td>{{ status.previewImageUpload.filesUploaded.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Images uploaded</td>
                  <td>{{ status.previewImageUpload.imagesUploaded.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Pending upload</td>
                  <td>
                    <span :class="status.previewImageUpload.filesPending > 0 ? 'text-warning' : ''">
                      {{ status.previewImageUpload.filesPending.toLocaleString() }}
                    </span>
                  </td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Auto-delete after upload</td>
                  <td>{{ status.previewImageUpload.autoDeleteEnabled ? 'Yes' : 'No' }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Last uploaded</td>
                  <td>
                    {{ status.previewImageUpload.lastUploadedAt
                      ? formatDate(status.previewImageUpload.lastUploadedAt)
                      : '—' }}
                  </td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Awaiting preview generation</td>
                  <td>
                    <span :class="status.previewImageUpload.filesAwaitingPreviewGeneration > 0 ? 'text-warning' : ''">
                      {{ status.previewImageUpload.filesAwaitingPreviewGeneration.toLocaleString() }}
                    </span>
                  </td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Awaiting thumbnail generation</td>
                  <td>
                    <span :class="status.previewImageUpload.filesAwaitingThumbnailGeneration > 0 ? 'text-warning' : ''">
                      {{ status.previewImageUpload.filesAwaitingThumbnailGeneration.toLocaleString() }}
                    </span>
                  </td>
                </tr>
              </tbody>
            </v-table>
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Library Counts -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-database</v-icon>
            Library
            <v-spacer />
            <v-btn
              icon="mdi-information-outline"
              size="small"
              variant="text"
              @click="infoDialog = true"
            />
              <v-btn
                size="small"
                variant="tonal"
                prepend-icon="mdi-restore"
                :loading="resettingFavoriteSiteCursor || resettingFavoriteActorCursor"
                @click="resetFavoriteCursors"
              >
                Reset Favorite Cursors
              </v-btn>
              <v-btn
                size="small"
                variant="tonal"
                prepend-icon="mdi-play"
                :loading="runningSyncAll"
                @click="runSyncAll"
              >
                Run Now
            </v-btn>
          </v-card-title>
          <v-card-text>
            <v-table density="compact">
              <tbody>
                <tr>
                  <td class="text-medium-emphasis">Networks</td>
                  <td>{{ status.library.networks.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Sites</td>
                  <td>
                    {{ status.library.sites.toLocaleString() }}
                    <span class="text-medium-emphasis text-body-2">
                      ({{ status.library.favoriteSites.toLocaleString() }} favourite)
                    </span>
                  </td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Videos</td>
                  <td>
                    {{ status.library.videos.toLocaleString() }}
                    <span class="text-medium-emphasis text-body-2">
                      ({{ status.library.wantedVideos.toLocaleString() }} wanted)
                    </span>
                  </td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Pre-names</td>
                  <td>{{ status.library.preNames.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Filehashes</td>
                  <td>{{ status.library.filehashes.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Video images</td>
                  <td>{{ status.library.videoImages.toLocaleString() }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Actors</td>
                  <td>
                    {{ status.library.actors.toLocaleString() }}
                    <span class="text-medium-emphasis text-body-2">
                      ({{ status.library.favoriteActors.toLocaleString() }} favourite)
                    </span>
                  </td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">Actor images</td>
                  <td>{{ status.library.actorImages.toLocaleString() }}</td>
                </tr>
                <tr v-if="status.syncWorker.lastRunAt">
                  <td class="text-medium-emphasis">Last sync</td>
                  <td>{{ formatDate(status.syncWorker.lastRunAt) }}</td>
                </tr>
                <tr v-else>
                  <td class="text-medium-emphasis">Last sync</td>
                  <td class="text-medium-emphasis">Never</td>
                </tr>
                <tr v-if="status.syncWorker.nextRunAt">
                  <td class="text-medium-emphasis">Next sync</td>
                  <td>{{ formatDate(status.syncWorker.nextRunAt) }}</td>
                </tr>
              </tbody>
            </v-table>
          </v-card-text>
        </v-card>
      </v-col>

      <!-- Rate Limits -->
      <v-col cols="12" md="6">
        <v-card>
          <v-card-title class="d-flex align-center ga-2">
            <v-icon>mdi-gauge</v-icon>
            Rate Limits
            <v-spacer />
            <v-btn
              icon="mdi-refresh"
              size="small"
              variant="text"
              :loading="loading"
              @click="load"
            />
          </v-card-title>
          <v-card-text>
            <div v-if="!status.rateLimit" class="text-medium-emphasis">
              API key not configured or rate limit unavailable.
            </div>
            <template v-else>
              <div v-if="!status.rateLimit.isEnforced" class="d-flex align-center ga-2 mb-4">
                <v-icon color="info">mdi-information</v-icon>
                <span class="text-medium-emphasis">Rate limiting is not enforced for this key.</span>
              </div>

              <div class="mb-4">
                <div class="d-flex justify-space-between mb-1">
                  <span class="text-body-2 font-weight-medium">Hourly</span>
                  <span class="text-body-2">
                    {{ status.rateLimit.hourly.used }} / {{ status.rateLimit.hourly.limit }}
                    &nbsp;·&nbsp;
                    resets {{ formatResets(status.rateLimit.hourly.resetsInSeconds) }}
                  </span>
                </div>
                <v-progress-linear
                  :model-value="ratePct(status.rateLimit.hourly)"
                  :color="rateColor(status.rateLimit.hourly)"
                  height="8"
                  rounded
                />
              </div>

              <div>
                <div class="d-flex justify-space-between mb-1">
                  <span class="text-body-2 font-weight-medium">Monthly</span>
                  <span class="text-body-2">
                    {{ status.rateLimit.monthly.used }} / {{ status.rateLimit.monthly.limit }}
                    &nbsp;·&nbsp;
                    resets {{ formatResets(status.rateLimit.monthly.resetsInSeconds) }}
                  </span>
                </div>
                <v-progress-linear
                  :model-value="ratePct(status.rateLimit.monthly)"
                  :color="rateColor(status.rateLimit.monthly)"
                  height="8"
                  rounded
                />
              </div>
            </template>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <!-- Debug results dialog -->
    <v-dialog v-model="debugDialog" max-width="960" scrollable>
      <v-card>
        <v-card-title class="d-flex align-center ga-2 pt-4">
          <v-icon>mdi-bug</v-icon>
          Indexer Row Match — Debug Results
          <v-spacer />
          <v-btn icon="mdi-close" variant="text" size="small" @click="debugDialog = false" />
        </v-card-title>
        <v-card-subtitle class="pb-2">
          Search: <strong>{{ debugSearch }}</strong> — {{ debugResult?.rowsChecked ?? 0 }} row(s) checked
        </v-card-subtitle>
        <v-card-text>
          <div v-if="!debugResult?.rows.length" class="text-medium-emphasis">
            No indexer rows matched the search string.
          </div>
          <v-table v-else density="compact">
            <thead>
              <tr>
                <th>Indexer</th>
                <th>NZB Title</th>
                <th>Status</th>
                <th>Details</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="entry in debugResult.rows" :key="entry.rowId">
                <td class="text-body-2 py-2 text-no-wrap">{{ entry.indexerTitle }}</td>
                <td class="text-body-2 py-2" style="word-break: break-all">{{ entry.title }}</td>
                <td class="py-2">
                  <v-chip size="small" :color="debugStatusColor(entry.matchStatus)">
                    {{ entry.matchStatus }}
                  </v-chip>
                </td>
                <td class="text-body-2 py-2">
                  <template v-if="entry.matchedVideoTitle">
                    {{ entry.matchedVideoTitle }}
                    <span v-if="entry.candidatePreNames.length" class="text-medium-emphasis">
                      ({{ entry.candidatePreNames[0] }})
                    </span>
                  </template>
                  <template v-else-if="entry.candidatePreNames.length">
                    {{ entry.candidatePreNames.join(' · ') }}
                  </template>
                  <template v-else>—</template>
                </td>
              </tr>
            </tbody>
          </v-table>
        </v-card-text>
        <v-card-actions class="justify-end pb-4 pr-4">
          <v-btn variant="tonal" @click="debugDialog = false">Close</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { api, type PrdbStatus, type IndexerRowMatchDebugResult, type HealthResponse } from '../../api'
import { usePageAction } from '../../composables/usePageAction'

const status                   = ref<PrdbStatus | null>(null)
const loading                  = ref(false)
const infoDialog               = ref(false)
const runningSyncAll           = ref(false)
const runningBackfill          = ref(false)
const runningVideoDetailSync   = ref(false)
const runningPreNameSync       = ref(false)
const resettingPreNameCursor   = ref(false)
const resettingWantedVideoCursor = ref(false)
const resettingFavoriteSiteCursor = ref(false)
const resettingFavoriteActorCursor = ref(false)
const runningWantedVideoSync        = ref(false)
const runningFavoritesWantedSync    = ref(false)
const runningIndexerBackfillId = ref<string | null>(null)
const runningIndexerRowMatch   = ref(false)
const runningWantedFulfillment = ref(false)
const runningDownloadedFromIndexerSync = ref(false)
const runningFilehashSync              = ref(false)
const resettingFilehashCursor          = ref(false)
const runningIndexerFilehashSync       = ref(false)
const resettingIndexerFilehashCursor   = ref(false)
const runningDebug             = ref(false)
const debugDialog              = ref(false)
const debugSearch              = ref('')
const debugResult              = ref<IndexerRowMatchDebugResult | null>(null)
const error                    = ref<string | null>(null)

const health      = ref<HealthResponse | null>(null)
const healthLoading = ref(false)
const healthError = ref<string | null>(null)

async function fetchHealth() {
  healthLoading.value = true
  healthError.value = null
  try {
    health.value = await api.health.get()
  } catch (e) {
    healthError.value = e instanceof Error ? e.message : 'Failed to fetch API health'
  } finally {
    healthLoading.value = false
  }
}

// ── Actor summary backfill ─────────────────────────────────────────────────

const backfillPercent = computed(() => {
  const bf = status.value?.actorBackfill
  if (!bf || bf.isComplete) return 100
  if (!bf.totalActors || !bf.currentPage) return 0
  return Math.min(((bf.currentPage - 1) * 500) / bf.totalActors * 100, 100)
})

const backfillProgressLabel = computed(() => {
  const bf = status.value?.actorBackfill
  if (!bf) return ''
  if (bf.totalActors) {
    const fetched = ((bf.currentPage ?? 1) - 1) * 500
    return `~${fetched.toLocaleString()} / ${bf.totalActors.toLocaleString()} (${backfillPercent.value.toFixed(1)}%)`
  }
  return `Page ${bf.currentPage}`
})

// ── Actor detail sync ──────────────────────────────────────────────────────

const actorDetailPercent = computed(() => {
  const s = status.value?.actorDetailSync
  if (!s || s.totalActors === 0) return 0
  return (s.actorsWithDetail / s.totalActors) * 100
})

const actorDetailProgressLabel = computed(() => {
  const s = status.value?.actorDetailSync
  if (!s) return ''
  return `${s.actorsWithDetail.toLocaleString()} / ${s.totalActors.toLocaleString()} (${actorDetailPercent.value.toFixed(1)}%)`
})

// ── Video detail sync ──────────────────────────────────────────────────────

const videoDetailPercent = computed(() => {
  const s = status.value?.videoDetailSync
  if (!s || s.totalVideos === 0) return 0
  return (s.videosWithDetail / s.totalVideos) * 100
})

const videoDetailProgressLabel = computed(() => {
  const s = status.value?.videoDetailSync
  if (!s) return ''
  return `${s.videosWithDetail.toLocaleString()} / ${s.totalVideos.toLocaleString()} (${videoDetailPercent.value.toFixed(1)}%)`
})

// ── Rate limits ────────────────────────────────────────────────────────────

function ratePct(w: { used: number; limit: number }) {
  return w.limit > 0 ? (w.used / w.limit) * 100 : 0
}

function rateColor(w: { used: number; limit: number }) {
  const pct = ratePct(w)
  if (pct >= 90) return 'error'
  if (pct >= 70) return 'warning'
  return 'success'
}

function formatResets(seconds: number) {
  if (seconds < 60) return `${seconds}s`
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  if (h > 0) return `in ${h}h ${m}m`
  return `in ${m}m`
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString()
}

// ── Actions ────────────────────────────────────────────────────────────────

async function runSyncAll() {
  runningSyncAll.value = true
  error.value = null
  try {
    await api.prdbSync.syncAll()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    runningSyncAll.value = false
  }
}

async function runBackfill() {
  runningBackfill.value = true
  error.value = null
  try {
    await api.prdbStatus.runBackfill()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    runningBackfill.value = false
  }
}

async function runVideoDetailSync() {
  runningVideoDetailSync.value = true
  error.value = null
  try {
    await api.prdbStatus.runVideoDetailSync()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    runningVideoDetailSync.value = false
  }
}

async function resetPreNameCursor() {
  resettingPreNameCursor.value = true
  error.value = null
  try {
    await api.prdbStatus.resetPreNameCursor()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    resettingPreNameCursor.value = false
  }
}

async function resetWantedVideoCursor() {
  resettingWantedVideoCursor.value = true
  error.value = null
  try {
    await api.prdbStatus.resetWantedVideoCursor()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    resettingWantedVideoCursor.value = false
  }
}

async function resetFilehashCursor() {
  resettingFilehashCursor.value = true
  error.value = null
  try {
    await api.prdbStatus.resetFilehashCursor()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    resettingFilehashCursor.value = false
  }
}

async function resetIndexerFilehashCursor() {
  resettingIndexerFilehashCursor.value = true
  error.value = null
  try {
    await api.prdbStatus.resetIndexerFilehashCursor()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    resettingIndexerFilehashCursor.value = false
  }
}

async function resetFavoriteCursors() {
  resettingFavoriteSiteCursor.value = true
  resettingFavoriteActorCursor.value = true
  error.value = null
  try {
    await api.prdbStatus.resetFavoriteSiteCursor()
    await api.prdbStatus.resetFavoriteActorCursor()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    resettingFavoriteSiteCursor.value = false
    resettingFavoriteActorCursor.value = false
  }
}

async function runPreNameSync() {
  runningPreNameSync.value = true
  error.value = null
  try {
    await api.prdbStatus.runPreNameSync()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    runningPreNameSync.value = false
  }
}

async function runWantedVideoSync() {
  runningWantedVideoSync.value = true
  error.value = null
  try {
    await api.prdbStatus.runWantedVideoSync()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    runningWantedVideoSync.value = false
  }
}

async function runFavoritesWantedSync() {
  runningFavoritesWantedSync.value = true
  error.value = null
  try {
    await api.prdbStatus.runFavoritesWantedSync()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    runningFavoritesWantedSync.value = false
  }
}

async function runIndexerBackfill(id: string) {
  runningIndexerBackfillId.value = id
  error.value = null
  try {
    await api.prdbStatus.runIndexerBackfill(id)
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    runningIndexerBackfillId.value = null
  }
}

function debugStatusColor(status: string) {
  switch (status) {
    case 'Matched':        return 'success'
    case 'AlreadyMatched': return 'info'
    case 'MultipleMatches': return 'warning'
    default:               return undefined
  }
}

async function runDebug() {
  const search = window.prompt('Enter search string (words separated by spaces — all must appear in the title):')
  if (!search?.trim()) return

  debugSearch.value = search.trim()
  runningDebug.value = true
  error.value = null
  try {
    debugResult.value = await api.prdbStatus.debugIndexerRowMatch(debugSearch.value)
    debugDialog.value = true
  } catch (e: any) {
    error.value = e.message
  } finally {
    runningDebug.value = false
  }
}

async function runWantedFulfillment() {
  runningWantedFulfillment.value = true
  error.value = null
  try {
    await api.prdbStatus.runWantedFulfillment()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    runningWantedFulfillment.value = false
  }
}

async function runFilehashSync() {
  runningFilehashSync.value = true
  error.value = null
  try {
    await api.prdbStatus.runFilehashSync()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    runningFilehashSync.value = false
  }
}

async function runIndexerFilehashSync() {
  runningIndexerFilehashSync.value = true
  error.value = null
  try {
    await api.prdbStatus.runIndexerFilehashSync()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    runningIndexerFilehashSync.value = false
  }
}

async function runDownloadedFromIndexerSync() {
  runningDownloadedFromIndexerSync.value = true
  error.value = null
  try {
    await api.prdbStatus.runDownloadedFromIndexerSync()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    runningDownloadedFromIndexerSync.value = false
  }
}

async function runIndexerRowMatch() {
  runningIndexerRowMatch.value = true
  error.value = null
  try {
    await api.prdbStatus.runIndexerRowMatch()
    await load()
  } catch (e: any) {
    error.value = e.message
  } finally {
    runningIndexerRowMatch.value = false
  }
}

async function load() {
  loading.value = true
  setActionLoading(true)
  error.value = null
  try {
    status.value = await api.prdbStatus.get()
  } catch (e: any) {
    error.value = e.message
  } finally {
    loading.value = false
    setActionLoading(false)
  }
}

const { setActions, clearAction, setActionLoading } = usePageAction()

onMounted(() => {
  load()
  fetchHealth()
  setActions()
})

onUnmounted(clearAction)
</script>
