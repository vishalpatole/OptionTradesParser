import { AlertTriangle, ArrowUpRight, Clock3 } from 'lucide-react'
import type { OrderSummary, TradeSummary } from './api'

const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })
const time = new Intl.DateTimeFormat('en-US', { hour: 'numeric', minute: '2-digit', timeZone: 'America/Los_Angeles', timeZoneName: 'short' })

function optionCode(type: string) {
  return type === 'CALL' ? 'C' : 'P'
}

function expiration(value: string) {
  if (value.length !== 8) return value
  return `${value.slice(4, 6)}/${value.slice(6, 8)}/${value.slice(2, 4)}`
}

function roleLabel(order: OrderSummary) {
  if (order.role === 'ENTRY') return 'ENTRY'
  if (order.role.startsWith('TP')) return order.role
  return order.role.replaceAll('_', ' ')
}

function orderValue(order: OrderSummary) {
  const state = order.status === 'FILLED' ? 'FILLED' : order.status === 'SUBMITTED' || order.status === 'PRESUBMITTED' ? 'WORKING' : order.status
  return `${money.format(order.limitPrice)} ×${order.quantity} · ${state}\n${time.format(new Date(order.submittedAt))}`
}

export function TradeCard({ trade }: { trade: TradeSummary }) {
  const entry = trade.orders.find((order) => order.role === 'ENTRY')
  const targets = trade.orders.filter((order) => order.role.startsWith('TP'))
  const buys = trade.executions.filter((execution) => execution.action === 'BUY')
  const sells = trade.executions.filter((execution) => execution.action === 'SELL')
  const manualExitOrders = trade.orders.filter((order) => order.role === 'MANUAL_EXIT')
  const bought = buys.reduce((sum, execution) => sum + execution.quantity, 0)
  const sold = sells.reduce((sum, execution) => sum + execution.quantity, 0)
  const avgEntry = bought ? buys.reduce((sum, execution) => sum + execution.quantity * execution.price, 0) / bought : null
  const avgExit = sold ? sells.reduce((sum, execution) => sum + execution.quantity * execution.price, 0) / sold : null
  const brokerRealized = trade.executions.reduce((sum, execution) => sum + (execution.realizedPnl ?? 0), 0)
  const estimatedPnl = avgEntry !== null && avgExit !== null ? (avgExit - avgEntry) * sold * 100 : null
  const returnPercent = avgEntry && avgExit ? ((avgExit - avgEntry) / avgEntry) * 100 : null
  const isLoss = (brokerRealized || estimatedPnl || 0) < 0
  const isClosedLoss = trade.status === 'CLOSED' && isLoss
  const hasManualExitWithoutFill = manualExitOrders.length > 0 && sold === 0
  const resultText = brokerRealized
    ? `${money.format(brokerRealized)} broker realized P&L`
    : estimatedPnl !== null
      ? `${money.format(estimatedPnl)} estimated P&L from fills`
      : hasManualExitWithoutFill
        ? 'Closed in TWS; exit price not available'
        : 'Awaiting verified exit fills'

  return (
    <article className="trade-card">
      <header className="trade-card__header">
        <div className="symbol-mark">{trade.ticker.slice(0, 3)}</div>
        <div>
          <div className="eyebrow">{trade.trader || 'UNKNOWN'} · {time.format(new Date(trade.alertedAt))}</div>
          <h3>{trade.ticker} <span>${trade.strike} {optionCode(trade.optionType)}</span></h3>
          <p>Exp {expiration(trade.expiration)} · {entry?.quantity ?? 0} contracts</p>
        </div>
        <div className={`status status--${trade.status.toLowerCase()}${isClosedLoss ? ' is-loss' : ''}`}>{trade.status}</div>
      </header>

      <div className="trade-card__metrics">
        <div className={`primary-result${isLoss ? ' is-loss' : ''}`}>
          <span>RETURN ON PREMIUM</span>
          <strong>{returnPercent === null ? '—' : `${returnPercent >= 0 ? '+' : ''}${returnPercent.toFixed(1)}%`}</strong>
          <small className="result-pnl">{resultText}</small>
        </div>
        <Metric label="ALERT" value={money.format(trade.alertPrice)} />
        <Metric label="AVG ENTRY" value={avgEntry === null ? '—' : money.format(avgEntry)} />
        <Metric label="AVG EXIT" value={avgExit === null ? '—' : money.format(avgExit)} />
        <Metric label="FILLS" value={`${trade.executions.length}`} />
      </div>

      <div className="timeline">
        <div className="timeline__line" />
        <TimelineNode label="ALERT" value={`${money.format(trade.alertPrice)}\n${time.format(new Date(trade.alertedAt))}`} active />
        {entry && <TimelineNode label="ENTRY ORDER" value={orderValue(entry)} active={entry.status === 'FILLED'} />}
        {targets.map((order) => <TimelineNode key={`${order.clientId}-${order.orderId}`} label={roleLabel(order)} value={orderValue(order)} active={order.status === 'FILLED'} warning={order.status === 'INACTIVE' || order.status === 'REJECTED'} />)}
        {manualExitOrders.map((order) => <TimelineNode key={`${order.clientId}-${order.orderId}`} label="MANUAL EXIT" value={order.limitPrice > 0 ? orderValue(order) : `${order.quantity} contract${order.quantity === 1 ? '' : 's'} · ${order.status}\n${time.format(new Date(order.submittedAt))}`} active={order.status === 'FILLED'} />)}
        {targets.length === 0 && <TimelineNode label="TARGETS" value="Not routed" warning />}
      </div>

      <footer className="trade-card__footer">
        <span><Clock3 size={14} /> {trade.executions.length ? 'Broker fills recorded' : 'Historical broker fills unavailable'}</span>
        {trade.status === 'ATTENTION' && <span className="warning"><AlertTriangle size={14} /> Review rejected order</span>}
        <span className="details-link">Trade #{trade.id} <ArrowUpRight size={14} /></span>
      </footer>
    </article>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return <div className="metric"><span>{label}</span><strong>{value}</strong></div>
}

function TimelineNode({ label, value, active = false, warning = false }: { label: string; value: string; active?: boolean; warning?: boolean }) {
  return <div className={`timeline__node${active ? ' is-active' : ''}${warning ? ' is-warning' : ''}`}><i /><span>{label}</span><strong>{value}</strong></div>
}
