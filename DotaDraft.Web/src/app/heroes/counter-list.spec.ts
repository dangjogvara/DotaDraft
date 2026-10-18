import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CounterList } from './counter-list';
import { HeroCounter } from './hero.models';

describe('CounterList', () => {
  let fixture: ComponentFixture<CounterList>;

  const counter = (overrides: Partial<HeroCounter>): HeroCounter => ({
    id: 1,
    localizedName: 'Weaver',
    primaryAttr: 'agi',
    imgUrl: null,
    advantage: 12.4,
    winRate: 73.91,
    baselineWinRate: 45.34,
    gamesPlayed: 23,
    ...overrides,
  });

  function render(counters: HeroCounter[]) {
    fixture = TestBed.createComponent(CounterList);
    fixture.componentRef.setInput('counters', counters);
    fixture.detectChanges();
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="counter"]')].map(
      (li) => li.textContent?.replace(/\s+/g, ' ').trim(),
    );
  }

  it('shows advantage, win rate, games and baseline', () => {
    const [row] = render([counter({})]);

    expect(row).toContain('Weaver');
    expect(row).toContain('+12.4');
    expect(row).toContain('73.9% win rate over 23 games');
    expect(row).toContain('usually 45.3%');
  });

  it('shows non-positive advantage without a plus sign', () => {
    const [row] = render([counter({ advantage: -1.25 })]);

    expect(row).toContain('-1.3');
    expect(row).not.toContain('+');
  });

  it('keeps the API order', () => {
    const rows = render([counter({ id: 1, localizedName: 'First' }), counter({ id: 2, localizedName: 'Second' })]);

    expect(rows[0]).toContain('First');
    expect(rows[1]).toContain('Second');
  });
});
