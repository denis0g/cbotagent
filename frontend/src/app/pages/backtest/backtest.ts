import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CbotSummary } from '../../interfaces/cbot_dtos';
import { BacktestParameters } from '../../interfaces/backtest_dtos';
import { CbotsService } from '../../services/cbotsService';
import { BacktestLaunchService } from '../../services/backtestLaunchService';

interface BacktestForm {
  cTraderId: string;
  password: string;
  account: string;
  symbol: string;
  period: string;
  start: string;
  end: string;
  dataMode: BacktestParameters['dataMode'];
  balance: number;
  commission: number;
  spread: number;
}

@Component({
  imports: [DatePipe, FormsModule, RouterLink],
  host: { class: 'block h-full min-h-0 w-full flex-1' },
  templateUrl: './backtest.html',
})
export class Backtest implements OnInit, OnDestroy {
  private readonly cbots = inject(CbotsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly launchService = inject(BacktestLaunchService);

  readonly bots = signal<CbotSummary[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly selectedBot = signal<CbotSummary | null>(null);
  readonly backtestStarting = signal(false);
  readonly form: BacktestForm = {
    cTraderId: '',
    password: '',
    account: '',
    symbol: 'EURUSD',
    period: 'h1',
    start: '2026-01-01',
    end: new Date().toISOString().slice(0, 10),
    dataMode: 'm1',
    balance: 10000,
    commission: 15,
    spread: 1,
  };
  readonly periods = [
    { value: 'm1', label: '1 minute' }, { value: 'm5', label: '5 minutes' },
    { value: 'm15', label: '15 minutes' }, { value: 'm30', label: '30 minutes' },
    { value: 'h1', label: '1 hour' }, { value: 'h4', label: '4 hours' },
    { value: 'd1', label: '1 day' }, { value: 'w1', label: '1 week' },
  ];
  private navigateTimer?: ReturnType<typeof setTimeout>;
  private queuedConversationId: string | null = null;

  ngOnInit(): void {
    this.cbots.list().subscribe({
      next: (bots) => {
        this.bots.set(bots);
        this.loading.set(false);
        const requestedBotId = this.route.snapshot.queryParamMap.get('botId');
        if (requestedBotId) {
          const bot = bots.find((item) => item.id === requestedBotId);
          if (bot) this.openBacktest(bot);
        }
      },
      error: () => {
        this.error.set('Could not load your cBots.');
        this.loading.set(false);
      },
    });
  }

  ngOnDestroy(): void {
    if (this.navigateTimer) {
      clearTimeout(this.navigateTimer);
      if (this.queuedConversationId) this.launchService.cancel(this.queuedConversationId);
    }
    this.form.password = '';
  }

  openBacktest(bot: CbotSummary): void {
    if (bot.lastCompileSucceeded && bot.hasAlgoFile) {
      this.error.set('');
      this.selectedBot.set(bot);
      return;
    }
    this.error.set(`Compile ${bot.name} successfully before starting a backtest.`);
  }

  closeBacktest(): void {
    this.selectedBot.set(null);
    this.form.password = '';
  }

  datesInvalid(): boolean {
    return Boolean(this.form.start && this.form.end && this.form.start > this.form.end);
  }

  startBacktest(form: NgForm): void {
    const bot = this.selectedBot();
    if (!bot || !form.valid || this.datesInvalid() || this.backtestStarting()) return;

    const parameters: BacktestParameters = { botId: bot.id, ...this.form };
    const message = `Please backtest my cBot "${bot.name}" (bot ID: ${bot.id}) using the settings I selected. Run its compiled .algo artifact and summarize the result.`;
    this.launchService.queue({ botId: bot.id, conversationId: bot.conversationId, message, parameters });
    this.queuedConversationId = bot.conversationId;
    this.form.password = '';
    this.selectedBot.set(null);
    this.backtestStarting.set(true);
    this.navigateTimer = setTimeout(() => {
      this.navigateTimer = undefined;
      this.backtestStarting.set(false);
      void this.router.navigate(['/agent', bot.conversationId]);
    }, 1100);
  }
}
