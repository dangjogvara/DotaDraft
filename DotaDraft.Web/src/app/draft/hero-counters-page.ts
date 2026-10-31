import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable, Subject, catchError, map, of, startWith, switchMap } from 'rxjs';

import { CounterList } from './counter-list';
import { HeroApiService } from './hero-api.service';
import { PrimaryAttr, filterHeroes, groupByAttribute } from './hero-search';
import { HeroFilters } from './hero-filters';
import { HeroGrid } from './hero-grid';
import { DraftCounter, Hero } from './hero.models';
import { LINEUP_PARAM, lineupFromParam, lineupToParam } from './lineup-param';
import { LineupSlots, MAX_LINEUP } from './lineup-slots';

type LoadState<T> = { status: 'loading' } | { status: 'loaded'; data: T } | { status: 'error' };

const loadedData = <T>(state: LoadState<T> | null): T | undefined =>
  state?.status === 'loaded' ? state.data : undefined;

@Component({
  selector: 'app-hero-counters-page',
  imports: [HeroFilters, HeroGrid, LineupSlots, CounterList],
  templateUrl: './hero-counters-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HeroCountersPage {
  private readonly heroApi = inject(HeroApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly lineupChanges = new Subject<Hero[]>();

  protected readonly heroes = signal<LoadState<Hero[]>>({ status: 'loading' });
  protected readonly lineup = signal<Hero[]>([]);
  protected readonly counters = signal<LoadState<DraftCounter[]> | null>(null);

  protected readonly heroList = computed(() => loadedData(this.heroes()) ?? []);
  protected readonly counterList = computed(() => loadedData(this.counters()) ?? []);
  protected readonly pickedIds = computed(() => new Set(this.lineup().map((hero) => hero.id)));
  protected readonly lineupFull = computed(() => this.lineup().length >= MAX_LINEUP);

  protected readonly query = signal('');
  protected readonly attribute = signal<PrimaryAttr | null>(null);
  protected readonly heroGroups = computed(() =>
    groupByAttribute(filterHeroes(this.heroList(), this.query(), this.attribute())),
  );

  constructor() {
    this.loadHeroes();

    // switchMap drops a slower response for an earlier lineup when the lineup changes again
    this.lineupChanges
      .pipe(
        switchMap((lineup) => (lineup.length === 0 ? of(null) : this.loadCounters(lineup))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => this.counters.set(state));
  }

  protected loadHeroes(): void {
    this.heroes.set({ status: 'loading' });
    this.heroApi.getHeroes().subscribe({
      next: (data) => {
        this.heroes.set({ status: 'loaded', data });
        this.restoreLineup(data);
      },
      error: () => this.heroes.set({ status: 'error' }),
    });
  }

  protected addEnemy(hero: Hero): void {
    if (this.pickedIds().has(hero.id) || this.lineupFull()) return;
    this.setLineup([...this.lineup(), hero]);
  }

  protected removeEnemy(hero: Hero): void {
    this.setLineup(this.lineup().filter((h) => h.id !== hero.id));
  }

  protected clearLineup(): void {
    this.setLineup([]);
  }

  private setLineup(lineup: Hero[]): void {
    this.lineup.set(lineup);
    this.lineupChanges.next(lineup);

    // Keeps the lineup in the URL, so a refresh or a shared link opens the same draft.
    // replaceUrl stops every pick from adding a browser history entry
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { [LINEUP_PARAM]: lineupToParam(lineup) },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  // Heroes are needed to turn the URL's IDs into a lineup, so this runs once they've loaded
  private restoreLineup(heroes: Hero[]): void {
    if (this.lineup().length > 0) return;

    const lineup = lineupFromParam(this.route.snapshot.queryParamMap.get(LINEUP_PARAM), heroes);
    if (lineup.length > 0) this.setLineup(lineup);
  }

  private loadCounters(lineup: Hero[]): Observable<LoadState<DraftCounter[]>> {
    return this.heroApi.getDraftCounters(lineup.map((hero) => hero.id)).pipe(
      map((data): LoadState<DraftCounter[]> => ({ status: 'loaded', data })),
      catchError(() => of<LoadState<DraftCounter[]>>({ status: 'error' })),
      startWith<LoadState<DraftCounter[]>>({ status: 'loading' }),
    );
  }
}
