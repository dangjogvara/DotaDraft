import { PlatformLocation } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { environment } from '../../environments/environment';
import { HeroCountersPage, SEED_POLL_MS } from './hero-counters-page';
import { DraftCounter, Hero } from './hero.models';

const heroesUrl = `${environment.apiBaseUrl}/api/heroes`;
const draftCountersUrl = `${environment.apiBaseUrl}/api/counters`;

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
const lineupOfSix = [antiMage, axe, hero(3, 'Bane', 'int'), hero(4, 'Bloodseeker'), hero(5, 'Crystal Maiden', 'int'), hero(6, 'Drow Ranger')];

const weaver: DraftCounter = {
  id: 63,
  localizedName: 'Weaver',
  primaryAttr: 'agi',
  imgUrl: null,
  totalAdvantage: 12.4,
  baselineWinRate: 45.34,
  matchups: [{ enemyId: 1, advantage: 12.4, winRate: 73.91, gamesPlayed: 23 }],
};

describe('HeroCountersPage', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;

  // Opens the page through the router, as the app does, so it can read and update the URL
  async function open(url = '/') {
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, HeroCountersPage);
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '', component: HeroCountersPage }]),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const el = () => harness.routeNativeElement!;
  /** The URL after pending navigations have finished. */
  const url = async () => {
    await harness.fixture.whenStable();
    return TestBed.inject(Router).url;
  };
  const text = (selector: string) => el().querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const gridButton = (name: string) =>
    [...el().querySelectorAll<HTMLButtonElement>('app-hero-grid button')].find((b) =>
      b.textContent?.includes(name),
    )!;
  const visibleHeroes = () =>
    [...el().querySelectorAll('app-hero-grid button')].map((b) => b.textContent?.replace('Picked', '').trim());
  const slotNames = () =>
    [...el().querySelectorAll('[data-testid="slot"]')].map((s) => s.textContent?.replace('✕', '').trim());

  function render() {
    harness.detectChanges();
  }

  function loadHeroes(heroes: Hero[] = [antiMage, axe]) {
    http.expectOne(heroesUrl).flush(heroes);
    render();
  }

  function pick(name: string) {
    gridButton(name).click();
    render();
  }

  /** The pending combined-counters request, asserting which enemies it asks about. */
  function countersRequest(enemyIds: number[]): TestRequest {
    const req = http.expectOne((r) => r.url === draftCountersUrl);
    expect(req.request.params.getAll('against')).toEqual(enemyIds.map(String));
    return req;
  }

  function search(query: string) {
    const input = el().querySelector<HTMLInputElement>('app-hero-filters input')!;
    input.value = query;
    input.dispatchEvent(new Event('input'));
    render();
  }

  it('shows a loading message, then the hero grid and an empty lineup', async () => {
    await open();
    expect(text('[data-testid="heroes-loading"]')).toContain('Loading heroes');

    loadHeroes();

    expect(el().querySelectorAll('app-hero-grid button').length).toBe(2);
    expect(slotNames()).toEqual(['Empty', 'Empty', 'Empty', 'Empty', 'Empty']);
    expect(text('[data-testid="counters-panel"]')).toContain('Pick up to five enemy heroes');
  });

  it('puts the counters between the lineup and the hero grid, the order phones stack them in', async () => {
    await open();
    loadHeroes();

    const all = [...el().querySelectorAll('*')];
    const positions = [
      'app-lineup-slots',
      '[data-testid="counters-panel"]',
      'section[aria-label="Heroes"]',
    ].map((selector) => all.indexOf(el().querySelector(selector)!));
    expect(positions.every((position) => position >= 0)).toBe(true);
    expect([...positions].sort((a, b) => a - b)).toEqual(positions);
  });

  it('shows an error with a working retry when heroes fail to load', async () => {
    await open();
    http.expectOne(heroesUrl).error(new ProgressEvent('error'));
    render();
    expect(text('[data-testid="heroes-error"]')).toContain('Could not load heroes');

    el().querySelector<HTMLButtonElement>('[data-testid="heroes-error"] button')!.click();
    render();
    loadHeroes();

    expect(el().querySelectorAll('app-hero-grid button').length).toBe(2);
  });

  it('explains that heroes are still loading when there are none', async () => {
    await open();
    loadHeroes([]);

    expect(text('[data-testid="heroes-empty"]')).toContain('loads them from OpenDota');
    expect(text('[data-testid="heroes-empty"]')).not.toMatch(/seed/i);
  });

  it('adds a picked hero to the lineup and shows its counters', async () => {
    await open();
    loadHeroes();

    pick('Anti-Mage');
    expect(slotNames()[0]).toBe('Anti-Mage');
    expect(gridButton('Anti-Mage').disabled).toBe(true);
    expect(text('[data-testid="counters-panel"]')).toContain('Loading counters');

    countersRequest([1]).flush([weaver]);
    render();

    const panel = text('[data-testid="counters-panel"]');
    expect(panel).toContain('Counters to Anti-Mage');
    expect(panel).toContain('Weaver');
    expect(panel).toContain('+12.4');
  });

  it('asks for counters to the whole lineup, in pick order', async () => {
    await open();
    loadHeroes();

    pick('Axe');
    countersRequest([2]).flush([]);
    pick('Anti-Mage');
    countersRequest([2, 1]).flush([weaver]);
    render();

    expect(slotNames().slice(0, 2)).toEqual(['Axe', 'Anti-Mage']);
    expect(text('[data-testid="counters-panel"]')).toContain('Counters to the enemy lineup');
  });

  it('removes a hero from the lineup and makes it pickable again', async () => {
    await open();
    loadHeroes();
    pick('Anti-Mage');
    countersRequest([1]).flush([weaver]);
    pick('Axe');
    countersRequest([1, 2]).flush([]);

    el().querySelector<HTMLButtonElement>('[aria-label="Remove Anti-Mage"]')!.click();
    render();
    countersRequest([2]).flush([]);
    render();

    expect(slotNames()[0]).toBe('Axe');
    expect(gridButton('Anti-Mage').disabled).toBe(false);
  });

  it('clears the lineup without requesting counters', async () => {
    await open();
    loadHeroes();
    pick('Anti-Mage');
    countersRequest([1]).flush([weaver]);

    [...el().querySelectorAll<HTMLButtonElement>('app-lineup-slots button')]
      .find((b) => b.textContent?.trim() === 'Clear')!
      .click();
    render();

    expect(slotNames()).toEqual(['Empty', 'Empty', 'Empty', 'Empty', 'Empty']);
    expect(text('[data-testid="counters-panel"]')).toContain('Pick up to five enemy heroes');
  });

  it('stops at five enemies and disables the rest of the grid', async () => {
    await open();
    loadHeroes(lineupOfSix);

    lineupOfSix.slice(0, 5).forEach((h, i) => {
      pick(h.localizedName);
      countersRequest(lineupOfSix.slice(0, i + 1).map((x) => x.id)).flush([]);
    });
    render();

    expect(gridButton('Drow Ranger').disabled).toBe(true);
    expect(text('app-lineup-slots h2')).toContain('5/5');
  });

  it('ignores a slower response for an earlier lineup', async () => {
    await open();
    loadHeroes();

    pick('Anti-Mage');
    const first = countersRequest([1]);
    pick('Axe');
    countersRequest([1, 2]).flush([]);
    render();

    expect(first.cancelled).toBe(true);
    expect(text('[data-testid="counters-empty"]')).toContain('No matchup data');
    expect(text('[data-testid="counters-empty"]')).not.toMatch(/seed/i);
  });

  it('restores the lineup from the URL and loads its counters', async () => {
    await open('/?against=2,1');
    loadHeroes();

    expect(slotNames().slice(0, 2)).toEqual(['Axe', 'Anti-Mage']);
    countersRequest([2, 1]).flush([weaver]);
    render();
    expect(text('[data-testid="counters-panel"]')).toContain('Weaver');
  });

  it('drops unknown heroes from a restored URL', async () => {
    await open('/?against=99,1');
    loadHeroes();
    countersRequest([1]).flush([]);

    expect(slotNames()[0]).toBe('Anti-Mage');
    expect(await url()).toBe('/?against=1');
  });

  it('keeps the URL in step with the lineup, without adding history entries', async () => {
    await open();
    loadHeroes();
    // Tests use a fake browser location; pushState is what would add a history entry
    const pushState = vi.spyOn(TestBed.inject(PlatformLocation), 'pushState');

    pick('Axe');
    countersRequest([2]).flush([]);
    pick('Anti-Mage');
    countersRequest([2, 1]).flush([]);
    expect(await url()).toBe('/?against=2,1');

    el().querySelector<HTMLButtonElement>('[aria-label="Remove Axe"]')!.click();
    render();
    countersRequest([1]).flush([]);
    expect(await url()).toBe('/?against=1');

    [...el().querySelectorAll<HTMLButtonElement>('app-lineup-slots button')]
      .find((b) => b.textContent?.trim() === 'Clear')!
      .click();
    render();
    expect(await url()).toBe('/');
    expect(pushState).not.toHaveBeenCalled();
  });

  it('shows an error when counters fail to load', async () => {
    await open();
    loadHeroes();

    pick('Anti-Mage');
    countersRequest([1]).error(new ProgressEvent('error'));
    render();

    expect(text('[data-testid="counters-panel"]')).toContain('Could not load counters');
  });

  it('groups the grid by attribute in in-game order', async () => {
    await open();
    loadHeroes();

    const headings = [...el().querySelectorAll('section > h2')].map((h) => h.textContent?.trim());
    expect(headings).toEqual(['Strength', 'Agility']);
    expect(visibleHeroes()).toEqual(['Axe', 'Anti-Mage']);
  });

  it('filters the grid by search and attribute', async () => {
    await open();
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

  it('says so when nothing matches', async () => {
    await open();
    loadHeroes();

    search('zzz');

    expect(visibleHeroes()).toEqual([]);
    expect(text('[data-testid="no-matches"]')).toContain('No heroes match');
  });

  it('keeps the lineup and counters while filtering', async () => {
    await open();
    loadHeroes();
    pick('Anti-Mage');
    countersRequest([1]).flush([weaver]);
    render();

    search('axe');

    expect(slotNames()[0]).toBe('Anti-Mage');
    expect(text('[data-testid="counters-panel"]')).toContain('Weaver');
  });

  describe('while the API is still seeding', () => {
    // Enabled after the page has opened, since the router harness itself waits on real timers
    const useFakeTimers = () => vi.useFakeTimers();
    const waitPoll = () => vi.advanceTimersByTime(SEED_POLL_MS);

    afterEach(() => vi.useRealTimers());

    it('asks for heroes again until there are some, then stops', async () => {
      await open();
      useFakeTimers();
      loadHeroes([]);

      waitPoll();
      loadHeroes([]);
      expect(text('[data-testid="heroes-empty"]')).toContain('updates by itself');

      waitPoll();
      loadHeroes();
      expect(el().querySelectorAll('app-hero-grid button').length).toBe(2);

      waitPoll();
      http.expectNone(heroesUrl);
    });

    it('restores the lineup from the URL once heroes arrive', async () => {
      await open('/?against=1');
      useFakeTimers();
      loadHeroes([]);

      waitPoll();
      loadHeroes();

      expect(slotNames()[0]).toBe('Anti-Mage');
      countersRequest([1]).flush([weaver]);
    });

    it('asks for counters again until there are some, without flashing a loading message', async () => {
      await open();
      useFakeTimers();
      loadHeroes();
      pick('Anti-Mage');
      countersRequest([1]).flush([]);
      render();

      waitPoll();
      render();
      expect(text('[data-testid="counters-empty"]')).toContain('appear here by themselves');
      countersRequest([1]).flush([weaver]);
      render();
      expect(text('[data-testid="counters-panel"]')).toContain('Weaver');

      waitPoll();
      http.expectNone((r) => r.url === draftCountersUrl);
    });

    it('stops asking for an old lineup when the lineup changes', async () => {
      await open();
      useFakeTimers();
      loadHeroes();
      pick('Anti-Mage');
      countersRequest([1]).flush([]);

      pick('Axe');
      countersRequest([1, 2]).flush([weaver]);

      waitPoll();
      http.expectNone((r) => r.url === draftCountersUrl);
    });

    it('stops asking when the page is left', async () => {
      await open();
      useFakeTimers();
      loadHeroes([]);

      harness.fixture.destroy();
      waitPoll();

      http.expectNone(heroesUrl);
    });
  });
});
