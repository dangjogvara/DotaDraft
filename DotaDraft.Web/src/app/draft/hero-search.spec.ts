import { filterHeroes, groupByAttribute } from './hero-search';
import { Hero } from './hero.models';

const hero = (id: number, localizedName: string, primaryAttr: string): Hero => ({
  id,
  name: `npc_dota_hero_${id}`,
  localizedName,
  primaryAttr,
  roles: [],
  imgUrl: null,
});

const antiMage = hero(1, 'Anti-Mage', 'agi');
const axe = hero(2, 'Axe', 'str');
const prophet = hero(53, "Nature's Prophet", 'int');
const mars = hero(129, 'Mars', 'str');
const heroes = [antiMage, axe, prophet, mars];

describe('filterHeroes', () => {
  it('returns everything with no query or attribute', () => {
    expect(filterHeroes(heroes, '', null)).toEqual(heroes);
  });

  it('matches anywhere in the name, ignoring case', () => {
    expect(filterHeroes(heroes, 'AR', null)).toEqual([mars]);
    expect(filterHeroes(heroes, 'xe', null)).toEqual([axe]);
  });

  it('ignores punctuation and spaces', () => {
    expect(filterHeroes(heroes, 'antimage', null)).toEqual([antiMage]);
    expect(filterHeroes(heroes, 'natures pro', null)).toEqual([prophet]);
  });

  it('filters by attribute, combined with the query', () => {
    expect(filterHeroes(heroes, '', 'str')).toEqual([axe, mars]);
    expect(filterHeroes(heroes, 'ma', 'str')).toEqual([mars]);
  });
});

describe('groupByAttribute', () => {
  it('groups in in-game order and drops empty groups', () => {
    const groups = groupByAttribute(heroes);

    expect(groups.map((g) => g.label)).toEqual(['Strength', 'Agility', 'Intelligence']);
    expect(groups[0].heroes).toEqual([axe, mars]);
  });
});
