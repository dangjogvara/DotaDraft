import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Hero } from './hero.models';
import { LineupSlots } from './lineup-slots';

const hero = (id: number, localizedName: string): Hero => ({
  id,
  name: `npc_dota_hero_${id}`,
  localizedName,
  primaryAttr: 'str',
  roles: [],
  imgUrl: null,
});

describe('LineupSlots', () => {
  let fixture: ComponentFixture<LineupSlots>;

  function render(lineup: Hero[], inputs: { label?: string; team?: 'enemy' | 'ally' } = {}) {
    fixture = TestBed.createComponent(LineupSlots);
    fixture.componentRef.setInput('lineup', lineup);
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('always shows five slots, filling them in pick order', () => {
    const el = render([hero(2, 'Axe'), hero(1, 'Anti-Mage')]);

    const slots = [...el.querySelectorAll('[data-testid="slot"]')].map((s) => s.textContent?.replace('✕', '').trim());
    expect(slots).toEqual(['Axe', 'Anti-Mage', 'Empty', 'Empty', 'Empty']);
    expect(el.querySelector('h2')?.textContent).toContain('2/5');
  });

  it('emits the hero to remove', () => {
    const el = render([hero(2, 'Axe')]);
    const removed: Hero[] = [];
    fixture.componentInstance.removed.subscribe((h) => removed.push(h));

    el.querySelector<HTMLButtonElement>('[aria-label="Remove Axe"]')!.click();

    expect(removed.map((h) => h.id)).toEqual([2]);
  });

  it('only offers Clear when the lineup has heroes', () => {
    const buttons = (el: HTMLElement) => [...el.querySelectorAll('button')].map((b) => b.textContent?.trim());

    expect(buttons(render([]))).not.toContain('Clear');
    expect(buttons(render([hero(2, 'Axe')]))).toContain('Clear');
  });

  it("shows your team's label and colours its picks green", () => {
    const el = render([hero(2, 'Axe')], { label: 'Your team', team: 'ally' });

    expect(el.querySelector('h2')?.textContent).toContain('Your team (1/5)');
    const pick = el.querySelector('[data-testid="slot"] .ring-2')!;
    expect(pick.classList).toContain('ring-emerald-500/70');
    expect(pick.classList).not.toContain('ring-red-500/70');
    expect(el.querySelector('[aria-label="Clear Your team"]')).not.toBeNull();
  });
});
