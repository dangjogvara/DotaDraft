import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { Hero } from './hero.models';

@Component({
  selector: 'app-hero-grid',
  templateUrl: './hero-grid.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HeroGrid {
  readonly heroes = input.required<Hero[]>();
  readonly selectedHeroId = input<number | null>(null);
  readonly heroSelected = output<Hero>();
}
