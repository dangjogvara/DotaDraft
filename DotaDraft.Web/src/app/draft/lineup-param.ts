import { Hero } from './hero.models';
import { MAX_LINEUP } from './lineup-slots';

/** The query parameter holding the enemy lineup, e.g. `?against=1,14`. */
export const LINEUP_PARAM = 'against';

/** The query parameter holding your own team's picks, e.g. `&team=5,8`. */
export const TEAM_PARAM = 'team';

/**
 * The lineup a URL describes, in order. Unknown, invalid and repeated IDs are skipped, and only the
 * first five heroes are kept, so a hand-edited or outdated link still opens a valid lineup.
 */
export function lineupFromParam(value: string | null, heroes: Hero[]): Hero[] {
  const byId = new Map(heroes.map((hero) => [hero.id, hero]));
  const ids = new Set(
    (value ?? '')
      .split(',')
      .map((part) => part.trim())
      .filter((part) => part.length > 0)
      .map(Number),
  );
  return [...ids]
    .map((id) => byId.get(id))
    .filter((hero) => hero !== undefined)
    .slice(0, MAX_LINEUP);
}

/** The parameter value for a lineup, or null to drop the parameter when the lineup is empty. */
export function lineupToParam(lineup: Hero[]): string | null {
  return lineup.length > 0 ? lineup.map((hero) => hero.id).join(',') : null;
}
