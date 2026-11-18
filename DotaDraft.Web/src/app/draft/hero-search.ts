import { Hero } from './hero.models';

export type PrimaryAttr = 'str' | 'agi' | 'int' | 'all';

/** In the order the in-game hero grid uses. */
export const ATTRIBUTES: { value: PrimaryAttr; label: string }[] = [
  { value: 'str', label: 'Strength' },
  { value: 'agi', label: 'Agility' },
  { value: 'int', label: 'Intelligence' },
  { value: 'all', label: 'Universal' },
];

export interface HeroGroup {
  attribute: PrimaryAttr;
  label: string;
  heroes: Hero[];
}

// Lowercase and drop everything but letters and digits, so "antimage" finds "Anti-Mage"
// and "natures" finds "Nature's Prophet"
const normalize = (text: string) => text.toLowerCase().replace(/[^\p{L}\p{N}]/gu, '');

export function filterHeroes(
  heroes: Hero[],
  query: string,
  attribute: PrimaryAttr | null,
  role: string | null = null,
): Hero[] {
  const needle = normalize(query);
  return heroes.filter(
    (hero) =>
      (attribute === null || hero.primaryAttr === attribute) &&
      (role === null || hero.roles.includes(role)) &&
      normalize(hero.localizedName).includes(needle),
  );
}

/** The roles the heroes have (Carry, Support, …), each once, in alphabetical order. */
export function rolesOf(heroes: Hero[]): string[] {
  return [...new Set(heroes.flatMap((hero) => hero.roles))].sort();
}

/** Groups heroes by primary attribute, keeping their order and dropping empty groups. */
export function groupByAttribute(heroes: Hero[]): HeroGroup[] {
  return ATTRIBUTES.map(({ value, label }) => ({
    attribute: value,
    label,
    heroes: heroes.filter((hero) => hero.primaryAttr === value),
  })).filter((group) => group.heroes.length > 0);
}
