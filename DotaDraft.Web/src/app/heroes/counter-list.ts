import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { DraftCounter, Hero } from './hero.models';

/** Matchups with fewer games than this are flagged as less certain. */
export const FEW_GAMES = 200;

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

  protected enemyName(id: number): string {
    return this.enemies().get(id)?.localizedName ?? 'this hero';
  }

  protected isFewGames(games: number): boolean {
    return games < FEW_GAMES;
  }

  protected advantageLabel(advantage: number): string {
    if (advantage > 0) return 'better than usual';
    if (advantage < 0) return 'worse than usual';
    return 'same as usual';
  }
}
