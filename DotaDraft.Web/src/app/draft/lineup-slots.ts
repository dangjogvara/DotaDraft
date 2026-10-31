import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { Hero } from './hero.models';

export const MAX_LINEUP = 5;

@Component({
  selector: 'app-lineup-slots',
  templateUrl: './lineup-slots.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LineupSlots {
  readonly lineup = input.required<Hero[]>();
  readonly removed = output<Hero>();
  readonly cleared = output<void>();

  // Pad to five slots so empty ones show where the next pick goes
  protected readonly slots = computed(() =>
    Array.from({ length: MAX_LINEUP }, (_, i) => this.lineup()[i] ?? null),
  );
}
