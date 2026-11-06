import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { ActivityEntry } from '../data-access/activity-api.service';
import { anEntry } from '../data-access/store/testing';
import { ActivityFeed } from './activity-feed';

const en = {
  activity: {
    loadMore: 'Load more',
    viaApiKey: 'via API key',
    empty: 'No activity yet.',
    generic: '{{ actor }} did {{ type }}',
    types: { TaskMoved: '{{ actor }} moved “{{ task }}” from {{ from }} to {{ to }}' },
  },
  tasks: { statuses: { ToDo: 'To do', Done: 'Done' } },
};

describe('ActivityFeed', () => {
  function render(inputs: { items: ActivityEntry[]; hasMore?: boolean; linkable?: string[] }) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [
        ActivityFeed,
        TranslocoTestingModule.forRoot({ langs: { en }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [{ provide: LanguageService, useValue: { current: signal('en') } }],
    });
    const fixture = TestBed.createComponent(ActivityFeed);
    fixture.componentRef.setInput('items', inputs.items);
    fixture.componentRef.setInput('hasMore', inputs.hasMore ?? false);
    fixture.componentRef.setInput('linkableTaskIds', new Set(inputs.linkable ?? []));
    const loadMore = vi.fn();
    const openTask = vi.fn();
    fixture.componentInstance.loadMore.subscribe(loadMore);
    fixture.componentInstance.openTask.subscribe(openTask);
    fixture.detectChanges();
    return { element: fixture.nativeElement as HTMLElement, loadMore, openTask };
  }

  const moved = anEntry({ id: 'a', type: 'TaskMoved', taskId: 't1', changes: { taskTitle: 'Docs', from: 'ToDo', to: 'Done' } });

  it('shows the translated sentence of each entry', () => {
    const { element } = render({ items: [moved, anEntry({ id: 'b', type: 'Strange', changes: {} })] });

    const sentences = Array.from(element.querySelectorAll('.sentence')).map((node) => node.textContent?.replace(/\s+/g, ' ').trim());
    expect(sentences).toEqual(['Ana moved “Docs” from To do to Done', 'Ana did Strange']);
  });

  it('marks the entries made with an API key', () => {
    const { element } = render({ items: [anEntry({ id: 'k', viaApiKey: true }), anEntry({ id: 'p' })] });

    const marks = Array.from(element.querySelectorAll('.via-key')).map((node) => node.textContent?.trim());
    expect(marks).toEqual(['via API key']);
    expect(element.querySelectorAll('.entry')[0].querySelector('.via-key')).not.toBeNull();
    expect(element.querySelectorAll('.entry')[1].querySelector('.via-key')).toBeNull();
  });

  it('makes the title a button that opens the task while the task exists', () => {
    const { element, openTask } = render({ items: [moved], linkable: ['t1'] });

    const link = element.querySelector<HTMLButtonElement>('.task-link');
    expect(link?.textContent).toBe('Docs');
    link?.click();
    expect(openTask).toHaveBeenCalledWith('t1');
  });

  it('leaves the title as plain text when the task is gone', () => {
    const { element } = render({ items: [moved], linkable: [] });

    expect(element.querySelector('.task-link')).toBeNull();
    expect(element.querySelector('.sentence')?.textContent).toContain('“Docs”');
  });

  it('asks for more only when there is more', () => {
    expect(render({ items: [moved] }).element.querySelector('.more')).toBeNull();

    const more = render({ items: [moved], hasMore: true });
    more.element.querySelector<HTMLButtonElement>('.more')?.click();
    expect(more.loadMore).toHaveBeenCalled();
  });

  it('says so when there is nothing yet', () => {
    expect(render({ items: [] }).element.querySelector('.empty')?.textContent).toBe('No activity yet.');
  });
});
