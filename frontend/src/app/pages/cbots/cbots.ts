import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CbotsService } from '../../services/cbotsService';
import { CbotSummary } from '../../interfaces/cbot_dtos';

@Component({
  imports: [DatePipe, RouterLink],
  templateUrl: './cbots.html',
})
export class Cbots implements OnInit {
  private readonly service = inject(CbotsService);
  readonly bots = signal<CbotSummary[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');

  ngOnInit(): void {
    this.service.list().subscribe({
      next: (bots) => { this.bots.set(bots); this.loading.set(false); },
      error: () => { this.error.set('Could not load your cBots.'); this.loading.set(false); },
    });
  }
}
