import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from './authService';
import { environment } from '../../environments/environment';
import {
  AgentMessageRequest,
  AgentMessageResponse,
  ConversationDetail,
  ConversationMessage,
  ConversationSummary,
} from '../interfaces/agent_dtos';

@Injectable({ providedIn: 'root' })
export class AgentChatService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly apiUrl = `${environment.apiUrl}/api/agent`;
  private loadSequence = 0;
  private errorTimer: ReturnType<typeof setTimeout> | undefined;
  private activeRequest: { controller: AbortController; requestId: string } | undefined;

  private readonly conversationState = signal<ConversationSummary[]>([]);
  private readonly messageState = signal<ConversationMessage[]>([]);
  private readonly activeConversationIdState = signal<string | null>(null);
  readonly conversations = this.conversationState.asReadonly();
  readonly messages = this.messageState.asReadonly();
  readonly activeConversationId = this.activeConversationIdState.asReadonly();
  readonly activeConversation = computed(() =>
    this.conversationState().find((item) => item.id === this.activeConversationIdState()) ?? null,
  );

  readonly loadingConversations = signal(false);
  readonly loadingMessages = signal(false);
  readonly sending = signal(false);
  readonly progress = signal('');
  readonly error = signal('');

  loadConversations(): void {
    this.loadingConversations.set(true);
    this.http.get<ConversationSummary[]>(`${this.apiUrl}/conversations`).subscribe({
      next: (conversations) => {
        this.conversationState.set(conversations);
        this.loadingConversations.set(false);
      },
      error: (error: unknown) => {
        this.loadingConversations.set(false);
        this.showError(this.errorMessage(error, 'Could not load conversations.'));
      },
    });
  }

  startNewConversation(): void {
    this.loadSequence++;
    this.cancelActiveRequest();
    this.activeConversationIdState.set(null);
    this.messageState.set([]);
    this.clearError();
    this.progress.set('');
  }

  openConversation(conversationId: string): void {
    const sequence = ++this.loadSequence;
    this.cancelActiveRequest();
    this.activeConversationIdState.set(conversationId);
    this.messageState.set([]);
    this.clearError();
    this.progress.set('');
    this.loadingMessages.set(true);

    this.http.get<ConversationDetail>(`${this.apiUrl}/conversations/${conversationId}`).subscribe({
      next: (conversation) => {
        if (sequence !== this.loadSequence) return;
        this.messageState.set(conversation.messages);
        this.loadingMessages.set(false);
      },
      error: (error: unknown) => {
        if (sequence !== this.loadSequence) return;
        this.loadingMessages.set(false);
        this.showError(this.errorMessage(error, 'Could not load this conversation.'));
      },
    });
  }

  sendMessage(message: string): void {
    const content = message.trim();
    if (!content || this.sending()) return;

    const requestConversationId = this.activeConversationIdState();
    const previousMessages = this.messageState();
    const optimisticMessage: ConversationMessage = {
      id: crypto.randomUUID(),
      role: 'user',
      content,
      createdAt: new Date().toISOString(),
    };
    this.messageState.update((messages) => [...messages, optimisticMessage]);
    this.clearError();
    this.sending.set(true);
    this.progress.set('');

    const request: AgentMessageRequest = {
      conversationId: requestConversationId,
      message: content,
    };
    const requestSequence = this.loadSequence;
    const controller = new AbortController();
    const requestId = crypto.randomUUID();
    this.activeRequest = { controller, requestId };
    const assistantId = crypto.randomUUID();
    this.messageState.update((messages) => [...messages, {
      id: assistantId,
      role: 'assistant',
      content: '',
      createdAt: new Date().toISOString(),
    }]);
    void this.streamMessage(request, assistantId, requestSequence, requestId, controller, previousMessages);
  }

  private async streamMessage(
    request: AgentMessageRequest,
    assistantId: string,
    requestSequence: number,
    requestId: string,
    controller: AbortController,
    previousMessages: ConversationMessage[],
  ): Promise<void> {
    let streamConversationId = request.conversationId;
    let completed = false;
    try {
      const token = this.auth.token();
      const response = await fetch(`${this.apiUrl}/messages`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'text/event-stream',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body: JSON.stringify(request),
        signal: controller.signal,
      });
      if (!response.ok || !response.body) {
        if (response.status === 401) this.auth.logout();
        const body = await response.text();
        let message = body || `Request failed (${response.status}).`;
        try {
          const problem = JSON.parse(body) as { detail?: string; title?: string };
          message = problem.detail || problem.title || message;
        } catch { /* The backend may return plain text. */ }
        throw new Error(message);
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      while (true) {
        const { value, done } = await reader.read();
        buffer += decoder.decode(value, { stream: !done });
        let boundary: number;
        while ((boundary = buffer.indexOf('\n\n')) >= 0) {
          const event = buffer.slice(0, boundary).replace(/\r/g, '');
          buffer = buffer.slice(boundary + 2);
          const eventName = event.split('\n').find((line) => line.startsWith('event:'))?.slice(6).trim();
          const payload = event.split('\n').filter((line) => line.startsWith('data:'))
            .map((line) => line.slice(5).trim()).join('\n');
          if (!payload) continue;
          const data = JSON.parse(payload) as Partial<AgentMessageResponse> & {
            ConversationId?: string;
            ConversationTitle?: string;
            MessageId?: string;
            CreatedAt?: string;
            conversationId?: string;
            title?: string;
            text?: string;
            status?: string;
            message?: string;
          };
          if (eventName === 'delta' && typeof data.text === 'string') {
            this.messageState.update((messages) => messages.map((message) => message.id === assistantId
              ? { ...message, content: message.content + data.text }
              : message));
          } else if (eventName === 'conversation' && typeof data.conversationId === 'string') {
            streamConversationId = data.conversationId;
            if (this.loadSequence === requestSequence) this.activeConversationIdState.set(data.conversationId);
            this.upsertConversation(data.conversationId, 'New conversation');
          } else if (eventName === 'title' && typeof data.conversationId === 'string' && typeof data.title === 'string') {
            this.upsertConversation(data.conversationId, data.title);
          } else if (eventName === 'progress' && typeof data.status === 'string') {
            this.progress.set(data.status);
          } else if (eventName === 'done') {
            completed = true;
            this.progress.set('');
            const conversationId = data.conversationId ?? data.ConversationId ?? request.conversationId;
            const conversationTitle = data.conversationTitle ?? data.ConversationTitle;
            const messageId = data.messageId ?? data.MessageId;
            const createdAt = data.createdAt ?? data.CreatedAt;
            if (conversationId && this.loadSequence === requestSequence)
              this.activeConversationIdState.set(conversationId);
            if (conversationId && conversationTitle) this.upsertConversation(conversationId, conversationTitle, createdAt);
            this.messageState.update((messages) => messages.map((message) => message.id === assistantId
              ? { ...message, id: messageId ?? assistantId, createdAt: createdAt ?? message.createdAt }
              : message));
            this.loadConversations();
          } else if (eventName === 'error') {
            throw new Error(data.message || 'The agent could not complete this request.');
          }
        }
        if (done) break;
      }
      if (!completed) throw new Error('The agent stream ended before the response was complete.');
    } catch (error: unknown) {
      if (controller.signal.aborted) {
        if (!completed && this.loadSequence === requestSequence && this.activeConversationIdState() === streamConversationId)
          this.messageState.set(previousMessages);
        if (!completed && request.conversationId === null && streamConversationId)
          this.conversationState.update((items) => items.filter((item) => item.id !== streamConversationId));
      } else {
        if (this.loadSequence === requestSequence && this.activeConversationIdState() === streamConversationId)
          this.messageState.set(previousMessages);
        this.showError(error instanceof Error && error.message ? error.message : 'The message could not be sent. Please try again.');
      }
    } finally {
      if (this.activeRequest?.requestId === requestId) {
        this.activeRequest = undefined;
        this.sending.set(false);
        this.progress.set('');
      }
    }
  }

  deleteConversation(conversationId: string): void {
    if (this.activeConversationIdState() === conversationId) {
      this.loadSequence++;
      this.cancelActiveRequest();
      this.activeConversationIdState.set(null);
      this.messageState.set([]);
      this.loadingMessages.set(false);
    }
    this.http.delete<void>(`${this.apiUrl}/conversations/${conversationId}`).subscribe({
      next: () => {
        this.conversationState.update((items) => items.filter((item) => item.id !== conversationId));
      },
      error: (error: unknown) => this.showError(this.errorMessage(error, 'Could not delete this conversation.')),
    });
  }

  private cancelActiveRequest(): void {
    const request = this.activeRequest;
    if (!request) return;
    this.activeRequest = undefined;
    request.controller.abort();
    this.sending.set(false);
    this.progress.set('');
  }

  clearError(): void {
    if (this.errorTimer) clearTimeout(this.errorTimer);
    this.error.set('');
  }

  private showError(message: string): void {
    if (this.errorTimer) clearTimeout(this.errorTimer);
    this.error.set(message);
    this.errorTimer = setTimeout(() => this.error.set(''), 6000);
  }

  private upsertConversation(id: string, title: string, updatedAt = new Date().toISOString()): void {
    const existing = this.conversationState().find((item) => item.id === id);
    const summary: ConversationSummary = {
      id,
      title,
      createdAt: existing?.createdAt ?? updatedAt,
      updatedAt,
    };
    this.conversationState.update((items) => [summary, ...items.filter((item) => item.id !== id)]);
  }

  private errorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      if (typeof error.error === 'string' && error.error.trim()) return error.error;
      if (typeof error.error?.detail === 'string' && error.error.detail.trim()) return error.error.detail;
    }
    return fallback;
  }
}
