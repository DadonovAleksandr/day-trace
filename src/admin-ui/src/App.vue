<template>
  <div v-if="authStore.sessionStatus === 'unknown'" class="login-page">
    <div class="login-card">
      <p class="loading">Restoring session...</p>
    </div>
  </div>
  <div v-else-if="!authStore.isAuthenticated">
    <router-view />
  </div>
  <div v-else class="app-layout">
    <aside class="sidebar">
      <h2>DayTrace Admin</h2>
      <nav>
        <router-link to="/dashboard">📊 Dashboard</router-link>
        <router-link v-if="authStore.isOperator" to="/users">👥 Users</router-link>
        <router-link v-if="authStore.isOperator" to="/content">📝 Content</router-link>
        <router-link v-if="authStore.isOperator" to="/operations">⚙️ Operations</router-link>
        <router-link v-if="authStore.isAdmin" to="/subscriptions">💳 Subscriptions</router-link>
        <router-link v-if="authStore.isAdmin" to="/audit">📋 Audit</router-link>
      </nav>
      <div class="user-info">
        <div class="email">{{ authStore.email }}</div>
        <div><span class="badge badge-info">{{ authStore.role }}</span></div>
        <button class="logout-btn" :disabled="loggingOut" @click="handleLogout">
          {{ loggingOut ? 'Logging out...' : 'Logout' }}
        </button>
      </div>
    </aside>
    <main class="main-content">
      <router-view />
    </main>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useAuthStore } from './stores/auth'
import { useRouter } from 'vue-router'
import { logout as apiLogout } from './api/admin'

const authStore = useAuthStore()
const router = useRouter()
const loggingOut = ref(false)

onMounted(() => {
  if (authStore.sessionStatus === 'unknown') {
    void authStore.restoreSession()
  }
})

async function handleLogout() {
  if (loggingOut.value) return

  loggingOut.value = true
  try {
    await apiLogout()
  } catch {
    // Local session must be cleared even if backend session already expired.
  } finally {
    authStore.logout()
    loggingOut.value = false
    if (router.currentRoute.value.path !== '/login') {
      await router.push('/login')
    }
  }
}
</script>
