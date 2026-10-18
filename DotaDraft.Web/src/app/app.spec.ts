import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../environments/environment';
import { App } from './app';

describe('App', () => {
  let http: HttpTestingController;
  const heroesUrl = `${environment.apiBaseUrl}/api/heroes`;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function render() {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    return fixture;
  }

  function status(fixture: ReturnType<typeof render>) {
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return el.querySelector('[data-testid="api-status"]')?.textContent?.trim();
  }

  it('renders the title', () => {
    const fixture = render();
    http.expectOne(heroesUrl).flush([]);

    expect((fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toContain('DotaDraft');
  });

  it('shows how many heroes the API returned', () => {
    const fixture = render();
    expect(status(fixture)).toContain('Loading heroes');

    http.expectOne(heroesUrl).flush([{ id: 1 }, { id: 2 }]);

    expect(status(fixture)).toContain('Loaded 2 heroes');
  });

  it('shows an error when the API is unreachable', () => {
    const fixture = render();

    http.expectOne(heroesUrl).error(new ProgressEvent('error'));

    expect(status(fixture)).toContain('Could not reach the API');
  });
});
