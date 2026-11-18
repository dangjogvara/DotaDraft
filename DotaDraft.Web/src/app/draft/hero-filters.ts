import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  input,
  model,
  viewChild,
} from '@angular/core';

import { ATTRIBUTES, PrimaryAttr } from './hero-search';

@Component({
  selector: 'app-hero-filters',
  templateUrl: './hero-filters.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown)': 'onDocumentKeydown($event)' },
})
export class HeroFilters {
  readonly query = model('');
  readonly attribute = model<PrimaryAttr | null>(null);
  /** The roles to offer as filters; no role row without any. */
  readonly roles = input<string[]>([]);
  readonly role = model<string | null>(null);

  protected readonly attributes = ATTRIBUTES;
  private readonly searchInput = viewChild.required<ElementRef<HTMLInputElement>>('search');

  protected toggleAttribute(value: PrimaryAttr): void {
    this.attribute.update((current) => (current === value ? null : value));
  }

  protected toggleRole(value: string): void {
    this.role.update((current) => (current === value ? null : value));
  }

  // "/" focuses search from anywhere, unless the user is already typing somewhere
  protected onDocumentKeydown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    const typing = target?.closest('input, textarea, select, [contenteditable="true"]');
    if (event.key === '/' && !typing) {
      event.preventDefault();
      this.searchInput().nativeElement.focus();
    }
  }

  protected onSearchKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.query.set('');
    }
  }
}
