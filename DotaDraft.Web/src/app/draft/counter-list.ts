import { DecimalPipe, formatNumber } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  LOCALE_ID,
  booleanAttribute,
  computed,
  inject,
  input,
  output,
} from '@angular/core';

import { EnemyMatchup, HeroScore, Hero } from './hero.models';

/** Matchups with fewer games than this are flagged as less certain. */
export const FEW_GAMES = 200;

@Component({
  selector: 'app-counter-list',
  imports: [DecimalPipe],
  templateUrl: './counter-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CounterList {
  private readonly locale = inject(LOCALE_ID);

  readonly counters = input.required<HeroScore[]>();
  /** The enemy lineup, to label each counter's per-enemy breakdown. */
  readonly lineup = input.required<Hero[]>();
  /** Shows a button on each counter to pick it for your team. Off for lists of heroes that are already picked. */
  readonly pickable = input(false, { transform: booleanAttribute });
  /** Disables the pick buttons, e.g. when your team is full. */
  readonly pickDisabled = input(false);
  readonly picked = output<HeroScore>();

  protected readonly enemies = computed(
    () => new Map(this.lineup().map((hero) => [hero.id, hero])),
  );

  protected enemyName(id: number): string {
    return this.enemies().get(id)?.localizedName ?? 'this hero';
  }

  protected isFewGames(games: number): boolean {
    return games < FEW_GAMES;
  }

  protected advantageLabel(advantage: number): string {
    if (advantage > 0) return 'better than expected';
    if (advantage < 0) return 'worse than expected';
    return 'as expected';
  }

  protected advantageTitle(counter: HeroScore, single: EnemyMatchup | null): string {
    if (!single) {
      return `Sum of ${counter.localizedName}'s edge against each enemy, in percentage points`;
    }
    const enemy = this.enemyName(single.enemyId);
    if (single.winRate === null) {
      return `No matchup data against ${enemy}`;
    }
    if (counter.totalAdvantage === 0) {
      return `${counter.localizedName} wins as often against ${enemy} as both heroes' usual win rates predict, adjusted for how many games there are`;
    }
    const points = formatNumber(Math.abs(counter.totalAdvantage), this.locale, '1.1-1');
    const direction = counter.totalAdvantage > 0 ? 'more' : 'less';
    return `${counter.localizedName} wins ${points} percentage points ${direction} often against ${enemy} than both heroes' usual win rates predict, adjusted for how many games there are`;
  }

  protected breakdownTitle(m: EnemyMatchup): string {
    if (m.winRate === null) return 'No matchup data';
    const winRate = formatNumber(m.winRate, this.locale, '1.1-1');
    const expected =
      m.expectedWinRate !== null
        ? `, expected ${formatNumber(m.expectedWinRate, this.locale, '1.1-1')}%`
        : '';
    const games = formatNumber(m.gamesPlayed, this.locale);
    const caveat = this.isFewGames(m.gamesPlayed) ? ', too few to be sure' : '';
    return `Wins ${winRate}% against ${this.enemyName(m.enemyId)}${expected} over ${games} games${caveat}`;
  }
}
