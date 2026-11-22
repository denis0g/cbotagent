export interface BacktestParameters {
  botId: string;
  cTraderId: string;
  password: string;
  account: string;
  symbol: string;
  period: string;
  start: string;
  end: string;
  dataMode: 'm1' | 'ticks' | 'm1-csv' | 'tick-csv' | 'open';
  balance: number;
  commission: number;
  spread: number;
}

export interface PendingBacktestLaunch {
  botId: string;
  conversationId: string;
  message: string;
  parameters: BacktestParameters;
}

export interface BacktestRun {
  id: string;
  botId: string;
  botName: string;
  status: 'Running' | 'Succeeded' | 'Failed';
  account: string;
  symbol: string;
  period: string;
  start: string;
  end: string;
  dataMode: string;
  balance: number;
  commission: number;
  spread: number;
  output: string;
  performanceOverview: string;
  suggestedImprovements: string[];
  startedAt: string;
  completedAt: string | null;
  htmlReportUrl: string | null;
  jsonReportUrl: string | null;
}
