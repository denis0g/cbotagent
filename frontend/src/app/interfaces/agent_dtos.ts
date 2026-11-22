import { BacktestParameters } from './backtest_dtos';

export interface AgentMessageRequest {
  conversationId: string | null;
  message: string;
  backtest?: BacktestParameters;
}

export interface AgentMessageResponse {
  conversationId: string;
  conversationTitle: string;
  messageId: string;
  reply: string;
  createdAt: string;
}

export interface ConversationSummary {
  id: string;
  title: string;
  createdAt: string;
  updatedAt: string;
}

export interface ConversationMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  createdAt: string;
}

export interface ConversationDetail extends ConversationSummary {
  messages: ConversationMessage[];
}
