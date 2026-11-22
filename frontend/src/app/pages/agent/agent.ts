import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AgentChatService } from '../../services/agentChatService';
import { ChatComponent } from '../../components/chat-component/chat-component';
import { ConversationSidebar } from '../../components/conversation-sidebar/conversation-sidebar';
import { BacktestLaunchService } from '../../services/backtestLaunchService';

@Component({
  imports: [ConversationSidebar, ChatComponent],
  host: { class: 'flex h-full min-h-0 w-full flex-1' },
  templateUrl: './agent.html',
})
export class Agent implements OnInit {
  readonly chat = inject(AgentChatService);
  readonly mobileSidebarOpen = signal(false);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly backtestLaunch = inject(BacktestLaunchService);

  ngOnInit(): void {
    this.chat.loadConversations();
    this.route.paramMap.subscribe((params) => {
      const conversationId = params.get('conversationId');
      if (conversationId) {
        const pendingBacktest = this.backtestLaunch.take(conversationId);
        if (pendingBacktest) {
          this.chat.openConversation(conversationId, () =>
            this.chat.sendMessage(pendingBacktest.message, pendingBacktest.parameters));
        } else if (this.chat.activeConversationId() !== conversationId) {
          this.chat.openConversation(conversationId);
        }
      }
      else if (!conversationId && this.chat.activeConversationId())
        this.chat.startNewConversation();
    });
  }

  startNewConversation(): void {
    this.chat.startNewConversation();
    void this.router.navigate(['/agent']);
    this.mobileSidebarOpen.set(false);
  }

  openConversation(conversationId: string): void {
    void this.router.navigate(['/agent', conversationId]);
  }
}
