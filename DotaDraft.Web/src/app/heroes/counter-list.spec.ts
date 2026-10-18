import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CounterList } from './counter-list';
import { DraftCounter, Hero } from './hero.models';

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

  const counter = (overrides: Partial<DraftCounter>): DraftCounter => ({
    id: 63,
    localizedName: 'Weaver',
    primaryAttr: 'agi',
    imgUrl: null,
    totalAdvantage: 12.4,
    baselineWinRate: 45.34,
    matchups: [{ enemyId: 1, advantage: 12.4, winRate: 73.91, gamesPlayed: 23 }],
    ...overrides,
  });

  function render(counters: DraftCounter[], lineup: Hero[] = [enemy(1, 'Anti-Mage')]) {
    fixture = TestBed.createComponent(CounterList);
    fixture.componentRef.setInput('counters', counters);
    fixture.componentRef.setInput('lineup', lineup);
    fixture.detectChanges();
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="counter"]')].map(
      (li) => li.textContent?.replace(/\s+/g, ' ').trim(),
    );
  }

  it('shows the advantage, baseline, and the single matchup for one enemy', () => {
    const [row] = render([counter({})]);

    expect(row).toContain('Weaver');
    expect(row).toContain('+12.4');
    expect(row).toContain('Usually wins 45.3%');
    expect(row).toContain('73.9% win rate over 23 games');
  });

  it('shows a per-enemy breakdown for a lineup, with a dash for missing data', () => {
    render(
      [
        counter({
          totalAdvantage: 9.5,
          matchups: [
            { enemyId: 1, advantage: 9.5, winRate: 68, gamesPlayed: 40 },
            { enemyId: 2, advantage: 0, winRate: null, gamesPlayed: 0 },
          ],
        }),
      ],
      [enemy(1, 'Anti-Mage'), enemy(2, 'Axe')],
    );

    // The chip's parts are separate spans laid out with a flex gap, so join them the way they appear
    const chips = [...(fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="breakdown"]')].map(
      (c) => [...c.querySelectorAll('span')].map((s) => s.textContent?.trim()).join(' '),
    );
    expect(chips).toEqual(['vs Anti-Mage +9.5 (40g)', 'vs Axe –']);
  });

  it('shows a non-positive total without a plus sign', () => {
    const [row] = render([counter({ totalAdvantage: -1.25 })]);

    expect(row).toContain('-1.3');
    expect(row).not.toContain('+');
  });

  it('keeps the API order', () => {
    const rows = render([counter({ id: 1, localizedName: 'First' }), counter({ id: 2, localizedName: 'Second' })]);

    expect(rows[0]).toContain('First');
    expect(rows[1]).toContain('Second');
  });
});
