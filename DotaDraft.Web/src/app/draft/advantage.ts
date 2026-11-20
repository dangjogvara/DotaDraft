/**
 * An advantage rounded to the one decimal it's shown with, so its sign, colour and wording match the number
 * on screen: 0.03 is a neutral 0.0, not a green +0.0. Rounds half away from zero, like Angular's number pipe.
 */
export function shownAdvantage(advantage: number): number {
  // The API sends 2 decimals; the tiny nudge stops binary fractions such as 1.45 * 10 = 14.4999… rounding down
  const rounded = (Math.sign(advantage) * Math.round(Math.abs(advantage) * 10 + 1e-9)) / 10;
  return rounded || 0; // -0 would show as "-0.0"
}
