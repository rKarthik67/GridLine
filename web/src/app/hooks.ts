import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { Lookups, PresenceRow } from '../api/types'
import { usePresence } from '../components/Presence'
import { useAuth } from './auth'
import { tabId } from '../api/client'

export const useLookups = () => useQuery({ queryKey: ['lookups'], queryFn: () => api.get<Lookups>('/lookups'), staleTime: 10 * 60_000 })

/** Query key that ProjectLayout's live-change handler refreshes when anyone changes this version. */
export const vkey = (name: string, versionId: number, ...rest: (string | number | undefined)[]) => [name, `v${versionId}`, ...rest.filter(x => x !== undefined)]

/** Who else is focused on something of this kind right now (for outlining cells others are editing). */
export function useOthersFocus(kind: string): Map<string, PresenceRow> {
  const rows = usePresence(); const { me } = useAuth()
  const m = new Map<string, PresenceRow>()
  for (const r of rows) if (r.focusKind === kind && r.focusKey && !(r.userId === me?.userId && r.connectionId.endsWith(tabId))) m.set(r.focusKey, r)
  return m
}
export function useRefresh() {
  const qc = useQueryClient()
  return (versionId: number) => qc.invalidateQueries({ predicate: q => JSON.stringify(q.queryKey).includes(`"v${versionId}"`) })
}
