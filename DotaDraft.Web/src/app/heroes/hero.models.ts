// Mirror DotaDraft.Api's Dtos/HeroDto.cs and Dtos/HeroCounterDto.cs

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
