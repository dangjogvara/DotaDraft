import { Hero } from './hero.models';
import { lineupFromParam, lineupToParam } from './lineup-param';

const hero = (id: number): Hero => ({
  id,
  name: `npc_dota_hero_${id}`,
  localizedName: `Hero ${id}`,
  primaryAttr: 'str',
  roles: [],
  imgUrl: null,
});

const heroes = [1, 2, 3, 4, 5, 6].map(hero);
const ids = (lineup: Hero[]) => lineup.map((h) => h.id);

describe('lineupFromParam', () => {
  it('keeps the order of the IDs in the URL', () => {
    expect(ids(lineupFromParam('3,1', heroes))).toEqual([3, 1]);
  });

  it('returns an empty lineup without the parameter', () => {
    expect(lineupFromParam(null, heroes)).toEqual([]);
    expect(lineupFromParam('', heroes)).toEqual([]);
  });

  it('skips unknown, invalid and repeated IDs', () => {
    expect(ids(lineupFromParam('1,99,abc,,2,1, 3', heroes))).toEqual([1, 2, 3]);
  });

  it('keeps only the first five heroes', () => {
    expect(ids(lineupFromParam('6,5,4,3,2,1', heroes))).toEqual([6, 5, 4, 3, 2]);
  });
});

describe('lineupToParam', () => {
  it('joins the IDs in lineup order', () => {
    expect(lineupToParam([hero(14), hero(1)])).toBe('14,1');
  });

  it('is null for an empty lineup, so the parameter is removed', () => {
    expect(lineupToParam([])).toBeNull();
  });
});
