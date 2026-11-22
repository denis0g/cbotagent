import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CbotsService } from '../../services/cbotsService';
import { CbotSummary } from '../../interfaces/cbot_dtos';
import { AuthService } from '../../services/authService';

@Component({
  imports: [DatePipe, RouterLink],
  host: { class: 'block h-full min-h-0 w-full flex-1' },
  templateUrl: './cbots.html',
})
export class Cbots implements OnInit {
  private readonly service = inject(CbotsService);
  readonly auth = inject(AuthService);
  readonly bots = signal<CbotSummary[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly deleteError = signal('');
  readonly deletingBotId = signal<string | null>(null);
  readonly confirmingDelete = signal<CbotSummary | null>(null);

  ngOnInit(): void {
    this.service.list().subscribe({
      next: (bots) => { this.bots.set(bots); this.loading.set(false); },
      error: () => { this.error.set('Could not load your cBots.'); this.loading.set(false); },
    });
  }

  requestDelete(bot: CbotSummary): void {
    if (this.deletingBotId()) return;
    this.deleteError.set('');
    this.confirmingDelete.set(bot);
  }

  cancelDelete(): void {
    if (this.deletingBotId()) return;
    this.confirmingDelete.set(null);
    this.deleteError.set('');
  }

  deleteBot(): void {
    const bot = this.confirmingDelete();
    if (!bot || this.deletingBotId()) return;
    this.deleteError.set('');
    this.deletingBotId.set(bot.id);
    this.service.delete(bot.id).subscribe({
      next: () => {
        this.bots.update((items) => items.filter((item) => item.id !== bot.id));
        this.deletingBotId.set(null);
        this.confirmingDelete.set(null);
      },
      error: () => {
        this.deleteError.set(`Could not delete ${bot.name}. Please try again.`);
        this.deletingBotId.set(null);
      },
    });
  }
}
