import { Injectable } from '@angular/core';
import { PendingBacktestLaunch } from '../interfaces/backtest_dtos';

@Injectable({ providedIn: 'root' })
export class BacktestLaunchService {
  private readonly pending = new Map<string, PendingBacktestLaunch>();

  queue(launch: PendingBacktestLaunch): void {
    this.pending.set(launch.conversationId, launch);
  }

  take(conversationId: string): PendingBacktestLaunch | null {
    const launch = this.pending.get(conversationId) ?? null;
    this.pending.delete(conversationId);
    return launch;
  }

  cancel(conversationId: string): void {
    this.pending.delete(conversationId);
  }
}
