import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { Hero } from './hero.models';

@Component({
  selector: 'app-hero-grid',
  templateUrl: './hero-grid.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HeroGrid {
  readonly heroes = input.required<Hero[]>();
  /** Heroes picked by either team: badged with their team and not selectable. */
  readonly enemyIds = input<ReadonlySet<number>>(new Set());
  readonly allyIds = input<ReadonlySet<number>>(new Set());
  /** When the team being picked for is full, no further heroes can be picked. */
  readonly full = input(false);
  readonly heroSelected = output<Hero>();
}
