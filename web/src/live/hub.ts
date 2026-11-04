// One SignalR connection per browser tab. The tab joins the project/version/page in its address,
// so two tabs on two projects show up as two presence rows and get only their own project's changes.
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { tabId, tokenStore } from '../api/client'
import type { PresenceRow } from '../api/types'
import { debugLog } from '../api/debug'

export interface ChangeEvent { eventId: number; verb: string; subjectType: string; subjectKey: string; userId: number; personName: string; versionId: number | null }
type Listener<T> = (v: T) => void
let conn: HubConnection | null = null
let joined: { projectId: number; versionId: number; page: string } | null = null
const presenceL = new Set<Listener<PresenceRow[]>>(), changeL = new Set<Listener<ChangeEvent>>()

export async function connect() {
  if (conn && conn.state !== HubConnectionState.Disconnected) return conn
  conn = new HubConnectionBuilder().withUrl(`/hubs/project?tab=${encodeURIComponent(tabId)}`, { accessTokenFactory: () => tokenStore.get() ?? '' })
    .withAutomaticReconnect([0, 1000, 3000, 5000, 10000]).configureLogging(LogLevel.Warning).build()
  conn.on('presence', (rows: PresenceRow[]) => { debugLog.live('←', `presence (${rows.length} tabs)`, rows); presenceL.forEach(f => f(rows)) })
  conn.on('changed', (e: ChangeEvent) => { debugLog.live('←', `changed ${e.verb} by ${e.personName}`, e); changeL.forEach(f => f(e)) })
  conn.onreconnecting(err => debugLog.live('←', 'reconnecting…', err))
  conn.onclose(err => debugLog.live('←', 'connection closed', err))
  conn.onreconnected(() => { if (joined) conn!.invoke('Join', joined.projectId, joined.versionId, joined.page).catch(() => {}) })
  await conn.start()
  debugLog.live('→', `connected · tab ${tabId}`)
  return conn
}
export async function join(projectId: number, versionId: number, page: string) {
  joined = { projectId, versionId, page }
  debugLog.live('→', 'Join', { projectId, versionId, page })
  try { const c = await connect(); await c.invoke('Join', projectId, versionId, page) } catch (e) { debugLog.live('←', 'Join failed (live updates off; the page still works)', e) }
}
export async function focus(kind: string | null, key: string | null, activity: 'viewing' | 'editing' | 'typing' = 'viewing') {
  debugLog.live('→', 'Focus', { kind, key, activity })
  try { if (conn?.state === HubConnectionState.Connected) await conn.invoke('Focus', kind, key, activity) } catch (e) { debugLog.live('←', 'Focus failed', e) }
}
export async function leave() { debugLog.live('→', 'Leave'); joined = null; try { if (conn?.state === HubConnectionState.Connected) await conn.invoke('Leave') } catch { /* ignore */ } }
export const onPresence = (f: Listener<PresenceRow[]>) => { presenceL.add(f); return () => { presenceL.delete(f) } }
export const onChange = (f: Listener<ChangeEvent>) => { changeL.add(f); return () => { changeL.delete(f) } }
