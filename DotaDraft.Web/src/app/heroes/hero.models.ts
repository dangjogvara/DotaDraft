// Mirror DotaDraft.Api's Dtos/HeroDto.cs, HeroCounterDto.cs and DraftCounterDto.cs

export interface Hero {
  id: number;
  name: string;
  localizedName: string;
  primaryAttr: string;
  roles: string[];
  imgUrl: string | null;
}

/** Win rates and advantage are percentages; counters are ranked by advantage. */
export interface HeroCounter {
  id: number;
  localizedName: string;
  primaryAttr: string;
  imgUrl: string | null;
  /** Sample-adjusted win rate minus the counter's baseline, in percentage points. */
  advantage: number;
  winRate: number;
  baselineWinRate: number;
  gamesPlayed: number;
}

/** A counter-pick against a whole enemy lineup, ranked by totalAdvantage (percentage points). */
export interface DraftCounter {
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
