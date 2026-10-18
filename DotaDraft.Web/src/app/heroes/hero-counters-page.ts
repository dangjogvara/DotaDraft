import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, catchError, map, of, startWith, switchMap } from 'rxjs';

import { CounterList } from './counter-list';
import { HeroApiService } from './hero-api.service';
import { PrimaryAttr, filterHeroes, groupByAttribute } from './hero-filter';
import { HeroFilters } from './hero-filters';
import { HeroGrid } from './hero-grid';
import { DraftCounter, Hero } from './hero.models';
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
      next: (data) => this.heroes.set({ status: 'loaded', data }),
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
  }

  private loadCounters(lineup: Hero[]): Observable<LoadState<DraftCounter[]>> {
    return this.heroApi.getDraftCounters(lineup.map((hero) => hero.id)).pipe(
      map((data): LoadState<DraftCounter[]> => ({ status: 'loaded', data })),
      catchError(() => of<LoadState<DraftCounter[]>>({ status: 'error' })),
      startWith<LoadState<DraftCounter[]>>({ status: 'loading' }),
    );
  }
}
