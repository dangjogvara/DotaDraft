import { Routes } from '@angular/router';

import { HeroCountersPage } from './draft/hero-counters-page';

export const routes: Routes = [
  { path: '', component: HeroCountersPage },
  { path: '**', redirectTo: '' },
];
