import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';
import { CbotDetail, CbotSummary, CompileCbotResponse } from '../interfaces/cbot_dtos';

@Injectable({ providedIn: 'root' })
export class CbotsService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/api/bots`;

  list() {
    return this.http.get<CbotSummary[]>(this.apiUrl);
  }

  get(botId: string) {
    return this.http.get<CbotDetail>(`${this.apiUrl}/${botId}`);
  }

  saveSource(botId: string, sourceCode: string) {
    return this.http.put<void>(`${this.apiUrl}/${botId}/source`, { sourceCode });
  }

  delete(botId: string) {
    return this.http.delete<void>(`${this.apiUrl}/${botId}`);
  }

  compile(botId: string) {
    return this.http.post<CompileCbotResponse>(`${this.apiUrl}/${botId}/compile`, {});
  }

  download(botId: string, artifact: 'source' | 'algo') {
    return this.http.get(`${this.apiUrl}/${botId}/download/${artifact}`, { responseType: 'blob' });
  }
}
