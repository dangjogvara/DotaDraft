import { PlatformLocation } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { environment } from '../../environments/environment';
import { HeroCountersPage, MAX_SEED_POLLS, SEED_POLL_MS } from './hero-counters-page';
import { HeroScore, Hero } from './hero.models';

const heroesUrl = `${environment.apiBaseUrl}/api/heroes`;
const draftCountersUrl = `${environment.apiBaseUrl}/api/counters`;
const teamMatchupUrl = `${environment.apiBaseUrl}/api/team-matchup`;

const hero = (id: number, localizedName: string, primaryAttr = 'agi', roles: string[] = []): Hero => ({
  id,
  name: `npc_dota_hero_${id}`,
  localizedName,
  primaryAttr,
  roles,
  imgUrl: `https://cdn.test/${id}.png`,
});

const antiMage = hero(1, 'Anti-Mage');
const axe = hero(2, 'Axe', 'str');
const lineupOfSix = [antiMage, axe, hero(3, 'Bane', 'int'), hero(4, 'Bloodseeker'), hero(5, 'Crystal Maiden', 'int'), hero(6, 'Drow Ranger')];

const weaver: HeroScore = {
  id: 63,
  localizedName: 'Weaver',
  primaryAttr: 'agi',
  imgUrl: null,
  totalAdvantage: 12.4,
  baselineWinRate: 45.34,
  matchups: [{ enemyId: 1, advantage: 12.4, winRate: 73.91, expectedWinRate: 44.36, gamesPlayed: 23 }],
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
    [...el().querySelectorAll('app-hero-grid button')].map((b) =>
      b.textContent?.replace(/Enemy|Ally/, '').trim(),
    );
  const slotNames = (team: 'enemy' | 'ally' = 'enemy') =>
    [...el().querySelectorAll(`[data-testid="${team}-lineup"] [data-testid="slot"]`)].map((s) =>
      s.textContent?.replace('✕', '').trim(),
    );
  const badge = (name: string) => gridButton(name).querySelector('span.absolute')?.textContent?.trim();

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

  /** The pending combined-counters request, asserting which enemies it asks about and which heroes it excludes. */
  function countersRequest(enemyIds: number[], excludeIds: number[] = []): TestRequest {
    const req = http.expectOne((r) => r.url === draftCountersUrl);
    expect(req.request.params.getAll('against')).toEqual(enemyIds.map(String));
    expect(req.request.params.getAll('exclude') ?? []).toEqual(excludeIds.map(String));
    return req;
  }

  /** The pending team-matchup request, asserting which heroes are on each team. */
  function teamRequest(teamIds: number[], enemyIds: number[]): TestRequest {
    const req = http.expectOne((r) => r.url === teamMatchupUrl);
    expect(req.request.params.getAll('team')).toEqual(teamIds.map(String));
    expect(req.request.params.getAll('against')).toEqual(enemyIds.map(String));
    return req;
  }

  function addPicksTo(team: 'Enemy team' | 'My team') {
    [...el().querySelectorAll<HTMLButtonElement>('app-team-toggle button')]
      .find((b) => b.textContent?.trim() === team)!
      .click();
    render();
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

  it('puts the lineups and team toggle first, then the counters, then the hero grid: the order phones stack them in', async () => {
    await open();
    loadHeroes();

    const all = [...el().querySelectorAll('*')];
    const positions = [
      '[data-testid="enemy-lineup"]',
      '[data-testid="ally-lineup"]',
      'app-team-toggle',
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

    el().querySelector<HTMLButtonElement>('[aria-label="Clear Enemy lineup"]')!.click();
    render();

    expect(slotNames()).toEqual(['Empty', 'Empty', 'Empty', 'Empty', 'Empty']);
    expect(text('[data-testid="counters-panel"]')).toContain('Pick up to five enemy heroes');
  });

  it('stops at five enemies: with the enemy team selected, the rest of the grid is disabled', async () => {
    await open();
    loadHeroes(lineupOfSix);

    lineupOfSix.slice(0, 5).forEach((h, i) => {
      pick(h.localizedName);
      countersRequest(lineupOfSix.slice(0, i + 1).map((x) => x.id)).flush([]);
    });
    // The toggle moves to your team by itself; switch back to check the full enemy lineup
    addPicksTo('Enemy team');

    expect(gridButton('Drow Ranger').disabled).toBe(true);
    expect(gridButton('Drow Ranger').title).toBe('That team is full');
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

    el().querySelector<HTMLButtonElement>('[aria-label="Clear Enemy lineup"]')!.click();
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

  describe('your own team', () => {
    it('adds picks to your team after switching the toggle, without asking for counters', async () => {
      await open();
      loadHeroes();

      addPicksTo('My team');
      pick('Axe');

      expect(slotNames('ally')[0]).toBe('Axe');
      expect(slotNames('enemy')[0]).toBe('Empty');
      expect(badge('Axe')).toBe('Ally');
      expect(gridButton('Axe').disabled).toBe(true);
      expect(text('[data-testid="counters-panel"]')).toContain('Pick up to five enemy heroes');
    });

    it("excludes your team's picks from the counters", async () => {
      await open();
      loadHeroes();
      pick('Anti-Mage');
      countersRequest([1]).flush([weaver]);

      addPicksTo('My team');
      pick('Axe');
      countersRequest([1], [2]).flush([]);
      teamRequest([2], [1]).flush({ totalAdvantage: 0, heroes: [] });
      render();
      expect(badge('Anti-Mage')).toBe('Enemy');

      el().querySelector<HTMLButtonElement>('[aria-label="Remove Axe"]')!.click();
      render();
      countersRequest([1]).flush([]);
    });

    it('keeps a hero to one team', async () => {
      await open();
      loadHeroes();
      pick('Anti-Mage');
      countersRequest([1]).flush([]);

      addPicksTo('My team');
      gridButton('Anti-Mage').click();
      render();

      expect(slotNames('ally')[0]).toBe('Empty');
    });

    it('fills each team separately', async () => {
      await open();
      loadHeroes(lineupOfSix);
      addPicksTo('My team');
      for (const h of lineupOfSix.slice(0, 4)) pick(h.localizedName);
      pick(lineupOfSix[4].localizedName);

      // Your team is full and the toggle has moved on, so the last hero goes to the enemy
      expect(gridButton('Drow Ranger').disabled).toBe(false);
      pick('Drow Ranger');
      countersRequest([6], [1, 2, 3, 4, 5]).flush([]);
      teamRequest([1, 2, 3, 4, 5], [6]).flush({ totalAdvantage: 0, heroes: [] });
    });

    it('keeps both teams in the URL', async () => {
      await open();
      loadHeroes();
      pick('Anti-Mage');
      countersRequest([1]).flush([]);
      addPicksTo('My team');
      pick('Axe');
      countersRequest([1], [2]).flush([]);
      teamRequest([2], [1]).flush({ totalAdvantage: 0, heroes: [] });

      expect(await url()).toBe('/?against=1&team=2');
    });

    it('restores both teams from the URL', async () => {
      await open('/?against=1&team=2');
      loadHeroes();

      expect(slotNames('enemy')[0]).toBe('Anti-Mage');
      expect(slotNames('ally')[0]).toBe('Axe');
      countersRequest([1], [2]).flush([]);
      teamRequest([2], [1]).flush({ totalAdvantage: 0, heroes: [] });
    });

    it('puts a hero that a link lists on both teams with the enemy', async () => {
      await open('/?against=1&team=1,2');
      loadHeroes();

      expect(slotNames('enemy')[0]).toBe('Anti-Mage');
      expect(slotNames('ally').slice(0, 2)).toEqual(['Axe', 'Empty']);
      countersRequest([1], [2]).flush([]);
      teamRequest([2], [1]).flush({ totalAdvantage: 0, heroes: [] });
    });
  });

  describe('filtering by role', () => {
    const carry = hero(1, 'Anti-Mage', 'agi', ['Carry', 'Escape']);
    const support = hero(5, 'Crystal Maiden', 'int', ['Support', 'Disabler']);
    const initiator = hero(2, 'Axe', 'str', ['Initiator']);

    function chooseRole(role: string) {
      [...el().querySelectorAll<HTMLButtonElement>('[aria-label="Filter by role"] button')]
        .find((b) => b.textContent?.trim() === role)!
        .click();
      render();
    }

    /** The pending counters request, asserting its role (null for none). */
    function countersWithRole(role: string | null): TestRequest {
      const req = http.expectOne((r) => r.url === draftCountersUrl);
      expect(req.request.params.get('role')).toBe(role);
      return req;
    }

    it("offers the heroes' roles and filters the grid", async () => {
      await open();
      loadHeroes([carry, support, initiator]);

      expect(
        [...el().querySelectorAll('[aria-label="Filter by role"] button')].map((b) => b.textContent?.trim()),
      ).toEqual(['Carry', 'Disabler', 'Escape', 'Initiator', 'Support']);

      chooseRole('Support');
      expect(visibleHeroes()).toEqual(['Crystal Maiden']);

      chooseRole('Support');
      expect(visibleHeroes()).toEqual(['Axe', 'Anti-Mage', 'Crystal Maiden']);
    });

    it('asks for counters with the role, and again without it, without asking for the team matchup', async () => {
      await open('/?against=2&team=1');
      loadHeroes([carry, support, initiator]);
      countersWithRole(null).flush([weaver]);
      teamRequest([1], [2]).flush({ totalAdvantage: 0, heroes: [] });

      chooseRole('Support');
      countersWithRole('Support').flush([weaver]);
      render();
      expect(text('[data-testid="counters-heading"]')).toBe('Support counters to Axe');

      chooseRole('Support');
      countersWithRole(null).flush([weaver]);
      render();
      expect(text('[data-testid="counters-heading"]')).toBe('Counters to Axe');
      http.expectNone((r) => r.url === teamMatchupUrl);
    });

    it('keeps the role when the lineup changes', async () => {
      await open();
      loadHeroes([carry, support, initiator]);
      chooseRole('Support');

      // The grid only shows supports now
      pick('Crystal Maiden');
      countersWithRole('Support').flush([]);
      render();

      expect(text('[data-testid="counters-empty"]')).toBe('No Support heroes have matchup data against this lineup.');
    });

    it("doesn't ask for counters without an enemy", async () => {
      await open();
      loadHeroes([carry, support, initiator]);

      chooseRole('Support');

      http.expectNone((r) => r.url === draftCountersUrl);
    });
  });

  describe('picking a counter', () => {
    const weaverHero = hero(63, 'Weaver');
    const pickButton = (name: string) =>
      el().querySelector<HTMLButtonElement>(`[data-testid="counters-panel"] [aria-label="Pick ${name} for your team"]`);

    it('adds it to your team, even while the grid picks enemies, and updates the URL', async () => {
      await open();
      loadHeroes([antiMage, axe, weaverHero]);
      pick('Anti-Mage');
      countersRequest([1]).flush([weaver]);
      render();

      pickButton('Weaver')!.click();
      render();

      expect(slotNames('ally')[0]).toBe('Weaver');
      expect(badge('Weaver')).toBe('Ally');
      countersRequest([1], [63]).flush([]);
      teamRequest([63], [1]).flush({ totalAdvantage: 0, heroes: [] });
      expect(await url()).toBe('/?against=1&team=63');
    });

    it("isn't offered for your team's own heroes in the matchup", async () => {
      await open();
      loadHeroes([antiMage, axe, weaverHero]);
      pick('Anti-Mage');
      countersRequest([1]).flush([]);
      addPicksTo('My team');
      pick('Weaver');
      countersRequest([1], [63]).flush([]);
      teamRequest([63], [1]).flush({ totalAdvantage: 12.4, heroes: [weaver] });
      render();

      expect(text('[data-testid="team-matchup"]')).toContain('Weaver');
      expect(el().querySelector('[data-testid="team-matchup"] [data-testid="pick-counter"]')).toBeNull();
    });

    it('is disabled once your team is full', async () => {
      const allies = [axe, hero(3, 'Bane', 'int'), hero(4, 'Bloodseeker'), hero(5, 'Crystal Maiden', 'int'), hero(6, 'Drow Ranger')];
      await open('/?against=1&team=2,3,4,5,6');
      loadHeroes([antiMage, ...allies, weaverHero]);
      countersRequest([1], [2, 3, 4, 5, 6]).flush([weaver]);
      teamRequest([2, 3, 4, 5, 6], [1]).flush({ totalAdvantage: 0, heroes: [] });
      render();

      expect(pickButton('Weaver')!.disabled).toBe(true);
    });
  });

  describe('switching teams when one is full', () => {
    const tenHeroes = Array.from({ length: 10 }, (_, i) => hero(i + 1, `Hero ${i + 1}`));
    const selectedTeam = () =>
      el().querySelector('app-team-toggle button[aria-pressed="true"]')?.textContent?.trim();
    /** Picks heroes for the selected team, answering every request they cause. */
    function pickAll(names: string[]) {
      for (const name of names) {
        pick(name);
        http.match(() => true).forEach((req) => req.flush(req.request.url === teamMatchupUrl ? { totalAdvantage: 0, heroes: [] } : []));
        render();
      }
    }
    const names = (from: number, to: number) => tenHeroes.slice(from - 1, to).map((h) => h.localizedName);

    it('switches to your team once the enemy lineup is full', async () => {
      await open();
      loadHeroes(tenHeroes);

      pickAll(names(1, 4));
      expect(selectedTeam()).toBe('Enemy team');
      pickAll(names(5, 5));

      expect(selectedTeam()).toBe('My team');
      pickAll(names(6, 6));
      expect(slotNames('ally')[0]).toBe('Hero 6');
    });

    it('switches to the enemy team once your team is full', async () => {
      await open();
      loadHeroes(tenHeroes);
      addPicksTo('My team');

      pickAll(names(1, 5));

      expect(selectedTeam()).toBe('Enemy team');
    });

    it('stays put when both teams are full', async () => {
      await open();
      loadHeroes(tenHeroes);
      addPicksTo('My team');
      pickAll(names(1, 5));

      pickAll(names(6, 10));

      expect(selectedTeam()).toBe('Enemy team');
    });

    it("doesn't switch back when a hero is removed, and can still be switched by hand", async () => {
      await open();
      loadHeroes(tenHeroes);
      pickAll(names(1, 5));

      el().querySelector<HTMLButtonElement>('[aria-label="Remove Hero 1"]')!.click();
      render();
      http.match(() => true).forEach((req) => req.flush([]));
      expect(selectedTeam()).toBe('My team');

      addPicksTo('Enemy team');
      expect(selectedTeam()).toBe('Enemy team');
    });

    it('starts on your team when a link already has five enemies', async () => {
      await open('/?against=1,2,3,4,5');
      loadHeroes(tenHeroes);
      countersRequest([1, 2, 3, 4, 5]).flush([]);

      expect(selectedTeam()).toBe('My team');
    });
  });

  describe("your team's matchup", () => {
    const axeScored: HeroScore = {
      id: 2,
      localizedName: 'Axe',
      primaryAttr: 'str',
      imgUrl: null,
      totalAdvantage: 4.52,
      baselineWinRate: 50.1,
      matchups: [{ enemyId: 1, advantage: 4.52, winRate: 55.3, expectedWinRate: 50.1, gamesPlayed: 1200 }],
    };
    const panel = () => text('[data-testid="team-matchup"]');

    /** Anti-Mage as the enemy and Axe on your team, with the counters answered. */
    async function draftWithBothTeams() {
      await open();
      loadHeroes();
      pick('Anti-Mage');
      countersRequest([1]).flush([]);
      addPicksTo('My team');
      pick('Axe');
      countersRequest([1], [2]).flush([]);
    }

    it('only appears once both teams have picks', async () => {
      await open();
      loadHeroes();
      addPicksTo('My team');
      pick('Axe');
      expect(el().querySelector('[data-testid="team-matchup"]')).toBeNull();

      addPicksTo('Enemy team');
      pick('Anti-Mage');
      countersRequest([1], [2]).flush([]);
      teamRequest([2], [1]);
      render();
      expect(panel()).toContain("Loading your team's matchup");
    });

    it("shows the team's combined edge and each hero's breakdown", async () => {
      await draftWithBothTeams();
      teamRequest([2], [1]).flush({ totalAdvantage: 4.52, heroes: [axeScored] });
      render();

      expect(panel()).toContain("Your team's matchup");
      expect(text('[data-testid="team-total"]')).toBe('+4.5 combined edge');
      expect(panel()).toContain('Axe');
      expect(panel()).toContain('Wins 55.3% against Anti-Mage');
      expect(panel()).toContain("isn't a win prediction");
    });

    it('colours a negative total red', async () => {
      await draftWithBothTeams();
      teamRequest([2], [1]).flush({ totalAdvantage: -2.25, heroes: [{ ...axeScored, totalAdvantage: -2.25 }] });
      render();

      const total = el().querySelector('[data-testid="team-total"] span')!;
      expect(total.textContent?.trim()).toBe('-2.3');
      expect(total.classList).toContain('text-red-400');
    });

    it.each([0.03, -0.03])('shows a near-zero total of %s as a neutral 0.0', async (total) => {
      await draftWithBothTeams();
      teamRequest([2], [1]).flush({ totalAdvantage: total, heroes: [{ ...axeScored, totalAdvantage: total }] });
      render();

      const value = el().querySelector('[data-testid="team-total"] span')!;
      expect(value.textContent?.trim()).toBe('0.0');
      expect(value.classList).toContain('text-slate-400');
      expect(value.classList).not.toContain('text-emerald-400');
      expect(value.classList).not.toContain('text-red-400');
    });

    it('asks again when either team changes, dropping the outdated answer', async () => {
      await open();
      loadHeroes(lineupOfSix);
      pick('Anti-Mage');
      countersRequest([1]).flush([]);
      addPicksTo('My team');
      pick('Axe');
      countersRequest([1], [2]).flush([]);
      const first = teamRequest([2], [1]);

      pick('Bane');
      countersRequest([1], [2, 3]).flush([]);

      expect(first.cancelled).toBe(true);
      teamRequest([2, 3], [1]).flush({ totalAdvantage: 0, heroes: [] });
    });

    it('disappears when your team is cleared', async () => {
      await draftWithBothTeams();
      teamRequest([2], [1]).flush({ totalAdvantage: 4.52, heroes: [axeScored] });

      el().querySelector<HTMLButtonElement>('[aria-label="Clear Your team"]')!.click();
      render();
      countersRequest([1]).flush([]);

      expect(el().querySelector('[data-testid="team-matchup"]')).toBeNull();
    });

    it('shows an error when it fails to load', async () => {
      await draftWithBothTeams();
      teamRequest([2], [1]).error(new ProgressEvent('error'));
      render();

      expect(panel()).toContain("Could not load your team's matchup");
    });
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

    it('does not poll when a role filter has no counters', async () => {
      const support = hero(5, 'Crystal Maiden', 'int', ['Support']);
      await open();
      useFakeTimers();
      loadHeroes([antiMage, support]);

      [...el().querySelectorAll<HTMLButtonElement>('[aria-label="Filter by role"] button')]
        .find((b) => b.textContent?.trim() === 'Support')!
        .click();
      render();
      pick('Crystal Maiden');
      countersRequest([5]).flush([]);
      render();

      waitPoll();
      http.expectNone((r) => r.url === draftCountersUrl);
    });

    it('stops polling after MAX_SEED_POLLS empty counter responses', async () => {
      await open();
      useFakeTimers();
      loadHeroes();
      pick('Anti-Mage');
      countersRequest([1]).flush([]);

      for (let i = 0; i < MAX_SEED_POLLS; i++) {
        waitPoll();
        countersRequest([1]).flush([]);
      }

      waitPoll();
      http.expectNone((r) => r.url === draftCountersUrl);
    });

    it("refreshes your team's matchup once counters arrive after seeding", async () => {
      const axeEmpty: HeroScore = {
        id: 2,
        localizedName: 'Axe',
        primaryAttr: 'str',
        imgUrl: null,
        totalAdvantage: 0,
        baselineWinRate: 50,
        matchups: [{ enemyId: 1, advantage: 0, winRate: null, expectedWinRate: null, gamesPlayed: 0 }],
      };
      const axeScored: HeroScore = {
        ...axeEmpty,
        totalAdvantage: 4.52,
        baselineWinRate: 50.1,
        matchups: [{ enemyId: 1, advantage: 4.52, winRate: 55.3, expectedWinRate: 50.1, gamesPlayed: 1200 }],
      };

      await open('/?against=1&team=2');
      useFakeTimers();
      loadHeroes();
      countersRequest([1], [2]).flush([]);
      teamRequest([2], [1]).flush({ totalAdvantage: 0, heroes: [axeEmpty] });
      render();
      expect(text('[data-testid="team-total"]')).toBe('0.0 combined edge');

      waitPoll();
      countersRequest([1], [2]).flush([weaver]);
      render();

      teamRequest([2], [1]).flush({ totalAdvantage: 4.52, heroes: [axeScored] });
      render();
      expect(text('[data-testid="team-total"]')).toBe('+4.5 combined edge');

      waitPoll();
      http.expectNone(() => true);
    });
  });
});
