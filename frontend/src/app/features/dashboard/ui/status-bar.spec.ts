import { TestBed } from '@angular/core/testing';
import { StatusBar, StatusSegment } from './status-bar';

const segments: StatusSegment[] = [
  { key: 'ToDo', label: 'To do', count: 3, color: 'blue' },
  { key: 'InProgress', label: 'In progress', count: 0, color: 'orange' },
  { key: 'Done', label: 'Done', count: 1, color: 'yellow' },
];

describe('StatusBar', () => {
  function render(input: StatusSegment[]): HTMLElement {
    const fixture = TestBed.createComponent(StatusBar);
    fixture.componentRef.setInput('segments', input);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('draws only non-empty segments but lists every status with its count and share', () => {
    const element = render(segments);

    expect(element.querySelectorAll('.segment')).toHaveLength(2);
    const legend = [...element.querySelectorAll('.legend li')].map((item) =>
      [...item.querySelectorAll('span:not(.swatch)')].map((span) => span.textContent?.trim()).join(' '),
    );
    expect(legend).toEqual(['To do 3 75%', 'In progress 0 0%', 'Done 1 25%']);
  });

  it('describes the bar for screen readers', () => {
    const element = render(segments);

    expect(element.querySelector('.bar')?.getAttribute('aria-label')).toBe('To do 3, In progress 0, Done 1');
  });

  it('shows an empty track without percentages when there are no tasks', () => {
    const element = render(segments.map((segment) => ({ ...segment, count: 0 })));

    expect(element.querySelector('.bar.empty')).not.toBeNull();
    expect(element.querySelector('.share')).toBeNull();
  });
});
