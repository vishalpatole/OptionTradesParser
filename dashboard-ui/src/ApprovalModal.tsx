import { useState } from 'react'
import { AlertTriangle, CheckCircle2, Clock3, ShieldCheck, XCircle } from 'lucide-react'
import { approveTrade, rejectTrade, type PendingApproval } from './api'

const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

export function ApprovalModal({ approval, queuedCount, onDecided }: { approval: PendingApproval; queuedCount: number; onDecided: () => void }) {
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const optionCode = approval.optionType === 'CALL' ? 'C' : 'P'

  async function decide(action: 'approve' | 'reject', budget: number | null = null) {
    setSubmitting(true)
    setError('')
    try {
      if (action === 'approve') await approveTrade(approval.id, budget)
      else await rejectTrade(approval.id)
      onDecided()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The approval could not be submitted.')
      onDecided()
    } finally {
      setSubmitting(false)
    }
  }

  return <div className="approval-backdrop" role="presentation">
    <section className="approval-modal" role="alertdialog" aria-modal="true" aria-labelledby="approval-title">
      <header className="approval-modal__header">
        <div className="approval-icon"><ShieldCheck size={22} /></div>
        <div><span className="eyebrow">PRE-TRADE CONFIRMATION</span><h2 id="approval-title">{approval.orderAction} {approval.ticker} ${approval.strike} {optionCode}</h2><p>{approval.trader} · {approval.intent}</p></div>
        <div className={`account-badge account-badge--${approval.accountType.toLowerCase()}`}>{approval.accountType}</div>
      </header>

      <div className="approval-summary">
        <ApprovalFact label="CONTRACT" value={approval.contractSymbol} wide />
        <ApprovalFact label="ORDER" value={`${approval.orderAction} @ ${approval.orderType} ${money.format(approval.limitPrice)}`} />
        <ApprovalFact label="MARKET" value={approval.marketQuote} />
        <ApprovalFact label="EXPIRY" value={approval.expiry} />
        <ApprovalFact label="RISK" value={approval.riskCategory} />
      </div>

      {(approval.warnings.length > 0 || approval.contractInferred) && <div className="approval-warnings">
        {approval.warnings.map((warning) => <p key={warning}><AlertTriangle size={15} />{warning}</p>)}
      </div>}

      <div className="approval-actions">
        {approval.budgetOptions.length > 0 ? approval.budgetOptions.map((option) => <button className="budget-choice" disabled={submitting} key={option.budget} onClick={() => void decide('approve', option.budget)}>
          <span>EXECUTE BUDGET</span><strong>{money.format(option.budget)}</strong><small>{option.quantity} contract{option.quantity === 1 ? '' : 's'} · est. {money.format(option.estimatedValue)}</small>
        </button>) : <button className="budget-choice budget-choice--single" disabled={submitting} onClick={() => void decide('approve')}>
          <CheckCircle2 size={18} /><strong>Execute {approval.orderAction} ×{approval.quantity}</strong><small>Limit {money.format(approval.limitPrice)}</small>
        </button>}
      </div>

      {error && <div className="approval-error"><AlertTriangle size={15} />{error}</div>}

      <footer className="approval-modal__footer">
        <span><Clock3 size={14} /> Respond now or the desktop confirmation will open</span>
        {queuedCount > 1 && <span>{queuedCount - 1} more queued</span>}
        <button disabled={submitting} onClick={() => void decide('reject')}><XCircle size={16} /> Reject order</button>
      </footer>
    </section>
  </div>
}

function ApprovalFact({ label, value, wide = false }: { label: string; value: string; wide?: boolean }) {
  return <div className={`approval-fact${wide ? ' is-wide' : ''}`}><span>{label}</span><strong>{value}</strong></div>
}
