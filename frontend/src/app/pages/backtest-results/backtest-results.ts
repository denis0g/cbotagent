import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { BacktestRun } from '../../interfaces/backtest_dtos';
import { BacktestsService } from '../../services/backtestsService';

@Component({
  imports: [DatePipe, RouterLink],
  host: { class: 'block h-full min-h-0 w-full flex-1' },
  templateUrl: './backtest-results.html',
})
export class BacktestResults implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(BacktestsService);
  private readonly sanitizer = inject(DomSanitizer);
  readonly run = signal<BacktestRun | null>(null);
  readonly reportUrl = signal<SafeResourceUrl | null>(null);
  readonly jsonText = signal('');
  readonly loading = signal(true);
  readonly reportError = signal('');
  private htmlBlob: Blob | null = null;
  private jsonBlob: Blob | null = null;
  private htmlObjectUrl: string | null = null;

  ngOnInit(): void {
    const botId = this.route.snapshot.paramMap.get('botId');
    if (!botId) {
      this.loading.set(false);
      this.reportError.set('Backtest not found.');
      return;
    }
    this.reports.latest(botId).subscribe({
      next: (run) => {
        this.run.set(run);
        this.loading.set(false);
        if (run.status === 'Succeeded') this.loadReports(run);
      },
      error: () => {
        this.loading.set(false);
        this.reportError.set('No backtest run is available for this cBot yet.');
      },
    });
  }

  ngOnDestroy(): void {
    if (this.htmlObjectUrl) URL.revokeObjectURL(this.htmlObjectUrl);
  }

  download(format: 'html' | 'json'): void {
    const currentRun = this.run();
    if (!currentRun) return;
    const cached = format === 'html' ? this.htmlBlob : this.jsonBlob;
    if (cached) {
      this.downloadBlob(cached, `${currentRun.botName}.${format}`);
      return;
    }
    this.reports.report(currentRun.id, format).subscribe({
      next: (blob) => this.downloadBlob(blob, `${currentRun.botName}.${format}`),
      error: () => this.reportError.set(`Could not download the ${format.toUpperCase()} report.`),
    });
  }

  private loadReports(currentRun: BacktestRun): void {
    this.reports.report(currentRun.id, 'html').subscribe({
      next: (blob) => {
        this.htmlBlob = blob;
        this.htmlObjectUrl = URL.createObjectURL(blob);
        this.reportUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(this.htmlObjectUrl));
      },
      error: () => this.reportError.set('The HTML report could not be loaded.'),
    });
    this.reports.report(currentRun.id, 'json').subscribe({
      next: async (blob) => {
        this.jsonBlob = blob;
        const raw = await blob.text();
        try { this.jsonText.set(JSON.stringify(JSON.parse(raw), null, 2)); }
        catch { this.jsonText.set(raw); }
      },
      error: () => this.reportError.set('The JSON report could not be loaded.'),
    });
  }

  private downloadBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(url);
  }
}
