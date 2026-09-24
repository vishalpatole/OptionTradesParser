import { useEffect, useState } from 'react'
import { ChevronDown, ChevronRight, CircleDot, CornerDownRight, FileCheck2 } from 'lucide-react'
import type { OrderSummary, TradeSummary } from './api'
import { TradeCard } from './TradeCard'

const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })
const shortTime = new Intl.DateTimeFormat('en-US', { hour: 'numeric', minute: '2-digit', timeZone: 'America/Los_Angeles', timeZoneName: 'short' })

function estimatedPnl(trade: TradeSummary) {
  const buys = trade.executions.filter((execution) => execution.action === 'BUY')
  const sells = trade.executions.filter((execution) => execution.action === 'SELL')
  const bought = buys.reduce((sum, execution) => sum + execution.quantity, 0)
  const sold = sells.reduce((sum, execution) => sum + execution.quantity, 0)
  if (!bought || !sold) return null

  const avgEntry = buys.reduce((sum, execution) => sum + execution.quantity * execution.price, 0) / bought
  const avgExit = sells.reduce((sum, execution) => sum + execution.quantity * execution.price, 0) / sold
  return (avgExit - avgEntry) * Math.min(bought, sold) * 100
}

export function TradeLedger({ trades }: { trades: TradeSummary[] }) {
  const [selectedId, setSelectedId] = useState(trades[0]?.id ?? '')
  const [expandedIds, setExpandedIds] = useState<Set<string>>(() => new Set(trades[0] ? [trades[0].id] : []))
  const selectedTrade = trades.find((trade) => trade.id === selectedId) ?? trades[0]

  useEffect(() => {
    if (!selectedTrade && trades[0]) setSelectedId(trades[0].id)
  }, [selectedTrade, trades])

  if (!trades.length) return <div className="empty"><FileCheck2 size={26} /><strong>No trades recorded yet</strong><span>Captured Discord alerts will appear here.</span></div>

  function toggleExpanded(id: string) {
    setExpandedIds((current) => {
      const next = new Set(current)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  return <div className="ledger-workspace">
    <section className="ledger-pane" aria-label="Trade ledger">
      <header className="ledger-pane__header">
        <div><span className="eyebrow">TRADE TREE</span><strong>{trades.length} captured trades</strong></div>
        <span>ORDER / EXECUTION</span>
      </header>
      <div className="trade-tree">
        {trades.map((trade) => {
          const expanded = expandedIds.has(trade.id)
          const selected = selectedTrade?.id === trade.id
          const isClosedLoss = trade.status === 'CLOSED' && (estimatedPnl(trade) ?? 0) < 0
          return <div className={`tree-trade${selected ? ' is-selected' : ''}`} key={trade.id}>
            <div className="tree-trade__row">
              <button className="tree-toggle" title={expanded ? 'Collapse trade' : 'Expand trade'} onClick={() => toggleExpanded(trade.id)}>
                {expanded ? <ChevronDown size={15} /> : <ChevronRight size={15} />}
              </button>
              <button className="tree-select" onClick={() => setSelectedId(trade.id)}>
                <span className="tree-symbol">{trade.ticker.slice(0, 2)}</span>
                <span className="tree-contract"><strong>{trade.ticker} ${trade.strike} {trade.optionType === 'CALL' ? 'C' : 'P'}</strong><small>{trade.trader} · {shortTime.format(new Date(trade.alertedAt))}</small></span>
                <span className={`tree-status tree-status--${trade.status.toLowerCase()}${isClosedLoss ? ' is-loss' : ''}`}>{trade.status}</span>
              </button>
            </div>
            {expanded && <div className="tree-children">
              {trade.orders.map((order) => <OrderBranch key={`${order.clientId}-${order.orderId}`} trade={trade} order={order} />)}
              {!trade.orders.length && <div className="tree-empty">No broker orders routed</div>}
            </div>}
          </div>
        })}
      </div>
    </section>

    <section className="trade-preview" aria-label="Selected trade detail">
      <header className="trade-preview__header">
        <div><span className="eyebrow">SELECTED TRADE</span><strong>{selectedTrade.ticker} execution card</strong></div>
        <span>#{selectedTrade.id}</span>
      </header>
      <TradeCard trade={selectedTrade} />
      <div className="preview-message"><span className="eyebrow">SOURCE MESSAGE</span><p>{selectedTrade.rawMessage}</p></div>
    </section>
  </div>
}

function OrderBranch({ trade, order }: { trade: TradeSummary; order: OrderSummary }) {
  const executions = trade.executions.filter((execution) => execution.orderId === order.orderId)
  const isTarget = order.parentOrderId !== null
  return <div className={`order-branch${isTarget ? ' is-target' : ''}`}>
    <div className="order-row">
      {isTarget ? <CornerDownRight size={14} /> : <CircleDot size={13} />}
      <span className="order-role">{order.role}</span>
      <span className="order-side">{order.action} ×{order.quantity}</span>
      <strong>{money.format(order.limitPrice)}</strong>
      <span className={`order-state order-state--${order.status.toLowerCase()}`}>{order.status}</span>
    </div>
    {executions.map((execution) => <div className="execution-row" key={execution.executionId}>
      <span>FILL</span><strong>{money.format(execution.price)} ×{execution.quantity}</strong><time>{shortTime.format(new Date(execution.executedAt))}</time>
    </div>)}
  </div>
}
