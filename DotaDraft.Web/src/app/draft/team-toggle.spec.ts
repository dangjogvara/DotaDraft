import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TeamToggle } from './team-toggle';

describe('TeamToggle', () => {
  let fixture: ComponentFixture<TeamToggle>;
  const el = () => fixture.nativeElement as HTMLElement;
  const button = (label: string) =>
    [...el().querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;

  beforeEach(() => {
    fixture = TestBed.createComponent(TeamToggle);
    fixture.detectChanges();
  });

  it('starts on the enemy team', () => {
    expect(button('Enemy team').getAttribute('aria-pressed')).toBe('true');
    expect(button('My team').getAttribute('aria-pressed')).toBe('false');
  });

  it('switches team on click', () => {
    button('My team').click();
    fixture.detectChanges();

    expect(fixture.componentInstance.team()).toBe('ally');
    expect(button('My team').getAttribute('aria-pressed')).toBe('true');
    expect(button('Enemy team').getAttribute('aria-pressed')).toBe('false');
  });
});
