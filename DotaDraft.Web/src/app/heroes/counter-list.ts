import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { DraftCounter, Hero } from './hero.models';

@Component({
  selector: 'app-counter-list',
  imports: [DecimalPipe],
  templateUrl: './counter-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CounterList {
  readonly counters = input.required<DraftCounter[]>();
  /** The enemy lineup, to label each counter's per-enemy breakdown. */
  readonly lineup = input.required<Hero[]>();

  protected readonly enemies = computed(() => new Map(this.lineup().map((hero) => [hero.id, hero])));
}
