import { ChangeDetectionStrategy, Component, model } from '@angular/core';

import { Team } from './team';

/** Chooses which team a click on the hero grid adds to. */
@Component({
  selector: 'app-team-toggle',
  templateUrl: './team-toggle.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamToggle {
  readonly team = model<Team>('enemy');

  protected readonly options: { value: Team; label: string; selectedClass: string }[] = [
    { value: 'enemy', label: 'Enemy team', selectedClass: 'bg-red-700 text-white' },
    { value: 'ally', label: 'My team', selectedClass: 'bg-emerald-700 text-white' },
  ];
}
