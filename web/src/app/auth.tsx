import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, tokenStore } from '../api/client'
import type { Me } from '../api/types'

interface AuthState { me: Me | null; loading: boolean; signIn(email: string, password: string): Promise<void>; signOut(): Promise<void>; can(cap: string): boolean }
const Ctx = createContext<AuthState>(null!)
export const useAuth = () => useContext(Ctx)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null)
  const [loading, setLoading] = useState(true)
  useEffect(() => {
    if (!tokenStore.get()) { setLoading(false); return }
    api.get<Me>('/me').then(setMe).catch(() => tokenStore.set(null)).finally(() => setLoading(false))
    // Sign-out in one tab signs every tab out.
    const onStorage = (e: StorageEvent) => { if (e.key === 'gridline.token' && !e.newValue) setMe(null) }
    window.addEventListener('storage', onStorage); return () => window.removeEventListener('storage', onStorage)
  }, [])
  const value: AuthState = {
    me, loading,
    async signIn(email, password) {
      const r = await api.post<{ accessToken: string; me: Me }>('/auth/login', { email, password })
      tokenStore.set(r.accessToken); setMe(r.me)
    },
    async signOut() { await api.post('/auth/logout').catch(() => {}); tokenStore.set(null); setMe(null) },
    can(cap) { return !!me && (me.capabilities.includes('*') || me.capabilities.includes(cap)) },
  }
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}
