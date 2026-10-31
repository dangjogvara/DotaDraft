// Mirror DotaDraft.Api's Dtos/HeroDto.cs, HeroScoreDto.cs, EnemyMatchupDto.cs and TeamMatchupDto.cs

export interface Hero {
  id: number;
  name: string;
  localizedName: string;
  primaryAttr: string;
  roles: string[];
  imgUrl: string | null;
}

/**
 * Mirrors HeroScoreDto: a hero scored against a whole enemy lineup, either a counter suggestion or one of
 * your own team's picks. Advantages are percentage points.
 */
export interface HeroScore {
  id: number;
  localizedName: string;
  primaryAttr: string;
  imgUrl: string | null;
  /** Sum of the per-enemy advantages. */
  totalAdvantage: number;
  baselineWinRate: number;
  /** One entry per enemy, in lineup order. */
  matchups: EnemyMatchup[];
}

export interface EnemyMatchup {
  enemyId: number;
  advantage: number;
  /** Null when there is no matchup data against this enemy. */
  winRate: number | null;
  gamesPlayed: number;
}

/** Mirrors TeamMatchupDto: your team's heroes scored against the enemy lineup, in pick order. */
export interface TeamMatchup {
  totalAdvantage: number;
  heroes: HeroScore[];
}
