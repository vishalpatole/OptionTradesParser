import { startTransition, useEffect, useEffectEvent, useState, type ReactNode } from 'react'
import { Activity, BarChart3, CalendarDays, CircleAlert, LayoutDashboard, RefreshCw, TableProperties, Wifi } from 'lucide-react'
import { Bar, BarChart, CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { connectTradeUpdates, getCalendar, getDashboard, getPendingApprovals, getTrades, getTradesForRange, setSelectedTradeDate, type CalendarDaySummary, type DashboardSnapshot, type PendingApproval, type TradeSummary } from './api'
import { ApprovalModal } from './ApprovalModal'
import { TradeLedger } from './TradeLedger'

const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

function analyticsRange(scope: 'day' | 'month' | 'year', value: string) {
  if (scope === 'day') {
    const end = new Date(`${value}T00:00:00Z`)
    end.setUTCDate(end.getUTCDate() + 1)
    return { start: value, end: end.toISOString().slice(0, 10) }
  }

  if (scope === 'month') {
    const [year, month] = value.split('-').map(Number)
    const end = new Date(Date.UTC(year, month, 1))
    return { start: `${value}-01`, end: end.toISOString().slice(0, 10) }
  }

  return { start: `${value}-01-01`, end: `${Number(value) + 1}-01-01` }
}

function App() {
  const [tab, setTab] = useState<'dashboard' | 'trades' | 'analytics'>('dashboard')
  const [dashboard, setDashboard] = useState<DashboardSnapshot | null>(null)
  const [trades, setTrades] = useState<TradeSummary[]>([])
  const [analyticsTrades, setAnalyticsTrades] = useState<TradeSummary[]>([])
    const [calendar, setCalendar] = useState<CalendarDaySummary[]>([])
    const [selectedDate, setSelectedDate] = useState(() => new Date().toISOString().slice(0, 10))
    const [calendarMonth, setCalendarMonth] = useState(() => new Date().toISOString().slice(0, 7))
  const [analyticsScope, setAnalyticsScope] = useState<'day' | 'month' | 'year'>('day')
  const [analyticsPeriod, setAnalyticsPeriod] = useState(() => new Date().toISOString().slice(0, 10))
  const [approvals, setApprovals] = useState<PendingApproval[]>([])
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  const loadData = useEffectEvent(async () => {
    try {
      const [year, month] = calendarMonth.split('-').map(Number)
      const range = analyticsRange(analyticsScope, analyticsPeriod)
      const [nextDashboard, nextTrades, nextApprovals, nextCalendar, nextAnalyticsTrades] = await Promise.all([
        getDashboard(year, month),
        getTrades(selectedDate),
        getPendingApprovals(),
        getCalendar(year, month),
        getTradesForRange(range.start, range.end),
        setSelectedTradeDate(selectedDate),
      ])
      startTransition(() => {
        setDashboard(nextDashboard)
        setTrades(nextTrades)
        setAnalyticsTrades(nextAnalyticsTrades)
        setApprovals(nextApprovals)
        setCalendar(nextCalendar)
        setError('')
        setLoading(false)
      })
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Dashboard is unavailable')
      setLoading(false)
    }
  })

  const loadApprovals = useEffectEvent(async () => {
    try {
      const nextApprovals = await getPendingApprovals()
      startTransition(() => {
        setApprovals(nextApprovals)
        if (nextApprovals.length > 0) setError('')
      })
    } catch {
      // Full dashboard loading reports API availability; keep this hot path quiet for speed.
    }
  })

  useEffect(() => {
    void loadData()
    return connectTradeUpdates(() => void loadData())
  }, [selectedDate, calendarMonth, analyticsScope, analyticsPeriod])

  useEffect(() => {
    const timer = window.setInterval(() => void loadApprovals(), 400)
    return () => window.clearInterval(timer)
  }, [])

  return <div className="app-shell">
    <header className="topbar">
      <div className="brand"><span><Activity size={19} /></span><strong>OPTIONALITY</strong></div>
      <nav aria-label="Primary navigation">
        <Tab active={tab === 'dashboard'} onClick={() => setTab('dashboard')} icon={<LayoutDashboard size={16} />} label="Dashboard" />
        <Tab active={tab === 'trades'} onClick={() => setTab('trades')} icon={<TableProperties size={16} />} label="Trades" />
        <Tab active={tab === 'analytics'} onClick={() => setTab('analytics')} icon={<BarChart3 size={16} />} label="Analytics" />
      </nav>
      <div className="topbar__right">
        <span className="connection"><Wifi size={14} /> LIVE DATA</span>
        <div className="account-switch"><strong>{dashboard?.accountType === 'LIVE' ? 'Live' : 'Paper'}</strong><span>{dashboard?.accountType === 'LIVE' ? 'Paper' : 'Live'}</span></div>
        <button className="icon-button" title="Refresh dashboard" onClick={() => void loadData()}><RefreshCw size={16} /></button>
      </div>
    </header>

    <main>
      <div className="page-heading">
        <div><span className="eyebrow">EXECUTION DESK</span><h1>{tab === 'dashboard' ? 'Trade command center' : tab === 'trades' ? 'Trade ledger' : 'Execution analytics'}</h1></div>
        <div className="date-controls">
          {tab === 'dashboard'
            ? <label><CalendarDays size={14} /> Month <input type="month" value={calendarMonth} onChange={(event) => setCalendarMonth(event.target.value)} /></label>
            : tab === 'analytics'
              ? <>
                <label>Scope <select value={analyticsScope} onChange={(event) => {
                  const nextScope = event.target.value as 'day' | 'month' | 'year'
                  setAnalyticsScope(nextScope)
                  setAnalyticsPeriod(new Date().toISOString().slice(0, nextScope === 'day' ? 10 : nextScope === 'month' ? 7 : 4))
                }}><option value="day">Day</option><option value="month">Month</option><option value="year">Year</option></select></label>
                <label><CalendarDays size={14} /> Period <input type={analyticsScope === 'day' ? 'date' : analyticsScope === 'month' ? 'month' : 'number'} value={analyticsPeriod} min="2000" max="2100" onChange={(event) => setAnalyticsPeriod(event.target.value)} /></label>
              </>
              : <label><CalendarDays size={14} /> Trade date <input type="date" value={selectedDate} onChange={(event) => setSelectedDate(event.target.value)} /></label>}
        </div>
      </div>
      {error && <div className="error-banner"><CircleAlert size={18} /> {error}. Start the dashboard API on port 5086.</div>}
      {loading ? <Loading /> : tab === 'dashboard' ? <Dashboard dashboard={dashboard} calendar={calendar} month={calendarMonth} onSelectDate={(date) => { setSelectedDate(date); setTab('trades') }} /> : tab === 'trades' ? <Trades trades={trades} /> : <Analytics trades={analyticsTrades} scope={analyticsScope} />}
    </main>
    {approvals[0] && <ApprovalModal approval={approvals[0]} queuedCount={approvals.length} onDecided={() => void loadData()} />}
  </div>
}

function Dashboard({ dashboard, calendar, month, onSelectDate }: { dashboard: DashboardSnapshot | null; calendar: CalendarDaySummary[]; month: string; onSelectDate: (date: string) => void }) {
  if (!dashboard) return null
  return <>
    <section className="stats-grid">
      <Stat label="CAPTURED ALERTS" value={dashboard.capturedTrades} note="Selected month" />
      <Stat label="EXECUTED TRADES" value={dashboard.executedTrades} note="With broker fills" />
      <Stat label="ROUTED ORDERS" value={dashboard.routedOrders} note="Entry and target orders" />
      <Stat label="WORKING NOW" value={dashboard.workingOrders} note="Submitted at broker" accent />
      <Stat label="NEEDS REVIEW" value={dashboard.rejectedOrders} note="Rejected or inactive" warning={dashboard.rejectedOrders > 0} />
    </section>
    <section className="calendar-panel">
      <div className="section-heading"><div><h2>{month} P/L calendar</h2><p>Daily realized/estimated premium P/L from recorded fills</p></div></div>
      <div className="month-grid">
        {calendar.map((day) => <button key={day.date} className={`day-block${day.pnl > 0 ? ' is-profit' : day.pnl < 0 ? ' is-loss' : ''}`} onClick={() => onSelectDate(day.date)}>
          <span>{Number(day.date.slice(-2))}</span>
          <strong>{money.format(day.pnl)}</strong>
          <small>{day.closedTrades} closed · {day.openTrades} open</small>
        </button>)}
      </div>
    </section>
  </>
}

function Trades({ trades }: { trades: TradeSummary[] }) {
  return <TradeLedger trades={trades} />
}

function Analytics({ trades, scope }: { trades: TradeSummary[]; scope: 'day' | 'month' | 'year' }) {
  const pnlByTrade = trades.map((trade) => {
    const sellExecutions = trade.executions.filter((execution) => execution.action === 'SELL')
    return {
      trade,
      pnl: sellExecutions.reduce((sum, execution) => sum + (execution.realizedPnl ?? 0), 0),
      executedAt: sellExecutions.at(-1)?.executedAt ?? trade.alertedAt,
    }
  })
  const pnlByPeriod = groupBy(pnlByTrade, (item) => analyticsBucket(item.executedAt, scope), ({ pnl }) => pnl)
  const pnlByTicker = groupBy(pnlByTrade, ({ trade }) => trade.ticker, ({ pnl }) => pnl)
  const volumeByType = groupBy(trades, (trade) => trade.optionType, (trade) => trade.orders.find((order) => order.role === 'ENTRY')?.quantity ?? 0)
  const pnlByTrader = groupBy(pnlByTrade, ({ trade }) => trade.trader || 'UNKNOWN', ({ pnl }) => pnl)

  return <div className="analytics-grid analytics-grid--wide">
    <section className="chart-panel chart-panel--wide"><div className="section-heading"><div><h2>P/L over selected period</h2><p>Realized P/L grouped {scope === 'day' ? 'by 5-minute interval' : scope === 'month' ? 'by day' : 'by month'}</p></div></div><ResponsiveContainer width="100%" height={300}><LineChart data={pnlByPeriod}><CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#dfe4ea" /><XAxis dataKey="label" tickLine={false} axisLine={false} /><YAxis tickLine={false} axisLine={false} /><Tooltip formatter={(value) => money.format(Number(value))} /><Line type="monotone" dataKey="value" stroke="#3157d5" strokeWidth={2} dot={{ r: 3 }} /></LineChart></ResponsiveContainer></section>
    <section className="chart-panel"><div className="section-heading"><div><h2>P/L by ticker</h2><p>Net realized result grouped by security</p></div></div><ResponsiveContainer width="100%" height={280}><BarChart data={pnlByTicker}><CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#dfe4ea" /><XAxis dataKey="label" tickLine={false} axisLine={false} /><YAxis tickLine={false} axisLine={false} /><Tooltip formatter={(value) => money.format(Number(value))} /><Bar dataKey="value" fill="#3157d5" radius={[3, 3, 0, 0]} /></BarChart></ResponsiveContainer></section>
    <section className="chart-panel"><div className="section-heading"><div><h2>Call vs put volume</h2><p>Entry contract volume by option side</p></div></div><ResponsiveContainer width="100%" height={280}><BarChart data={volumeByType}><CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#dfe4ea" /><XAxis dataKey="label" tickLine={false} axisLine={false} /><YAxis allowDecimals={false} tickLine={false} axisLine={false} /><Tooltip /><Bar dataKey="value" fill="#20a66b" radius={[3, 3, 0, 0]} /></BarChart></ResponsiveContainer></section>
    <section className="chart-panel chart-panel--wide"><div className="section-heading"><div><h2>P/L by trader</h2><p>Net realized result grouped by alert source</p></div></div><ResponsiveContainer width="100%" height={280}><BarChart data={pnlByTrader}><CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#dfe4ea" /><XAxis dataKey="label" tickLine={false} axisLine={false} /><YAxis tickLine={false} axisLine={false} /><Tooltip formatter={(value) => money.format(Number(value))} /><Bar dataKey="value" fill="#d8841f" radius={[3, 3, 0, 0]} /></BarChart></ResponsiveContainer></section>
  </div>
}

function analyticsBucket(value: string, scope: 'day' | 'month' | 'year') {
  const moment = new Date(value)
  if (scope === 'day') {
    const parts = new Intl.DateTimeFormat('en-US', { hour: 'numeric', minute: '2-digit', hour12: true, timeZone: 'America/Los_Angeles' }).formatToParts(moment)
    const hour = parts.find((part) => part.type === 'hour')?.value?.padStart(2, '0') ?? '00'
    const rawMinute = Number(parts.find((part) => part.type === 'minute')?.value ?? '0')
    const minute = (Math.floor(rawMinute / 5) * 5).toString().padStart(2, '0')
    const period = parts.find((part) => part.type === 'dayPeriod')?.value ?? ''
    return `${hour}:${minute} ${period}`
  }
  if (scope === 'month') return new Intl.DateTimeFormat('en-US', { day: '2-digit', timeZone: 'America/Los_Angeles' }).format(moment)
  return moment.toLocaleString('en-US', { month: 'short', timeZone: 'America/Los_Angeles' })
}

function groupBy<T>(items: T[], labelFor: (item: T) => string, valueFor: (item: T) => number) {
  const grouped = items.reduce<Record<string, { label: string; value: number; order: number }>>((acc, item) => {
    const label = labelFor(item)
    acc[label] = { label, value: (acc[label]?.value ?? 0) + valueFor(item), order: bucketOrder(label) }
    return acc
  }, {})
  return Object.values(grouped).sort((left, right) => left.order - right.order).map(({ label, value }) => ({ label, value }))
}

function bucketOrder(label: string) {
  const timeMatch = /^(\d{1,2}):(\d{2})\s*(AM|PM)$/i.exec(label)
  if (timeMatch) {
    let hour = Number(timeMatch[1]) % 12
    if (timeMatch[3].toUpperCase() === 'PM') hour += 12
    return hour * 60 + Number(timeMatch[2])
  }

  const numeric = Number(label)
  if (!Number.isNaN(numeric)) return numeric

  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
  const month = months.indexOf(label)
  return month >= 0 ? month : Number.MAX_SAFE_INTEGER
}

function Tab({ active, onClick, icon, label }: { active: boolean; onClick: () => void; icon: ReactNode; label: string }) { return <button className={active ? 'is-active' : ''} onClick={onClick}>{icon}{label}</button> }
function Stat({ label, value, note, accent = false, warning = false }: { label: string; value: number; note: string; accent?: boolean; warning?: boolean }) { return <article className={`stat-card${accent ? ' is-accent' : ''}${warning ? ' is-warning' : ''}`}><span>{label}</span><strong>{value}</strong><small>{note}</small></article> }
function Loading() { return <div className="loading"><i /><i /><i /></div> }

export default App
