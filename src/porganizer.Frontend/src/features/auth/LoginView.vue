<template>
  <v-container class="fill-height" fluid>
    <v-row align="center" justify="center">
      <v-col cols="12" sm="6" md="4">
        <v-card>
          <v-card-title class="text-h6 pa-6 pb-2">Porganizer — Sign In</v-card-title>
          <v-card-text>
            <v-form @submit.prevent="submit">
              <v-text-field
                v-model="username"
                label="Username"
                autocomplete="username"
                autofocus
                variant="outlined"
                class="mb-2"
              />
              <v-text-field
                v-model="password"
                label="Password"
                type="password"
                autocomplete="current-password"
                variant="outlined"
              />
              <v-alert
                v-if="error"
                type="error"
                variant="tonal"
                class="mb-4"
              >
                Invalid username or password.
              </v-alert>
              <v-btn
                type="submit"
                color="primary"
                block
                :loading="loading"
              >
                Sign In
              </v-btn>
            </v-form>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </v-container>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../../api'
import { useAuth } from '../../composables/useAuth'

const router = useRouter()
const { invalidate } = useAuth()

const username = ref('')
const password = ref('')
const loading = ref(false)
const error = ref(false)

async function submit() {
  error.value = false
  loading.value = true
  try {
    await api.auth.login(username.value, password.value)
    invalidate()
    router.push('/')
  } catch {
    error.value = true
  } finally {
    loading.value = false
  }
}
</script>
