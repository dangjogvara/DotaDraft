import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';

import { CounterList } from './counter-list';
import { HeroApiService } from './hero-api.service';
import { PrimaryAttr, filterHeroes, groupByAttribute } from './hero-filter';
import { HeroFilters } from './hero-filters';
import { HeroGrid } from './hero-grid';
import { Hero, HeroCounter } from './hero.models';

type LoadState<T> = { status: 'loading' } | { status: 'loaded'; data: T } | { status: 'error' };

const loadedData = <T>(state: LoadState<T> | null): T | undefined =>
  state?.status === 'loaded' ? state.data : undefined;

@Component({
  selector: 'app-hero-counters-page',
  imports: [HeroFilters, HeroGrid, CounterList],
  templateUrl: './hero-counters-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HeroCountersPage {
  private readonly heroApi = inject(HeroApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly heroSelections = new Subject<Hero>();

  protected readonly heroes = signal<LoadState<Hero[]>>({ status: 'loading' });
  protected readonly selectedHero = signal<Hero | null>(null);
  protected readonly counters = signal<LoadState<HeroCounter[]> | null>(null);

  protected readonly heroList = computed(() => loadedData(this.heroes()) ?? []);

  protected readonly query = signal('');
  protected readonly attribute = signal<PrimaryAttr | null>(null);
  protected readonly heroGroups = computed(() =>
    groupByAttribute(filterHeroes(this.heroList(), this.query(), this.attribute())),
  );
  protected readonly counterList = computed(() => loadedData(this.counters()) ?? []);

  constructor() {
    this.loadHeroes();

    // switchMap drops a slower earlier response when the user clicks another hero
    this.heroSelections
      .pipe(
        switchMap((hero) =>
          this.heroApi.getCounters(hero.id).pipe(
            map((data): LoadState<HeroCounter[]> => ({ status: 'loaded', data })),
            catchError(() => of<LoadState<HeroCounter[]>>({ status: 'error' })),
            startWith<LoadState<HeroCounter[]>>({ status: 'loading' }),
          ),
        ),
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

  protected selectHero(hero: Hero): void {
    this.selectedHero.set(hero);
    this.heroSelections.next(hero);
  }
}
