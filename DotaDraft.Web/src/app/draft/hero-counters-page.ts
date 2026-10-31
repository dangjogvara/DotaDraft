import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import {
  Observable,
  Subject,
  catchError,
  map,
  of,
  repeat,
  startWith,
  switchMap,
  takeWhile,
} from 'rxjs';

import { CounterList } from './counter-list';
import { HeroApiService } from './hero-api.service';
import { PrimaryAttr, filterHeroes, groupByAttribute } from './hero-search';
import { HeroFilters } from './hero-filters';
import { HeroGrid } from './hero-grid';
import { DraftCounter, Hero, TeamMatchup } from './hero.models';
import { LINEUP_PARAM, TEAM_PARAM, lineupFromParam, lineupToParam } from './lineup-param';
import { LineupSlots, MAX_LINEUP } from './lineup-slots';
import { Team } from './team';
import { TeamToggle } from './team-toggle';

type LoadState<T> = { status: 'loading' } | { status: 'loaded'; data: T } | { status: 'error' };

/**
 * How often to ask again while the API has no data yet. On a fresh database it seeds in the
 * background for about 1.5 minutes after starting, so an empty answer usually means "not yet".
 */
export const SEED_POLL_MS = 15_000;

/** Repeats a request every SEED_POLL_MS while it returns an empty list, and stops at the first non-empty one. */
const pollWhileEmpty = <T>(request: Observable<T[]>): Observable<T[]> =>
  request.pipe(
    repeat({ delay: SEED_POLL_MS }),
    takeWhile((items) => items.length === 0, true),
  );

/** Both sides of the draft: the enemy lineup to counter, and your own team's picks. */
type Draft = { enemies: Hero[]; allies: Hero[] };

const ids = (heroes: Hero[]) => heroes.map((hero) => hero.id);

const loadedData = <T>(state: LoadState<T> | null): T | undefined =>
  state?.status === 'loaded' ? state.data : undefined;

@Component({
  selector: 'app-hero-counters-page',
  imports: [DecimalPipe, HeroFilters, HeroGrid, LineupSlots, TeamToggle, CounterList],
  templateUrl: './hero-counters-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HeroCountersPage {
  private readonly heroApi = inject(HeroApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly draftChanges = new Subject<Draft>();

  protected readonly heroes = signal<LoadState<Hero[]>>({ status: 'loading' });
  protected readonly enemies = signal<Hero[]>([]);
  protected readonly allies = signal<Hero[]>([]);
  /** Which team a click on the grid adds to. */
  protected readonly addingTo = signal<Team>('enemy');
  protected readonly counters = signal<LoadState<DraftCounter[]> | null>(null);
  /** How your team does against the enemy; null until both teams have picks. */
  protected readonly teamMatchup = signal<LoadState<TeamMatchup> | null>(null);

  protected readonly heroList = computed(() => loadedData(this.heroes()) ?? []);
  protected readonly counterList = computed(() => loadedData(this.counters()) ?? []);
  protected readonly enemyIds = computed(() => new Set(ids(this.enemies())));
  protected readonly allyIds = computed(() => new Set(ids(this.allies())));
  protected readonly addingToFull = computed(
    () => (this.addingTo() === 'enemy' ? this.enemies() : this.allies()).length >= MAX_LINEUP,
  );

  protected readonly query = signal('');
  protected readonly attribute = signal<PrimaryAttr | null>(null);
  protected readonly heroGroups = computed(() =>
    groupByAttribute(filterHeroes(this.heroList(), this.query(), this.attribute())),
  );

  constructor() {
    this.loadHeroes();

    // switchMap drops a slower response for an earlier draft when the draft changes again, and
    // stops polling for the old one. Counters need at least one enemy
    this.draftChanges
      .pipe(
        switchMap((draft) => (draft.enemies.length === 0 ? of(null) : this.loadCounters(draft))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => this.counters.set(state));

    this.draftChanges
      .pipe(
        switchMap((draft) =>
          draft.enemies.length === 0 || draft.allies.length === 0 ? of(null) : this.loadTeamMatchup(draft),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => this.teamMatchup.set(state));
  }

  protected loadHeroes(): void {
    this.heroes.set({ status: 'loading' });
    pollWhileEmpty(this.heroApi.getHeroes())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.heroes.set({ status: 'loaded', data });
          if (data.length > 0) this.restoreDraft(data);
        },
        error: () => this.heroes.set({ status: 'error' }),
      });
  }

  /** Adds a hero to the team being picked for, unless either team already has it or that team is full. */
  protected addHero(hero: Hero): void {
    if (this.enemyIds().has(hero.id) || this.allyIds().has(hero.id) || this.addingToFull()) return;

    if (this.addingTo() === 'enemy') this.setDraft({ enemies: [...this.enemies(), hero], allies: this.allies() });
    else this.setDraft({ enemies: this.enemies(), allies: [...this.allies(), hero] });
  }

  protected removeHero(team: Team, hero: Hero): void {
    const without = (heroes: Hero[]) => heroes.filter((h) => h.id !== hero.id);
    this.setTeam(team, without(team === 'enemy' ? this.enemies() : this.allies()));
  }

  protected clearTeam(team: Team): void {
    this.setTeam(team, []);
  }

  private setTeam(team: Team, heroes: Hero[]): void {
    this.setDraft(
      team === 'enemy' ? { enemies: heroes, allies: this.allies() } : { enemies: this.enemies(), allies: heroes },
    );
  }

  private setDraft(draft: Draft): void {
    this.enemies.set(draft.enemies);
    this.allies.set(draft.allies);
    this.draftChanges.next(draft);

    // Keeps the draft in the URL, so a refresh or a shared link opens the same draft.
    // replaceUrl stops every pick from adding a browser history entry
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        [LINEUP_PARAM]: lineupToParam(draft.enemies),
        [TEAM_PARAM]: lineupToParam(draft.allies),
      },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  // Heroes are needed to turn the URL's IDs into a draft, so this runs once they've loaded
  private restoreDraft(heroes: Hero[]): void {
    if (this.enemies().length > 0 || this.allies().length > 0) return;

    const params = this.route.snapshot.queryParamMap;
    const enemies = lineupFromParam(params.get(LINEUP_PARAM), heroes);
    // A hero can't be on both teams; in a hand-edited link the enemy side wins
    const enemyIds = new Set(ids(enemies));
    const allies = lineupFromParam(params.get(TEAM_PARAM), heroes).filter((hero) => !enemyIds.has(hero.id));

    if (enemies.length > 0 || allies.length > 0) this.setDraft({ enemies, allies });
  }

  private loadTeamMatchup({ enemies, allies }: Draft): Observable<LoadState<TeamMatchup>> {
    return this.heroApi.getTeamMatchup(ids(allies), ids(enemies)).pipe(
      map((data): LoadState<TeamMatchup> => ({ status: 'loaded', data })),
      catchError(() => of<LoadState<TeamMatchup>>({ status: 'error' })),
      startWith<LoadState<TeamMatchup>>({ status: 'loading' }),
    );
  }

  private loadCounters({ enemies, allies }: Draft): Observable<LoadState<DraftCounter[]>> {
    return pollWhileEmpty(this.heroApi.getDraftCounters(ids(enemies), ids(allies))).pipe(
      map((data): LoadState<DraftCounter[]> => ({ status: 'loaded', data })),
      catchError(() => of<LoadState<DraftCounter[]>>({ status: 'error' })),
      startWith<LoadState<DraftCounter[]>>({ status: 'loading' }),
    );
  }
}
