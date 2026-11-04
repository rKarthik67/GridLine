import { ICONS } from './iconPaths'
export function Icon({ name, size }: { name: string; size?: 's' | 'l' }) {
  return <svg className={`i ${size ?? ''}`} viewBox="0 0 24 24" aria-hidden="true" dangerouslySetInnerHTML={{ __html: ICONS[name] ?? ICONS.grid }} />
}
