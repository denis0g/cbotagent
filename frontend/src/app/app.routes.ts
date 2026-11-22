import { Routes } from '@angular/router';
import { authGuard } from './Guards/auth-guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: 'login', loadComponent: () => import('./pages/login/login').then((m) => m.Login) },
  { path: 'register', loadComponent: () => import('./pages/register/register').then((m) => m.Register) },
  { path: 'agent', canActivate: [authGuard], loadComponent: () => import('./pages/agent/agent').then((m) => m.Agent) },
  { path: 'agent/:conversationId', canActivate: [authGuard], loadComponent: () => import('./pages/agent/agent').then((m) => m.Agent) },
  { path: 'cbots', canActivate: [authGuard], loadComponent: () => import('./pages/cbots/cbots').then((m) => m.Cbots) },
  { path: 'cbots/:botId', canActivate: [authGuard], loadComponent: () => import('./pages/cbot-detail/cbot-detail').then((m) => m.CbotDetail) },
  { path: 'backtest', canActivate: [authGuard], loadComponent: () => import('./pages/backtest/backtest').then((m) => m.Backtest) },
  { path: 'backtest/:botId', canActivate: [authGuard], loadComponent: () => import('./pages/backtest-results/backtest-results').then((m) => m.BacktestResults) },
  ...[
    ['deploy-live', 'Deploy live', 'Deploy strategies to your live trading account.'],
  ].map(([path, title, description]) => ({
    path, canActivate: [authGuard],
    loadComponent: () => import('./pages/feature/feature').then((m) => m.Feature),
    data: { title, description },
  })),
  { path: '**', redirectTo: 'login' },
];
