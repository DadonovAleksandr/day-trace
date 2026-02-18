import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      redirect: '/today',
    },
    {
      path: '/today',
      name: 'today',
      component: () => import('../views/TodayView.vue'),
      meta: { title: 'Сегодня', icon: '📝' },
    },
    {
      path: '/week',
      name: 'week',
      component: () => import('../views/WeekView.vue'),
      meta: { title: 'Неделя', icon: '📅' },
    },
    {
      path: '/month',
      name: 'month',
      component: () => import('../views/MonthView.vue'),
      meta: { title: 'Месяц', icon: '📆' },
    },
    {
      path: '/year',
      name: 'year',
      component: () => import('../views/YearView.vue'),
      meta: { title: 'Год', icon: '📊' },
    },
    {
      path: '/settings',
      name: 'settings',
      component: () => import('../views/SettingsView.vue'),
      meta: { title: 'Настройки', icon: '⚙️' },
    },
  ],
})

export default router
