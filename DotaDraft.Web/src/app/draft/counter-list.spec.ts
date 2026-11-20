import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CounterList } from './counter-list';
import { HeroScore, Hero } from './hero.models';

const enemy = (id: number, localizedName: string): Hero => ({
  id,
  name: `npc_dota_hero_${id}`,
  localizedName,
  primaryAttr: 'str',
  roles: [],
  imgUrl: null,
});

describe('CounterList', () => {
  let fixture: ComponentFixture<CounterList>;

  const counter = (overrides: Partial<HeroScore>): HeroScore => ({
    id: 63,
    localizedName: 'Weaver',
    primaryAttr: 'agi',
    imgUrl: null,
    totalAdvantage: 12.4,
    baselineWinRate: 45.34,
    matchups: [{ enemyId: 1, advantage: 12.4, winRate: 73.91, expectedWinRate: 44.36, gamesPlayed: 23 }],
    ...overrides,
  });

  function render(counters: HeroScore[], lineup: Hero[] = [enemy(1, 'Anti-Mage')]) {
    fixture = TestBed.createComponent(CounterList);
    fixture.componentRef.setInput('counters', counters);
    fixture.componentRef.setInput('lineup', lineup);
    fixture.detectChanges();
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="counter"]')].map(
      (li) => li.textContent?.replace(/\s+/g, ' ').trim(),
    );
  }

  const text = (selector: string) =>
    [...(fixture.nativeElement as HTMLElement).querySelectorAll(selector)].map((el) =>
      el.textContent?.replace(/\s+/g, ' ').trim(),
    );

  it('names the enemy, the expected win rate, the games and the usual win rate for one enemy', () => {
    const [row] = render([
      counter({ matchups: [{ enemyId: 1, advantage: 12.4, winRate: 73.91, expectedWinRate: 44.36, gamesPlayed: 1841 }] }),
    ]);

    expect(row).toContain('Weaver');
    expect(row).toContain('+12.4');
    expect(text('[data-testid="advantage-label"]')).toEqual(['better than expected']);
    expect(text('[data-testid="matchup"]')).toEqual(['Wins 73.9% against Anti-Mage, expected 44.4% (1,841 games)']);
    expect(row).toContain('Usually wins 45.3%');
  });

  it('flags a matchup with few games', () => {
    render([counter({})]);

    expect(text('[data-testid="matchup"]')).toEqual(['Wins 73.9% against Anti-Mage, expected 44.4% (only 23 games)']);
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="matchup"] .text-amber-400')).not.toBeNull();
  });

  it('says so when there are no games against the enemy', () => {
    render([
      counter({ totalAdvantage: 0, matchups: [{ enemyId: 1, advantage: 0, winRate: null, expectedWinRate: null, gamesPlayed: 0 }] }),
    ]);

    expect(text('[data-testid="matchup"]')).toEqual(['No games against Anti-Mage yet']);
    expect(text('[data-testid="advantage-label"]')).toEqual(['as expected']);
  });

  it('shows a per-enemy breakdown for a lineup, flagging few games and missing data', () => {
    render(
      [
        counter({
          totalAdvantage: 9.5,
          matchups: [
            { enemyId: 1, advantage: 9.5, winRate: 68, expectedWinRate: 52.5, gamesPlayed: 1500 },
            { enemyId: 3, advantage: 2, winRate: 60, expectedWinRate: 51, gamesPlayed: 40 },
            { enemyId: 2, advantage: 0, winRate: null, expectedWinRate: null, gamesPlayed: 0 },
          ],
        }),
      ],
      [enemy(1, 'Anti-Mage'), enemy(2, 'Axe'), enemy(3, 'Lion')],
    );

    // The chip's parts are separate spans laid out with a flex gap, so join them the way they appear
    const chipEls = [...(fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="breakdown"]')];
    const chips = chipEls.map((c) => [...c.querySelectorAll('span')].map((s) => s.textContent?.trim()).join(' '));
    expect(chips).toEqual([
      'vs Anti-Mage +9.5 (1,500 games)',
      'vs Lion +2.0 (only 40 games)',
      'vs Axe no data',
    ]);
    expect(chipEls.map((c) => c.getAttribute('title'))).toEqual([
      'Wins 68.0% against Anti-Mage, expected 52.5% over 1,500 games',
      'Wins 60.0% against Lion, expected 51.0% over 40 games, too few to be sure',
      'No matchup data',
    ]);
    expect(text('[data-testid="advantage-label"]')).toEqual(['combined edge']);
  });

  it('shows a non-positive total without a plus sign', () => {
    const [row] = render([counter({ totalAdvantage: -1.25 })]);

    expect(row).toContain('-1.3');
    expect(row).not.toContain('+');
    expect(text('[data-testid="advantage-label"]')).toEqual(['worse than expected']);
  });

  it('colours positive advantages emerald, negative red and zero neutral', () => {
    render(
      [
        counter({
          id: 1,
          totalAdvantage: 5.5,
          matchups: [
            { enemyId: 1, advantage: 8, winRate: 60, expectedWinRate: 52, gamesPlayed: 500 },
            { enemyId: 2, advantage: -2.5, winRate: 45, expectedWinRate: 47.5, gamesPlayed: 500 },
            { enemyId: 3, advantage: 0, winRate: 50, expectedWinRate: 50, gamesPlayed: 500 },
          ],
        }),
        counter({ id: 2, totalAdvantage: -3.2, matchups: [{ enemyId: 1, advantage: -3.2, winRate: 46, expectedWinRate: 49.2, gamesPlayed: 500 }] }),
        counter({ id: 3, totalAdvantage: 0, matchups: [{ enemyId: 1, advantage: 0, winRate: 50, expectedWinRate: 50, gamesPlayed: 500 }] }),
      ],
      [enemy(1, 'Anti-Mage'), enemy(2, 'Axe'), enemy(3, 'Lion')],
    );

    const root = fixture.nativeElement as HTMLElement;
    const totals = [...root.querySelectorAll('[data-testid="counter"] [data-testid="advantage-value"]')];
    expect(totals[0].classList).toContain('text-emerald-400');
    expect(totals[1].classList).toContain('text-red-400');
    expect(totals[2].classList).toContain('text-slate-400');

    const chips = [...root.querySelectorAll('[data-testid="breakdown"] [data-testid="breakdown-advantage"]')];
    expect(chips[0].classList).toContain('text-emerald-400');
    expect(chips[1].classList).toContain('text-red-400');
    expect(chips[2].classList).toContain('text-slate-400');
  });

  it('words single-enemy tooltips for positive, negative, zero and missing advantages', () => {
    render([
      counter({ id: 1, localizedName: 'Weaver', totalAdvantage: 12.4, matchups: [{ enemyId: 1, advantage: 12.4, winRate: 73.9, expectedWinRate: 61.5, gamesPlayed: 500 }] }),
      counter({ id: 2, localizedName: 'Axe', totalAdvantage: -4.25, matchups: [{ enemyId: 1, advantage: -4.25, winRate: 45.7, expectedWinRate: 50, gamesPlayed: 500 }] }),
      counter({ id: 3, localizedName: 'Lion', totalAdvantage: 0, matchups: [{ enemyId: 1, advantage: 0, winRate: 50, expectedWinRate: 50, gamesPlayed: 500 }] }),
      counter({ id: 4, localizedName: 'Bane', totalAdvantage: 0, matchups: [{ enemyId: 1, advantage: 0, winRate: null, expectedWinRate: null, gamesPlayed: 0 }] }),
    ]);

    const titles = [...(fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="advantage"]')].map((el) =>
      el.getAttribute('title'),
    );
    expect(titles).toEqual([
      "Weaver wins 12.4 percentage points more often against Anti-Mage than both heroes' usual win rates predict, adjusted for how many games there are",
      "Axe wins 4.3 percentage points less often against Anti-Mage than both heroes' usual win rates predict, adjusted for how many games there are",
      "Lion wins as often against Anti-Mage as both heroes' usual win rates predict, adjusted for how many games there are",
      'No matchup data against Anti-Mage',
    ]);
  });

  describe('near-zero advantages', () => {
    // Advantages are shown with one decimal; sign, colour and wording must follow the number shown
    const single = (advantage: number) =>
      counter({
        totalAdvantage: advantage,
        matchups: [{ enemyId: 1, advantage, winRate: 50, expectedWinRate: 50, gamesPlayed: 500 }],
      });
    const valueEl = () => (fixture.nativeElement as HTMLElement).querySelector('[data-testid="advantage-value"]')!;
    const title = () =>
      (fixture.nativeElement as HTMLElement).querySelector('[data-testid="advantage"]')!.getAttribute('title');

    it.each([0.03, -0.03, 0.049, -0.049])('shows %s as a neutral 0.0, as expected', (advantage) => {
      render([single(advantage)]);

      expect(valueEl().textContent?.trim()).toBe('0.0');
      expect(valueEl().classList).toContain('text-slate-400');
      expect(valueEl().classList).not.toContain('text-emerald-400');
      expect(valueEl().classList).not.toContain('text-red-400');
      expect(text('[data-testid="advantage-label"]')).toEqual(['as expected']);
      expect(title()).toBe(
        "Weaver wins as often against Anti-Mage as both heroes' usual win rates predict, adjusted for how many games there are",
      );
    });

    it('shows 0.05 as +0.1, better than expected', () => {
      render([single(0.05)]);

      expect(valueEl().textContent?.trim()).toBe('+0.1');
      expect(valueEl().classList).toContain('text-emerald-400');
      expect(text('[data-testid="advantage-label"]')).toEqual(['better than expected']);
      expect(title()).toContain('wins 0.1 percentage points more often');
    });

    it('shows -0.05 as -0.1, worse than expected', () => {
      render([single(-0.05)]);

      expect(valueEl().textContent?.trim()).toBe('-0.1');
      expect(valueEl().classList).toContain('text-red-400');
      expect(text('[data-testid="advantage-label"]')).toEqual(['worse than expected']);
      expect(title()).toContain('wins 0.1 percentage points less often');
    });

    it('shows near-zero breakdown chips as a neutral 0.0', () => {
      render(
        [
          counter({
            totalAdvantage: 0.08,
            matchups: [
              { enemyId: 1, advantage: 0.03, winRate: 50, expectedWinRate: 50, gamesPlayed: 500 },
              { enemyId: 2, advantage: -0.03, winRate: 50, expectedWinRate: 50, gamesPlayed: 500 },
              { enemyId: 3, advantage: 0.08, winRate: 50, expectedWinRate: 50, gamesPlayed: 500 },
            ],
          }),
        ],
        [enemy(1, 'Anti-Mage'), enemy(2, 'Axe'), enemy(3, 'Lion')],
      );

      const chips = [
        ...(fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="breakdown-advantage"]'),
      ];
      expect(chips.map((c) => c.textContent?.trim())).toEqual(['0.0', '0.0', '+0.1']);
      expect(chips.map((c) => c.className.match(/text-(emerald|red|slate)-400/)?.[0])).toEqual([
        'text-slate-400',
        'text-slate-400',
        'text-emerald-400',
      ]);
    });
  });

  it('keeps the API order', () => {
    const rows = render([counter({ id: 1, localizedName: 'First' }), counter({ id: 2, localizedName: 'Second' })]);

    expect(rows[0]).toContain('First');
    expect(rows[1]).toContain('Second');
  });

  describe('pick buttons', () => {
    const pickButtons = () =>
      [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('[data-testid="pick-counter"]')];

    it("aren't shown unless the list is pickable", () => {
      render([counter({})]);

      expect(pickButtons()).toEqual([]);
    });

    it('emit the counter that was picked', () => {
      fixture = TestBed.createComponent(CounterList);
      fixture.componentRef.setInput('counters', [counter({}), counter({ id: 26, localizedName: 'Lion' })]);
      fixture.componentRef.setInput('lineup', [enemy(1, 'Anti-Mage')]);
      fixture.componentRef.setInput('pickable', true);
      const picked: HeroScore[] = [];
      fixture.componentInstance.picked.subscribe((c) => picked.push(c));
      fixture.detectChanges();

      expect(pickButtons().map((b) => b.getAttribute('aria-label'))).toEqual([
        'Pick Weaver for your team',
        'Pick Lion for your team',
      ]);
      pickButtons()[1].click();

      expect(picked.map((c) => c.id)).toEqual([26]);
    });

    it('are disabled when picking is', () => {
      fixture = TestBed.createComponent(CounterList);
      fixture.componentRef.setInput('counters', [counter({})]);
      fixture.componentRef.setInput('lineup', [enemy(1, 'Anti-Mage')]);
      fixture.componentRef.setInput('pickable', true);
      fixture.componentRef.setInput('pickDisabled', true);
      fixture.detectChanges();

      expect(pickButtons()[0].disabled).toBe(true);
      expect(pickButtons()[0].title).toBe('Your team is full');
    });
  });
});
