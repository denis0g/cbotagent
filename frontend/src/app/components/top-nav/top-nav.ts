import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../services/authService';

@Component({
  selector: 'app-top-nav',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './top-nav.html',
})
export class TopNav {
  readonly auth = inject(AuthService);
  readonly links = [
    { label: 'Agent', path: '/agent' },
    { label: 'Cbots', path: '/cbots' },
    { label: 'Backtest', path: '/backtest' },
    { label: 'Deploy live', path: '/deploy-live' },
  ];
}
