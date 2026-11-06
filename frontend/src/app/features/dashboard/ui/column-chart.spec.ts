import { TestBed } from '@angular/core/testing';
import { Column, ColumnChart } from './column-chart';

const columns: Column[] = [
  { key: 'mon', label: 'Mon', value: 120, text: '2 h' },
  { key: 'tue', label: 'Tue', value: 0, text: '0 min' },
  { key: 'wed', label: 'Wed', value: 60, text: '1 h' },
];

describe('ColumnChart', () => {
  function render(input: Column[], max: number | null = null): HTMLElement {
    const fixture = TestBed.createComponent(ColumnChart);
    fixture.componentRef.setInput('columns', input);
    fixture.componentRef.setInput('max', max);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const heights = (element: HTMLElement) =>
    [...element.querySelectorAll<HTMLElement>('.fill')].map((fill) => fill.style.height);

  it('scales the columns to the largest value', () => {
    expect(heights(render(columns))).toEqual(['100%', '0%', '50%']);
  });

  it('scales to a given maximum', () => {
    expect(heights(render(columns, 240))).toEqual(['50%', '0%', '25%']);
  });

  it('shows every value as text, zeros included, under its label', () => {
    const element = render(columns);

    expect([...element.querySelectorAll('.value')].map((node) => node.textContent?.trim())).toEqual(['2 h', '0 min', '1 h']);
    expect([...element.querySelectorAll('.label')].map((node) => node.textContent?.trim())).toEqual(['Mon', 'Tue', 'Wed']);
  });

  it('falls back to the number and describes the chart for screen readers', () => {
    const element = render([{ key: 'a', label: 'A', value: 3 }]);

    expect(element.querySelector('.value')?.textContent?.trim()).toBe('3');
    expect(render(columns).querySelector('.chart')?.getAttribute('aria-label')).toBe('Mon 2 h, Tue 0 min, Wed 1 h');
  });

  it('does not divide by zero when every value is zero', () => {
    expect(heights(render([{ key: 'a', label: 'A', value: 0 }]))).toEqual(['0%']);
  });
});
