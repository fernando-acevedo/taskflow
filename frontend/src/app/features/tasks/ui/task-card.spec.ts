import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { LanguageService } from '@core/i18n/language.service';
import { expectNoAxeViolations } from '@shared/testing/axe';
import { aTask } from '../data-access/store/testing';
import { Task } from '../data-access/tasks-api.service';
import { TaskCard } from './task-card';

describe('TaskCard', () => {
  function render(overrides: Partial<Task>) {
    TestBed.configureTestingModule({
      imports: [
        TaskCard,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [{ provide: LanguageService, useValue: { current: signal('en') } }],
    });
    const fixture = TestBed.createComponent(TaskCard);
    fixture.componentRef.setInput('task', aTask({ id: 'a', ...overrides }));
    fixture.detectChanges();
    return fixture;
  }

  it('shows the blocked marker and the status note', () => {
    const element = render({ isBlocked: true, statusNote: 'Waiting for the client' }).nativeElement as HTMLElement;

    expect(element.querySelector('.blocked')).not.toBeNull();
    expect(element.querySelector('.note')?.textContent).toContain('Waiting for the client');
  });

  it('shows neither when the task is not blocked and has no note', () => {
    const element = render({}).nativeElement as HTMLElement;

    expect(element.querySelector('.blocked')).toBeNull();
    expect(element.querySelector('.note')).toBeNull();
  });

  it('has no accessibility violations when blocked', async () => {
    await expectNoAxeViolations(render({ isBlocked: true, statusNote: 'Waiting' }));
  });
});
