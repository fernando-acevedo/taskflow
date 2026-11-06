import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { DemoAccount, DemoService, DemoStatus } from '../../../core/demo/demo.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { DemoPanel } from './demo-panel';

const enabled: DemoStatus = {
  enabled: true,
  nextResetAt: '2026-11-07T03:00:00Z',
  accounts: [
    { role: 'Admin', email: 'admin@demo.local', password: 'Demo12345' },
    { role: 'Member', email: 'member@demo.local', password: 'Demo12345' },
  ],
};

function setup(status: DemoStatus) {
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } })],
    providers: [
      { provide: DemoService, useValue: { status: signal(status) } },
      { provide: LanguageService, useValue: { current: signal('en') } },
    ],
  });
  const fixture = TestBed.createComponent(DemoPanel);
  fixture.detectChanges();
  return fixture;
}

describe('DemoPanel', () => {
  it('lists each account and emits the one chosen', () => {
    const fixture = setup(enabled);
    const chosen: DemoAccount[] = [];
    fixture.componentInstance.selected.subscribe((a) => chosen.push(a));
    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent).toContain('admin@demo.local');
    expect(element.textContent).toContain('Demo12345');
    expect(element.textContent).toContain('demo.resetsAt');
    element.querySelectorAll('button')[1].click();

    expect(chosen).toEqual([enabled.accounts[1]]);
  });

  it('renders nothing when the demo is disabled', () => {
    const fixture = setup({ enabled: false, nextResetAt: null, accounts: [] });

    expect((fixture.nativeElement as HTMLElement).querySelector('section')).toBeNull();
  });
});
