import { Component, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { TopNav } from './components/top-nav/top-nav';

@Component({
  imports: [RouterOutlet, TopNav],
  selector: 'app-root',
  template: `
    <div class="flex h-dvh flex-col overflow-hidden bg-slate-950 text-white">
      <app-top-nav />
      <main
        class="relative flex min-h-0 w-full flex-1"
        [class.overflow-hidden]="!isCbotDetailRoute()"
        [class.overflow-y-auto]="isCbotDetailRoute()"
        [class.cbot-scrollbar]="isCbotDetailRoute()"
        [class.cbot-scrollbar-page]="isCbotDetailRoute()"
        [class.items-center]="!isAgentRoute() && !isCbotRoute() && !isBacktestRoute()"
        [class.justify-center]="!isAgentRoute() && !isCbotRoute() && !isBacktestRoute()"
      >
        <router-outlet />
      </main>
    </div>
  `,
})
export class App {
  private readonly router = inject(Router);
  readonly isAgentRoute = signal(this.router.url.split('?')[0].startsWith('/agent'));
  readonly isCbotRoute = signal(this.router.url.split('?')[0] === '/cbots' || this.router.url.split('?')[0].startsWith('/cbots/'));
  readonly isBacktestRoute = signal(this.router.url.split('?')[0] === '/backtest' || this.router.url.split('?')[0].startsWith('/backtest/'));
  readonly isCbotDetailRoute = signal(/^\/cbots\/[^/]+$/.test(this.router.url.split('?')[0]));

  constructor() {
    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)).subscribe((event) => {
      const url = (event as NavigationEnd).urlAfterRedirects.split('?')[0];
      this.isAgentRoute.set(url.startsWith('/agent'));
      this.isCbotRoute.set(url === '/cbots' || url.startsWith('/cbots/'));
      this.isBacktestRoute.set(url === '/backtest' || url.startsWith('/backtest/'));
      this.isCbotDetailRoute.set(/^\/cbots\/[^/]+$/.test(url));
    });
  }
}
