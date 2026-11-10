import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { provideMockStore } from '@ngrx/store/testing';
import { tenantFeature } from '@core/tenant/store/tenant.feature';
import { authFeature } from '@core/auth/store/auth.feature';
import { LanguageService } from '@core/i18n/language.service';
import { expectNoAxeViolations } from '@shared/testing/axe';
import { MyDashboard } from '../data-access/dashboard-api.service';
import { myDashboardFeature } from '../data-access/store/my-dashboard.feature';
import { DashboardPage } from './dashboard-page';

const dashboard: MyDashboard = {
  openTasks: 5,
  overdueTasks: 1,
  dueThisWeek: 2,
  weekMinutes: 150,
  weekMinutesByDay: [30, 60, 0, 60, 0, 0, 0],
  tasks: [
    {
      id: 't1',
      projectId: 'p1',
      projectName: 'Website',
      clientName: 'Acme',
      title: 'Write brief',
      status: 'ToDo',
      priority: 'High',
      dueDate: '2026-11-05',
      isOverdue: true,
    },
  ],
  clients: [{ clientId: 'c1', clientName: 'Acme', activeProjects: 2, openTasks: 5, overdueTasks: 1 }],
};

describe('DashboardPage', () => {
  it('has no accessibility violations, charts included', async () => {
    TestBed.configureTestingModule({
      imports: [
        DashboardPage,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideRouter([]),
        provideMockStore({
          selectors: [
            {
              selector: authFeature.selectUser,
              value: { id: 'u1', email: 'a@b.c', displayName: 'Ada', roles: [] },
            },
            { selector: tenantFeature.selectCanManage, value: false },
            { selector: tenantFeature.selectCurrentId, value: 't1' },
            { selector: myDashboardFeature.selectDashboard, value: dashboard },
            { selector: myDashboardFeature.selectLoading, value: false },
            { selector: myDashboardFeature.selectError, value: null },
          ],
        }),
        { provide: LanguageService, useValue: { current: signal('en') } },
      ],
    });
    const fixture = TestBed.createComponent(DashboardPage);
    fixture.detectChanges();

    await expectNoAxeViolations(fixture);
  });
});
