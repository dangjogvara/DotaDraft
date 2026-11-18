import { ComponentFixture, TestBed } from '@angular/core/testing';

import { HeroFilters } from './hero-filters';

describe('HeroFilters', () => {
  let fixture: ComponentFixture<HeroFilters>;
  let filters: HeroFilters;

  beforeEach(() => {
    fixture = TestBed.createComponent(HeroFilters);
    filters = fixture.componentInstance;
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
  });

  afterEach(() => (fixture.nativeElement as HTMLElement).remove());

  const input = () => (fixture.nativeElement as HTMLElement).querySelector('input')!;
  const button = (label: string) =>
    [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (b) => b.textContent?.trim() === label,
    )!;
  const press = (key: string, target: EventTarget = document.body) =>
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));

  it('updates the query as the user types', () => {
    input().value = 'axe';
    input().dispatchEvent(new Event('input'));

    expect(filters.query()).toBe('axe');
  });

  it('focuses search when "/" is pressed outside an input', () => {
    press('/');

    expect(document.activeElement).toBe(input());
  });

  it('does not steal "/" while typing in another input', () => {
    const other = document.body.appendChild(document.createElement('input'));
    other.focus();

    press('/', other);

    expect(document.activeElement).toBe(other);
    other.remove();
  });

  it('clears the query on Escape', () => {
    filters.query.set('axe');
    fixture.detectChanges();

    press('Escape', input());

    expect(filters.query()).toBe('');
  });

  it('toggles an attribute on and off', () => {
    button('Strength').click();
    fixture.detectChanges();
    expect(filters.attribute()).toBe('str');
    expect(button('Strength').getAttribute('aria-pressed')).toBe('true');

    button('Agility').click();
    expect(filters.attribute()).toBe('agi');

    button('Agility').click();
    expect(filters.attribute()).toBeNull();
  });

  it('swaps state and hover colours while keeping the static classes', () => {
    filters.attribute.set('str');
    fixture.detectChanges();

    expect(button('Strength').className).toContain('rounded-md');
    expect(button('Strength').classList).toContain('hover:bg-sky-500');
    expect(button('Agility').classList).toContain('hover:bg-slate-700');
    expect(button('Agility').classList).not.toContain('bg-sky-600');
  });

  describe('roles', () => {
    const roleButtons = () =>
      [
        ...(fixture.nativeElement as HTMLElement).querySelectorAll(
          '[aria-label="Filter by role"] button',
        ),
      ].map((b) => b.textContent?.trim());

    it('shows no role buttons without roles', () => {
      expect(
        (fixture.nativeElement as HTMLElement).querySelector('[aria-label="Filter by role"]'),
      ).toBeNull();
    });

    it('shows one button per role and toggles a role on and off', () => {
      fixture.componentRef.setInput('roles', ['Carry', 'Support']);
      fixture.detectChanges();
      expect(roleButtons()).toEqual(['Carry', 'Support']);

      button('Support').click();
      fixture.detectChanges();
      expect(filters.role()).toBe('Support');
      expect(button('Support').getAttribute('aria-pressed')).toBe('true');

      button('Carry').click();
      expect(filters.role()).toBe('Carry');

      button('Carry').click();
      expect(filters.role()).toBeNull();
    });
  });
});
