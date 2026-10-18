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

  function render(lineup: Hero[]) {
    fixture = TestBed.createComponent(LineupSlots);
    fixture.componentRef.setInput('lineup', lineup);
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
});
