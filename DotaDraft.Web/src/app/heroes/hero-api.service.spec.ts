import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../environments/environment';
import { HeroApiService } from './hero-api.service';
import { Hero, HeroCounter } from './hero.models';

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

  it('gets counters for a hero with the requested limit', async () => {
    const counters: HeroCounter[] = [
      { id: 63, localizedName: 'Weaver', primaryAttr: 'agi', imgUrl: null, advantage: 12.4, winRate: 73.91, baselineWinRate: 45.34, gamesPlayed: 23 },
    ];

    const result = firstValueFrom(service.getCounters(1, 3));
    http.expectOne({ method: 'GET', url: `${baseUrl}/1/counters?limit=3` }).flush(counters);

    expect(await result).toEqual(counters);
  });

  it('defaults to 5 counters', () => {
    service.getCounters(1).subscribe();

    http.expectOne(`${baseUrl}/1/counters?limit=5`).flush([]);
  });
});
