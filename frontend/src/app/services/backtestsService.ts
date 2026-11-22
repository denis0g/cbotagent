import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';
import { BacktestRun } from '../interfaces/backtest_dtos';

@Injectable({ providedIn: 'root' })
export class BacktestsService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/api/backtests`;

  latest(botId: string) {
    return this.http.get<BacktestRun>(`${this.apiUrl}/${botId}/latest`);
  }

  report(runId: string, format: 'html' | 'json') {
    return this.http.get(`${this.apiUrl}/runs/${runId}/report/${format}`, { responseType: 'blob' });
  }
}
