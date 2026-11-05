import { TestBed } from '@angular/core/testing';
import { LabelChip } from './label-chip';
import { LABEL_COLORS, textColorFor } from './label-colors';

describe('LabelChip', () => {
  function render(color: string) {
    const fixture = TestBed.createComponent(LabelChip);
    fixture.componentRef.setInput('label', { name: 'Bug', color });
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the name with the label color as background', () => {
    const element = render('#D32F2F');

    expect(element.textContent).toContain('Bug');
    expect(element.style.backgroundColor).toBe('rgb(211, 47, 47)');
  });

  it('uses dark text on light colors and light text on dark ones', () => {
    expect(render('#F9A825').style.color).toBe('rgb(0, 0, 0)');
    expect(render('#512DA8').style.color).toBe('rgb(255, 255, 255)');
  });

  it('gives every palette color a text color with at least 4.5:1 contrast', () => {
    for (const color of LABEL_COLORS) {
      const l = textColorFor(color) === '#ffffff' ? 1.05 / (lum(color) + 0.05) : (lum(color) + 0.05) / 0.05;
      expect(l, color).toBeGreaterThanOrEqual(4.5);
    }
  });
});

function lum(hex: string): number {
  const [r, g, b] = [1, 3, 5].map((i) => {
    const c = parseInt(hex.slice(i, i + 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * (r ?? 0) + 0.7152 * (g ?? 0) + 0.0722 * (b ?? 0);
}
