import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, tokenStore } from '../api/client'
import type { Me } from '../api/types'

interface AuthState {
  me: Me | null; loading: boolean
  signIn(email: string, password: string, rememberMe?: boolean): Promise<void>
  /** sign-up and invitation acceptance already hold a token: start the session with it */
  setSession(token: string, me: Me): void
  signOut(): Promise<void>
  refresh(): Promise<void>
  can(cap: string): boolean
  isAdmin: boolean
}
const Ctx = createContext<AuthState>(null!)
export const useAuth = () => useContext(Ctx)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null)
  const [loading, setLoading] = useState(true)
  useEffect(() => {
    if (!tokenStore.get()) { setLoading(false); return }
    api.get<Me>('/me').then(setMe).catch(() => tokenStore.set(null)).finally(() => setLoading(false))
    // Sign-out in one tab signs every tab out; sign-in in one tab signs every tab in.
    const onStorage = (e: StorageEvent) => { if (e.key === 'gridline.token') { if (!e.newValue) setMe(null); else api.get<Me>('/me').then(setMe).catch(() => {}) } }
    window.addEventListener('storage', onStorage); return () => window.removeEventListener('storage', onStorage)
  }, [])
  const value: AuthState = {
    me, loading,
    async signIn(email, password, rememberMe) {
      const r = await api.post<{ accessToken: string; me: Me }>('/auth/login', { email, password, rememberMe: !!rememberMe })
      tokenStore.set(r.accessToken); setMe(r.me)
    },
    setSession(token, m) { tokenStore.set(token); setMe(m) },
    async signOut() { await api.post('/auth/logout').catch(() => {}); tokenStore.set(null); setMe(null) },
    async refresh() { try { setMe(await api.get<Me>('/me')) } catch { /* keep what we have */ } },
    can(cap) { return !!me && (me.capabilities.includes('*') || me.capabilities.includes(cap)) },
    isAdmin: !!me && me.capabilities.includes('*'),
  }
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}
