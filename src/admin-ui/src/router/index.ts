import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'Login',
      component: () => import('../views/LoginView.vue'),
      meta: { public: true },
    },
    {
      path: '/',
      redirect: '/dashboard',
    },
    {
      path: '/dashboard',
      name: 'Dashboard',
      component: () => import('../views/DashboardView.vue'),
      meta: { minRole: 'analyst' },
    },
    {
      path: '/users',
      name: 'Users',
      component: () => import('../views/UsersView.vue'),
      meta: { minRole: 'operator' },
    },
    {
      path: '/users/:id',
      name: 'UserDetail',
      component: () => import('../views/UserDetailView.vue'),
      meta: { minRole: 'operator' },
    },
    {
      path: '/content',
      name: 'Content',
      component: () => import('../views/ContentView.vue'),
      meta: { minRole: 'operator' },
    },
    {
      path: '/operations',
      name: 'Operations',
      component: () => import('../views/OperationsView.vue'),
      meta: { minRole: 'operator' },
    },
    {
      path: '/audit',
      name: 'Audit',
      component: () => import('../views/AuditView.vue'),
      meta: { minRole: 'admin' },
    },
  ],
})

const roleLevel: Record<string, number> = {
  analyst: 1,
  operator: 2,
  admin: 3,
}

router.beforeEach(async (to) => {
  const authStore = useAuthStore()

  if (authStore.sessionStatus === 'unknown') {
    await authStore.restoreSession()
  }

  if (to.meta.public) {
    if (to.path === '/login' && authStore.isAuthenticated) return '/dashboard'
    return true
  }
  if (!authStore.isAuthenticated) return '/login'

  const minRole = to.meta.minRole as string | undefined
  if (minRole) {
    const userLevel = roleLevel[authStore.role] || 0
    const requiredLevel = roleLevel[minRole] || 0
    if (userLevel < requiredLevel) return '/dashboard'
  }

  return true
})

export default router
