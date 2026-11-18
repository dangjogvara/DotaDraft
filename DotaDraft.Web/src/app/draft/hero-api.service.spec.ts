import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../environments/environment';
import { HeroApiService } from './hero-api.service';
import { Hero } from './hero.models';

describe('HeroApiService', () => {
  let service: HeroApiService;
  let http: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/api/heroes`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(HeroApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('gets all heroes', async () => {
    const heroes: Hero[] = [
      { id: 1, name: 'npc_dota_hero_antimage', localizedName: 'Anti-Mage', primaryAttr: 'agi', roles: ['Carry'], imgUrl: null },
    ];

    const result = firstValueFrom(service.getHeroes());
    http.expectOne({ method: 'GET', url: baseUrl }).flush(heroes);

    expect(await result).toEqual(heroes);
  });

  it('gets draft counters with one repeated "against" key per enemy', () => {
    service.getDraftCounters([1, 14, 74]).subscribe();

    http.expectOne(`${environment.apiBaseUrl}/api/counters?against=1&against=14&against=74&limit=10`).flush([]);
  });

  it('adds one repeated "exclude" key per hero to leave out', () => {
    service.getDraftCounters([1], [5, 8]).subscribe();

    http.expectOne(`${environment.apiBaseUrl}/api/counters?against=1&exclude=5&exclude=8&limit=10`).flush([]);
  });

  it('adds the role when there is one', () => {
    service.getDraftCounters([1], [5], 'Support').subscribe();

    http.expectOne(`${environment.apiBaseUrl}/api/counters?against=1&exclude=5&role=Support&limit=10`).flush([]);
  });

  it('gets the team matchup with repeated "team" and "against" keys', () => {
    service.getTeamMatchup([82, 26], [1, 14]).subscribe();

    http.expectOne(`${environment.apiBaseUrl}/api/team-matchup?team=82&team=26&against=1&against=14`).flush({
      totalAdvantage: 0,
      heroes: [],
    });
  });
});
