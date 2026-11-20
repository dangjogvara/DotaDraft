import { shownAdvantage } from './advantage';

describe('shownAdvantage', () => {
  it.each([
    [0.03, 0],
    [-0.03, 0],
    [0.049, 0],
    [0.05, 0.1],
    [-0.05, -0.1],
    [1.45, 1.5],
    [-2.25, -2.3],
    [12.44, 12.4],
    [0, 0],
  ])('rounds %s to %s', (advantage, shown) => {
    expect(shownAdvantage(advantage)).toBe(shown);
  });

  it('never returns -0, which would show as "-0.0"', () => {
    expect(Object.is(shownAdvantage(-0.03), 0)).toBe(true);
  });
});
