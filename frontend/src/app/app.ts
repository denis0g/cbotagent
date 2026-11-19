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
        class="relative flex min-h-0 w-full flex-1 overflow-hidden"
        [class.items-center]="!isAgentRoute()"
        [class.justify-center]="!isAgentRoute()"
      >
        <router-outlet />
      </main>
    </div>
  `,
})
export class App {
  private readonly router = inject(Router);
  readonly isAgentRoute = signal(this.router.url.split('?')[0].startsWith('/agent'));

  constructor() {
    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)).subscribe((event) => {
      this.isAgentRoute.set((event as NavigationEnd).urlAfterRedirects.split('?')[0].startsWith('/agent'));
    });
  }
}
