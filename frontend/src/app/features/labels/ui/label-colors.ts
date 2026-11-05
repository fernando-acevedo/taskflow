/** The colors a label can have: mid tones that keep their chip text readable (see `textColorFor`) in light and dark themes. */
export const LABEL_COLORS = [
  '#D32F2F',
  '#E64A19',
  '#F9A825',
  '#388E3C',
  '#00796B',
  '#0288D1',
  '#1976D2',
  '#512DA8',
  '#C2185B',
  '#616161',
] as const;

/** Translation key (under `labels.colors`) with the readable name of each palette color. */
export const LABEL_COLOR_NAMES: Readonly<Record<(typeof LABEL_COLORS)[number], string>> = {
  '#D32F2F': 'red',
  '#E64A19': 'orange',
  '#F9A825': 'amber',
  '#388E3C': 'green',
  '#00796B': 'teal',
  '#0288D1': 'lightBlue',
  '#1976D2': 'blue',
  '#512DA8': 'purple',
  '#C2185B': 'pink',
  '#616161': 'gray',
};

function channel(value: number): number {
  const c = value / 255;
  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

/** WCAG relative luminance of `#RRGGBB` (0 when it isn't one). */
export function luminance(hex: string): number {
  const match = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex);
  if (!match) {
    return 0;
  }
  const [r, g, b] = [match[1], match[2], match[3]].map((part) => channel(parseInt(part ?? '0', 16)));
  return 0.2126 * (r ?? 0) + 0.7152 * (g ?? 0) + 0.0722 * (b ?? 0);
}

/** Black or white, whichever contrasts more with the background. */
export function textColorFor(background: string): '#000000' | '#ffffff' {
  // Contrast with white is 1.05 / (L + 0.05); with black (L + 0.05) / 0.05.
  const l = luminance(background);
  return 1.05 / (l + 0.05) > (l + 0.05) / 0.05 ? '#ffffff' : '#000000';
}
