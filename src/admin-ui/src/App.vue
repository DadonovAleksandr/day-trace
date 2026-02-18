<template>
  <div v-if="!authStore.isAuthenticated">
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
        <router-link v-if="authStore.isAdmin" to="/audit">📋 Audit</router-link>
      </nav>
      <div class="user-info">
        <div class="email">{{ authStore.email }}</div>
        <div><span class="badge badge-info">{{ authStore.role }}</span></div>
        <button class="logout-btn" @click="handleLogout">Logout</button>
      </div>
    </aside>
    <main class="main-content">
      <router-view />
    </main>
  </div>
</template>

<script setup lang="ts">
import { useAuthStore } from './stores/auth'
import { useRouter } from 'vue-router'

const authStore = useAuthStore()
const router = useRouter()

function handleLogout() {
  authStore.logout()
  router.push('/login')
}
</script>
