// The scratch pad in its own window (was: /scratchpad-popup). Stays in step with the trade's Scope match tab.
import { useParams, useSearchParams } from 'react-router'
import { ScratchPad } from '../components/ScratchPad'
import { ErrorBoundary } from '../components/ErrorBoundary'

export default function ScratchWindow() {
  const { vid = '0', tradeId = '0' } = useParams(); const [sp] = useSearchParams(); const title = sp.get('title') ?? ''
  if (title) document.title = `Scratch pad · ${title}`
  return <div className="scratch-win"><h2>Scratch pad{title && <small className="mut"> · {title}</small>}</h2>
    <ErrorBoundary where="the scratch pad"><ScratchPad vid={+vid} tradeId={+tradeId} canEdit popout={false} title={title} /></ErrorBoundary></div>
}
