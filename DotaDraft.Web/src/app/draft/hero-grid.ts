import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { Hero } from './hero.models';

@Component({
  selector: 'app-hero-grid',
  templateUrl: './hero-grid.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HeroGrid {
  readonly heroes = input.required<Hero[]>();
  /** Heroes already in the lineup: shown as picked and not selectable. */
  readonly pickedIds = input<ReadonlySet<number>>(new Set());
  /** When the lineup is full, no further heroes can be picked. */
  readonly full = input(false);
  readonly heroSelected = output<Hero>();
}
