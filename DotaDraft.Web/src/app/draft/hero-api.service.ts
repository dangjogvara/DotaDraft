import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../environments/environment';
import { DraftCounter, Hero, TeamMatchup } from './hero.models';

@Injectable({ providedIn: 'root' })
export class HeroApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/heroes`;
  private readonly draftCountersUrl = `${environment.apiBaseUrl}/api/counters`;
  private readonly teamMatchupUrl = `${environment.apiBaseUrl}/api/team-matchup`;

  /** All heroes, sorted by name. */
  getHeroes(): Observable<Hero[]> {
    return this.http.get<Hero[]>(this.baseUrl);
  }

  /**
   * Counter-picks against a lineup of 1-5 enemies, ranked by summed advantage, never suggesting the
   * up to 5 excluded heroes (your own team's picks). The API limits limit to 1-20.
   */
  getDraftCounters(enemyIds: number[], excludeIds: number[] = [], limit = 10): Observable<DraftCounter[]> {
    // Repeated keys, as the API binds them to an array: ?against=1&against=14&exclude=5
    const params = new HttpParams({
      fromObject: {
        against: enemyIds.map(String),
        ...(excludeIds.length > 0 && { exclude: excludeIds.map(String) }),
        limit,
      },
    });
    return this.http.get<DraftCounter[]>(this.draftCountersUrl, { params });
  }

  /** How each of your team's 1-5 heroes does against a lineup of 1-5 enemies, plus the team's total. */
  getTeamMatchup(teamIds: number[], enemyIds: number[]): Observable<TeamMatchup> {
    const params = new HttpParams({ fromObject: { team: teamIds.map(String), against: enemyIds.map(String) } });
    return this.http.get<TeamMatchup>(this.teamMatchupUrl, { params });
  }
}
