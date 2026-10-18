import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../environments/environment';
import { Hero, HeroCounter } from './hero.models';

@Injectable({ providedIn: 'root' })
export class HeroApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/heroes`;

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
}
