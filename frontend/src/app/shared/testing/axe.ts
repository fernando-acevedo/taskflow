import { ComponentFixture } from '@angular/core/testing';
import axe, { Result, RunOptions } from 'axe-core';

/**
 * Rules switched off because jsdom cannot evaluate them: `color-contrast` needs real layout and painted colors
 * (contrast is checked by hand against the theme tokens), and `region` flags a component rendered on its own,
 * outside the app shell's landmarks.
 */
const JSDOM_UNSUPPORTED: RunOptions['rules'] = {
  'color-contrast': { enabled: false },
  region: { enabled: false },
};

function describeViolation(violation: Result): string {
  const nodes = violation.nodes.map((node) => `    ${node.target.join(' ')}\n      ${node.html.slice(0, 160)}`).join('\n');
  return `${violation.id} (${violation.impact ?? 'n/a'}): ${violation.help}\n${nodes}`;
}

/** Fails with a readable list when the rendered fixture (or the given element, such as an open dialog) breaks an axe rule. */
export async function expectNoAxeViolations(
  target: ComponentFixture<unknown> | HTMLElement,
  options: { disableRules?: string[] } = {},
): Promise<void> {
  const root = target instanceof HTMLElement ? target : (target.nativeElement as HTMLElement);
  const rules = { ...JSDOM_UNSUPPORTED };
  for (const id of options.disableRules ?? []) {
    rules[id] = { enabled: false };
  }
  const results = await axe.run(root, { rules, runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'] } });
  if (results.violations.length > 0) {
    throw new Error(`Accessibility violations:\n${results.violations.map(describeViolation).join('\n')}`);
  }
}
