import { createContext, useCallback, useContext, useState, type ReactNode } from 'react'
import { Icon } from '../components/Icon'
const Ctx = createContext<(msg: string, icon?: string) => void>(() => {})
export const useToast = () => useContext(Ctx)
export function ToastProvider({ children }: { children: ReactNode }) {
  const [t, setT] = useState<{ msg: string; icon: string; id: number } | null>(null)
  const show = useCallback((msg: string, icon = 'check') => { const id = Date.now(); setT({ msg, icon, id }); setTimeout(() => setT(x => x?.id === id ? null : x), 2800) }, [])
  return <Ctx.Provider value={show}>{children}{t && <div className="toast" role="status"><Icon name={t.icon} />{t.msg}</div>}</Ctx.Provider>
}
