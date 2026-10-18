import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../environments/environment';
import { DraftCounter, Hero, HeroCounter } from './hero.models';

@Injectable({ providedIn: 'root' })
export class HeroApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/heroes`;
  private readonly draftCountersUrl = `${environment.apiBaseUrl}/api/counters`;

  /** All heroes, sorted by name. */
  getHeroes(): Observable<Hero[]> {
    return this.http.get<Hero[]>(this.baseUrl);
  }

  /** Top counter-picks against a hero; the API clamps limit to 1-20. */
  getCounters(heroId: number, limit = 5): Observable<HeroCounter[]> {
    return this.http.get<HeroCounter[]>(`${this.baseUrl}/${heroId}/counters`, {
      params: { limit },
    });
  }

  /** Counter-picks against a lineup of 1-5 enemies, ranked by summed advantage; limit is clamped to 1-20. */
  getDraftCounters(enemyIds: number[], limit = 10): Observable<DraftCounter[]> {
    // Repeated keys, as the API binds them to an array: ?against=1&against=14
    const params = new HttpParams({ fromObject: { against: enemyIds.map(String), limit } });
    return this.http.get<DraftCounter[]>(this.draftCountersUrl, { params });
  }
}
