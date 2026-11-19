import { Component, input, output } from '@angular/core';
import { NgClass } from '@angular/common';
import { ConversationSummary } from '../../interfaces/agent_dtos';

@Component({
  selector: 'app-conversation-sidebar',
  standalone: true,
  imports: [NgClass],
  host: { class: 'block h-full min-h-0 w-0 shrink-0 md:w-[300px]' },
  templateUrl: './conversation-sidebar.html',
})
export class ConversationSidebar {
  readonly conversations = input<ConversationSummary[]>([]);
  readonly activeConversationId = input<string | null>(null);
  readonly mobileOpen = input(false);
  readonly newConversation = output<void>();
  readonly selectConversation = output<string>();
  readonly deleteConversation = output<string>();
  readonly closeSidebar = output<void>();
}
