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
    matchups: [{ enemyId: 1, advantage: 12.4, winRate: 73.91, gamesPlayed: 23 }],
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

  it('names the enemy, the games and the usual win rate for one enemy', () => {
    const [row] = render([counter({ matchups: [{ enemyId: 1, advantage: 12.4, winRate: 73.91, gamesPlayed: 1841 }] })]);

    expect(row).toContain('Weaver');
    expect(row).toContain('+12.4');
    expect(text('[data-testid="advantage-label"]')).toEqual(['better than expected']);
    expect(text('[data-testid="matchup"]')).toEqual(['Wins 73.9% against Anti-Mage (1,841 games)']);
    expect(row).toContain('Usually wins 45.3%');
  });

  it('flags a matchup with few games', () => {
    render([counter({})]);

    expect(text('[data-testid="matchup"]')).toEqual(['Wins 73.9% against Anti-Mage (only 23 games)']);
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="matchup"] .text-amber-400')).not.toBeNull();
  });

  it('says so when there are no games against the enemy', () => {
    render([counter({ totalAdvantage: 0, matchups: [{ enemyId: 1, advantage: 0, winRate: null, gamesPlayed: 0 }] })]);

    expect(text('[data-testid="matchup"]')).toEqual(['No games against Anti-Mage yet']);
    expect(text('[data-testid="advantage-label"]')).toEqual(['as expected']);
  });

  it('shows a per-enemy breakdown for a lineup, flagging few games and missing data', () => {
    render(
      [
        counter({
          totalAdvantage: 9.5,
          matchups: [
            { enemyId: 1, advantage: 9.5, winRate: 68, gamesPlayed: 1500 },
            { enemyId: 3, advantage: 2, winRate: 60, gamesPlayed: 40 },
            { enemyId: 2, advantage: 0, winRate: null, gamesPlayed: 0 },
          ],
        }),
      ],
      [enemy(1, 'Anti-Mage'), enemy(2, 'Axe'), enemy(3, 'Lion')],
    );

    // The chip's parts are separate spans laid out with a flex gap, so join them the way they appear
    const chips = [...(fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="breakdown"]')].map(
      (c) => [...c.querySelectorAll('span')].map((s) => s.textContent?.trim()).join(' '),
    );
    expect(chips).toEqual([
      'vs Anti-Mage +9.5 (1,500 games)',
      'vs Lion +2.0 (only 40 games)',
      'vs Axe no data',
    ]);
    expect(text('[data-testid="advantage-label"]')).toEqual(['combined edge']);
  });

  it('shows a non-positive total without a plus sign', () => {
    const [row] = render([counter({ totalAdvantage: -1.25 })]);

    expect(row).toContain('-1.3');
    expect(row).not.toContain('+');
    expect(text('[data-testid="advantage-label"]')).toEqual(['worse than expected']);
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
