// The two-panel layout behind sign-in, sign-up, invitations and password resets: a dark story panel with the
// product pitch on the left, the form on the right.
import type { ReactNode } from 'react'
import { Link } from 'react-router'

export function AuthShell({ title, lead, children, footer }: { title: string; lead?: ReactNode; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="signin">
      <div className="art">
        <div>
          <Link to="/signin" className="brand" style={{ padding: 0, textDecoration: 'none' }}><span className="mark">G</span><b>Gridline<i>.</i></b></Link>
          <div className="art-eyebrow">Preconstruction workspace</div>
          <h1>Level every bid.<br />Price every <i>project.</i></h1>
          <p className="art-lead">Bid leveling, scope matching, crew and precon planning and the price roll-up, in one place your whole precon team works in together, live.</p>
        </div>
        <ul className="art-points">
          <li><b>Bid board</b> every trade with all its bids side by side, coverage and spread at a glance.</li>
          <li><b>Scope match</b> answer every scope line per bidder, paste from Excel, read proposals with AI.</li>
          <li><b>Price roll-up</b> reserves, bond, insurance, fee and tax, confirmed by the server on every change.</li>
          <li><b>Your company's data stays your company's</b> each customer is a separate company with its own people, partners and templates.</li>
        </ul>
        <div className="art-foot">Clarity before construction.</div>
      </div>
      <div className="frm">
        <div className="auth-card">
          <h2>{title}</h2>
          {lead && <p className="muted auth-lead">{lead}</p>}
          {children}
          {footer && <div className="auth-foot">{footer}</div>}
        </div>
      </div>
    </div>
  )
}

export function AuthError({ error }: { error: string | null }) {
  return error ? <div className="auth-err" role="alert">{error}</div> : null
}
