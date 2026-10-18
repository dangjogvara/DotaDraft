import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { environment } from '../../environments/environment';
import { HeroCountersPage } from './hero-counters-page';
import { Hero, HeroCounter } from './hero.models';

const heroesUrl = `${environment.apiBaseUrl}/api/heroes`;
const countersUrl = (id: number) => `${heroesUrl}/${id}/counters?limit=5`;

const hero = (id: number, localizedName: string, primaryAttr = 'agi'): Hero => ({
  id,
  name: `npc_dota_hero_${id}`,
  localizedName,
  primaryAttr,
  roles: [],
  imgUrl: `https://cdn.test/${id}.png`,
});

const antiMage = hero(1, 'Anti-Mage');
const axe = hero(2, 'Axe', 'str');

const weaver: HeroCounter = {
  id: 63,
  localizedName: 'Weaver',
  primaryAttr: 'agi',
  imgUrl: null,
  advantage: 12.4,
  winRate: 73.91,
  baselineWinRate: 45.34,
  gamesPlayed: 23,
};

describe('HeroCountersPage', () => {
  let fixture: ComponentFixture<HeroCountersPage>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HeroCountersPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(HeroCountersPage);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  const el = () => fixture.nativeElement as HTMLElement;
  const text = (selector: string) => el().querySelector(selector)?.textContent?.trim() ?? '';
  const heroButton = (name: string) =>
    [...el().querySelectorAll<HTMLButtonElement>('app-hero-grid button')].find((b) =>
      b.textContent?.includes(name),
    )!;

  function render() {
    fixture.detectChanges();
  }

  function loadHeroes(heroes: Hero[] = [antiMage, axe]) {
    http.expectOne(heroesUrl).flush(heroes);
    render();
  }

  function click(name: string) {
    heroButton(name).click();
    render();
  }

  it('shows a loading message, then the hero grid', () => {
    expect(text('[data-testid="heroes-loading"]')).toContain('Loading heroes');

    loadHeroes();

    expect(el().querySelectorAll('app-hero-grid button').length).toBe(2);
    expect(text('[data-testid="counters-panel"]')).toContain('Pick a hero');
  });

  it('shows an error with a working retry when heroes fail to load', () => {
    http.expectOne(heroesUrl).error(new ProgressEvent('error'));
    render();
    expect(text('[data-testid="heroes-error"]')).toContain('Could not load heroes');

    el().querySelector<HTMLButtonElement>('[data-testid="heroes-error"] button')!.click();
    render();
    loadHeroes();

    expect(el().querySelectorAll('app-hero-grid button').length).toBe(2);
  });

  it('explains how to seed when there are no heroes', () => {
    loadHeroes([]);

    expect(text('[data-testid="heroes-empty"]')).toContain('seed-heroes');
  });

  it('loads and shows counters for the selected hero', () => {
    loadHeroes();

    click('Anti-Mage');
    expect(heroButton('Anti-Mage').getAttribute('aria-pressed')).toBe('true');
    expect(text('[data-testid="counters-panel"]')).toContain('Loading counters');

    http.expectOne(countersUrl(1)).flush([weaver]);
    render();

    const panel = text('[data-testid="counters-panel"]');
    expect(panel).toContain('Counters to Anti-Mage');
    expect(panel).toContain('Weaver');
    expect(panel).toContain('+12.4');
  });

  it('ignores a slower response for a previously selected hero', () => {
    loadHeroes();

    click('Anti-Mage');
    const first = http.expectOne(countersUrl(1));
    click('Axe');
    http.expectOne(countersUrl(2)).flush([]);
    render();

    expect(first.cancelled).toBe(true);
    expect(text('[data-testid="counters-panel"]')).toContain('Counters to Axe');
  });

  it('shows an error when counters fail to load', () => {
    loadHeroes();

    click('Anti-Mage');
    http.expectOne(countersUrl(1)).error(new ProgressEvent('error'));
    render();

    expect(text('[data-testid="counters-panel"]')).toContain('Could not load counters');
  });

  it('explains when a hero has no matchup data', () => {
    loadHeroes();

    click('Anti-Mage');
    http.expectOne(countersUrl(1)).flush([]);
    render();

    expect(text('[data-testid="counters-panel"]')).toContain('No matchup data');
  });

  const visibleHeroes = () =>
    [...el().querySelectorAll('app-hero-grid button')].map((b) => b.textContent?.trim());

  function search(query: string) {
    const input = el().querySelector<HTMLInputElement>('app-hero-filters input')!;
    input.value = query;
    input.dispatchEvent(new Event('input'));
    render();
  }

  it('groups the grid by attribute in in-game order', () => {
    loadHeroes();

    const headings = [...el().querySelectorAll('section h2')].map((h) => h.textContent?.trim());
    expect(headings).toEqual(['Strength', 'Agility']);
    expect(visibleHeroes()).toEqual(['Axe', 'Anti-Mage']);
  });

  it('filters the grid by search and attribute', () => {
    loadHeroes();

    search('anti');
    expect(visibleHeroes()).toEqual(['Anti-Mage']);

    search('');
    [...el().querySelectorAll<HTMLButtonElement>('app-hero-filters button')]
      .find((b) => b.textContent?.trim() === 'Strength')!
      .click();
    render();
    expect(visibleHeroes()).toEqual(['Axe']);
  });

  it('says so when nothing matches', () => {
    loadHeroes();

    search('zzz');

    expect(visibleHeroes()).toEqual([]);
    expect(text('[data-testid="no-matches"]')).toContain('No heroes match');
  });

  it('keeps the selected hero and its counters while filtering', () => {
    loadHeroes();
    click('Anti-Mage');
    http.expectOne(countersUrl(1)).flush([weaver]);
    render();

    search('axe');

    expect(text('[data-testid="counters-panel"]')).toContain('Counters to Anti-Mage');
  });
});
