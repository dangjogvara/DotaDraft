import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import {
  Observable,
  catchError,
  map,
  of,
  repeat,
  startWith,
  switchMap,
  takeWhile,
  tap,
} from 'rxjs';

import { shownAdvantage } from './advantage';
import { CounterList } from './counter-list';
import { HeroApiService } from './hero-api.service';
import { PrimaryAttr, filterHeroes, groupByAttribute, rolesOf } from './hero-search';
import { HeroFilters } from './hero-filters';
import { HeroGrid } from './hero-grid';
import { HeroScore, Hero, TeamMatchup } from './hero.models';
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

/** Stops polling after this many retries (5 minutes) so a permanently empty result doesn't poll forever. */
export const MAX_SEED_POLLS = 20;

/** Repeats a request every SEED_POLL_MS while it returns an empty list, up to MAX_SEED_POLLS retries. */
const pollWhileEmpty = <T>(request: Observable<T[]>): Observable<T[]> =>
  request.pipe(
    repeat({ count: MAX_SEED_POLLS + 1, delay: SEED_POLL_MS }),
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

  protected readonly heroes = signal<LoadState<Hero[]>>({ status: 'loading' });
  protected readonly enemies = signal<Hero[]>([]);
  protected readonly allies = signal<Hero[]>([]);
  protected readonly shownAdvantage = shownAdvantage;

  /** Which team a click on the grid adds to. */
  protected readonly addingTo = signal<Team>('enemy');

  protected readonly query = signal('');
  protected readonly attribute = signal<PrimaryAttr | null>(null);
  /** Filters both the grid and the counter suggestions. */
  protected readonly role = signal<string | null>(null);

  /** Incremented when polled counters transition from empty to non-empty, so team matchup refreshes after seeding. */
  private readonly matchupRefresh = signal(0);

  private readonly draft = computed<Draft>(() => ({ enemies: this.enemies(), allies: this.allies() }));
  private readonly counterQuery = computed(() => ({ draft: this.draft(), role: this.role() }));
  private readonly teamQuery = computed(() => ({ draft: this.draft(), refresh: this.matchupRefresh() }));

  // switchMap drops a slower response for an earlier draft or role when either changes again, and
  // stops polling for the old one. Counters need at least one enemy
  protected readonly counters = toSignal(
    toObservable(this.counterQuery).pipe(
      switchMap(({ draft, role }) => (draft.enemies.length === 0 ? of(null) : this.loadCounters(draft, role))),
    ),
    { initialValue: null },
  );

  /** How your team does against the enemy; null until both teams have picks. */
  protected readonly teamMatchup = toSignal(
    toObservable(this.teamQuery).pipe(
      switchMap(({ draft }) =>
        draft.enemies.length === 0 || draft.allies.length === 0 ? of(null) : this.loadTeamMatchup(draft),
      ),
    ),
    { initialValue: null },
  );

  protected readonly heroList = computed(() => loadedData(this.heroes()) ?? []);
  protected readonly counterList = computed(() => loadedData(this.counters()) ?? []);
  protected readonly enemyIds = computed(() => new Set(ids(this.enemies())));
  protected readonly allyIds = computed(() => new Set(ids(this.allies())));
  protected readonly addingToFull = computed(
    () => (this.addingTo() === 'enemy' ? this.enemies() : this.allies()).length >= MAX_LINEUP,
  );

  protected readonly roles = computed(() => rolesOf(this.heroList()));
  protected readonly heroGroups = computed(() =>
    groupByAttribute(filterHeroes(this.heroList(), this.query(), this.attribute(), this.role())),
  );

  constructor() {
    this.loadHeroes();
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

  protected readonly alliesFull = computed(() => this.allies().length >= MAX_LINEUP);

  /** Adds a hero from the grid to the team being picked for. */
  protected addHero(hero: Hero): void {
    this.addToTeam(this.addingTo(), hero);
  }

  /** Adds a suggested counter to your team, whichever team the grid is picking for. */
  protected pickCounter(counter: HeroScore): void {
    const hero = this.heroList().find((h) => h.id === counter.id);
    if (hero) this.addToTeam('ally', hero);
  }

  // Adds a hero to a team, unless either team already has it or that team is full
  private addToTeam(team: Team, hero: Hero): void {
    const lineup = team === 'enemy' ? this.enemies() : this.allies();
    if (this.enemyIds().has(hero.id) || this.allyIds().has(hero.id) || lineup.length >= MAX_LINEUP) return;

    this.setTeam(team, [...lineup, hero]);
    this.switchTeamIfFull();
  }

  // Moves the toggle to the other team once the selected one is full, if the other still has room,
  // so the next click on the grid isn't wasted. Removing heroes never switches
  private switchTeamIfFull(): void {
    const other: Team = this.addingTo() === 'enemy' ? 'ally' : 'enemy';
    const otherCount = (other === 'enemy' ? this.enemies() : this.allies()).length;
    if (this.addingToFull() && otherCount < MAX_LINEUP) this.addingTo.set(other);
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
    this.switchTeamIfFull();
  }

  private loadTeamMatchup({ enemies, allies }: Draft): Observable<LoadState<TeamMatchup>> {
    return this.heroApi.getTeamMatchup(ids(allies), ids(enemies)).pipe(
      map((data): LoadState<TeamMatchup> => ({ status: 'loaded', data })),
      catchError(() => of<LoadState<TeamMatchup>>({ status: 'error' })),
      startWith<LoadState<TeamMatchup>>({ status: 'loading' }),
    );
  }

  private loadCounters({ enemies, allies }: Draft, role: string | null): Observable<LoadState<HeroScore[]>> {
    const request = this.heroApi.getDraftCounters(ids(enemies), ids(allies), role);
    let sawEmpty = false;
    // Only poll unfiltered counters: a role filter with no results is usually legitimate, not seeding
    const stream =
      role === null
        ? pollWhileEmpty(request).pipe(
            tap((data) => {
              if (data.length === 0) sawEmpty = true;
              else if (sawEmpty) this.matchupRefresh.update((n) => n + 1);
            }),
          )
        : request;
    return stream.pipe(
      map((data): LoadState<HeroScore[]> => ({ status: 'loaded', data })),
      catchError(() => of<LoadState<HeroScore[]>>({ status: 'error' })),
      startWith<LoadState<HeroScore[]>>({ status: 'loading' }),
    );
  }
}
