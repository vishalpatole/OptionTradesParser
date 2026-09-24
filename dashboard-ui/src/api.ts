import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'

export type OrderSummary = {
  orderId: number
  clientId: number
  role: string
  action: string
  quantity: number
  limitPrice: number
  status: string
  parentOrderId: number | null
  submittedAt: string
}

export type ExecutionSummary = {
  executionId: string
  orderId: number
  action: string
  quantity: number
  price: number
  executedAt: string
  commission: number | null
  realizedPnl: number | null
}

export type TradeSummary = {
  id: string
  trader: string
  action: string
  ticker: string
  optionType: string
  strike: number
  expiration: string
  alertPrice: number
  risk: string
  status: string
  alertedAt: string
  rawMessage: string
  orders: OrderSummary[]
  executions: ExecutionSummary[]
  audit: { step: string; status: string; details: string; timestamp: string }[]
}

export type CalendarDaySummary = {
  date: string
  pnl: number
  closedTrades: number
  openTrades: number
}

export type DashboardSnapshot = {
  accountType: string
  capturedTrades: number
  executedTrades: number
  routedOrders: number
  workingOrders: number
  rejectedOrders: number
  asOf: string
  recentTrades: TradeSummary[]
}

export type PendingApproval = {
  id: string
  trader: string
  intent: string
  orderAction: string
  ticker: string
  optionType: string
  strike: number
  expiry: string
  quantity: number
  orderType: string
  limitPrice: number
  marketQuote: string
  contractSymbol: string
  riskCategory: string
  accountType: string
  contractInferred: boolean
  warnings: string[]
  budgetOptions: { budget: number; quantity: number; estimatedValue: number }[]
  createdAt: string
}

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path)
  if (!response.ok) throw new Error(`Dashboard request failed (${response.status})`)
  return response.json() as Promise<T>
}

export const getDashboard = (year?: number, month?: number) => getJson<DashboardSnapshot>(year && month ? `/api/dashboard?year=${year}&month=${month}` : '/api/dashboard')
export const getTrades = (date?: string) => getJson<TradeSummary[]>(date ? `/api/trades?date=${encodeURIComponent(date)}` : '/api/trades?days=90')
export const getTradesForRange = (startDate: string, endDate: string) => getJson<TradeSummary[]>(`/api/trades?startDate=${encodeURIComponent(startDate)}&endDate=${encodeURIComponent(endDate)}`)
export const getCalendar = (year: number, month: number) => getJson<CalendarDaySummary[]>(`/api/calendar?year=${year}&month=${month}`)
export const getPendingApprovals = () => getJson<PendingApproval[]>('/api/approvals/pending')

export async function setSelectedTradeDate(date: string) {
  const response = await fetch('/api/settings/selected-trade-date', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ date }),
  })
  if (!response.ok) throw new Error(`Selected date update failed (${response.status})`)
  return response.json() as Promise<{ date: string }>
}

async function sendApproval(path: string, body?: unknown) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body ?? {}),
  })
  const result = await response.json() as { success: boolean; message: string }
  if (!response.ok) throw new Error(result.message)
  return result
}

export const approveTrade = (id: string, budget: number | null) => sendApproval(`/api/approvals/${id}/approve`, { budget })
export const rejectTrade = (id: string) => sendApproval(`/api/approvals/${id}/reject`)

export function connectTradeUpdates(onChanged: () => void) {
  const connection = new HubConnectionBuilder()
    .withUrl('/hubs/trades')
    .withAutomaticReconnect()
    .configureLogging(LogLevel.None)
    .build()

  connection.on('tradesChanged', onChanged)
  void connection.start().catch(() => undefined)
  return () => void connection.stop()
}
