import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import { HeroCounter } from './hero.models';

@Component({
  selector: 'app-counter-list',
  imports: [DecimalPipe],
  templateUrl: './counter-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CounterList {
  readonly counters = input.required<HeroCounter[]>();
}
