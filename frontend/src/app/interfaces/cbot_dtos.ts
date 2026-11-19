export interface CbotSummary {
  id: string;
  name: string;
  conversationId: string;
  conversationTitle: string;
  initialPrompt: string;
  lastCompileSucceeded: boolean;
  hasAlgoFile: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CbotDetail extends CbotSummary {
  sourceCode: string;
  lastCompileOutput: string;
}

export interface CompileCbotResponse {
  succeeded: boolean;
  output: string;
  sourceCodePath?: string | null;
  algoFilePath?: string | null;
  botId?: string | null;
}
