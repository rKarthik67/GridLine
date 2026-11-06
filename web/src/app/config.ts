// Public settings the sign-in pages and the shell need: whether sign-up is open, where to get help, legal links.
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'

export interface PublicConfig { signupEnabled: boolean; supportEmail: string | null; termsUrl: string | null; privacyUrl: string | null; version: string | null }
export const useConfig = () => useQuery({ queryKey: ['config'], queryFn: () => api.get<PublicConfig>('/config'), staleTime: 60 * 60_000, retry: 1 })
