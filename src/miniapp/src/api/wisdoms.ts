import apiClient from './client'
import type { WisdomResponse } from '../types'

export async function getRandomWisdom(): Promise<WisdomResponse> {
  const { data } = await apiClient.get('/wisdoms/random')
  return data
}
