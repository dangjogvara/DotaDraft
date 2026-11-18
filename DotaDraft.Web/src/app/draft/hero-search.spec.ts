import { filterHeroes, groupByAttribute, rolesOf } from './hero-search';
import { Hero } from './hero.models';

const hero = (
  id: number,
  localizedName: string,
  primaryAttr: string,
  roles: string[] = [],
): Hero => ({
  id,
  name: `npc_dota_hero_${id}`,
  localizedName,
  primaryAttr,
  roles,
  imgUrl: null,
});

const antiMage = hero(1, 'Anti-Mage', 'agi', ['Carry', 'Escape']);
const axe = hero(2, 'Axe', 'str', ['Initiator', 'Durable']);
const prophet = hero(53, "Nature's Prophet", 'int', ['Carry', 'Pusher']);
const mars = hero(129, 'Mars', 'str', ['Initiator']);
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

describe('filterHeroes by role', () => {
  it('keeps heroes with the role, combined with the other filters', () => {
    expect(filterHeroes(heroes, '', null, 'Carry')).toEqual([antiMage, prophet]);
    expect(filterHeroes(heroes, '', 'str', 'Initiator')).toEqual([axe, mars]);
    expect(filterHeroes(heroes, 'ma', 'str', 'Initiator')).toEqual([mars]);
  });

  it('matches roles exactly, as the API spells them', () => {
    expect(filterHeroes(heroes, '', null, 'carry')).toEqual([]);
  });
});

describe('rolesOf', () => {
  it('lists each role once, alphabetically', () => {
    expect(rolesOf(heroes)).toEqual(['Carry', 'Durable', 'Escape', 'Initiator', 'Pusher']);
  });

  it('is empty without heroes', () => {
    expect(rolesOf([])).toEqual([]);
  });
});

describe('groupByAttribute', () => {
  it('groups in in-game order and drops empty groups', () => {
    const groups = groupByAttribute(heroes);

    expect(groups.map((g) => g.label)).toEqual(['Strength', 'Agility', 'Intelligence']);
    expect(groups[0].heroes).toEqual([axe, mars]);
  });
});
