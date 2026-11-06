import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { expectNoAxeViolations } from '../../shared/testing/axe';
import { TenantActions } from './store/tenant.actions';
import { tenantFeature } from './store/tenant.feature';
import { TenantSwitcher } from './tenant-switcher';
import { Tenant } from './tenant.models';

const acme: Tenant = { id: 'a', name: 'Acme', slug: 'acme', isDemo: false, role: 'Owner' };
const globex: Tenant = { id: 'g', name: 'Globex', slug: 'globex', isDemo: false, role: 'Member' };

function render(tenants: Tenant[]) {
  TestBed.configureTestingModule({
    imports: [
      TranslocoTestingModule.forRoot({
        langs: { en: {} },
        translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
      }),
    ],
    providers: [
      provideMockStore({
        selectors: [
          { selector: tenantFeature.selectTenants, value: tenants },
          { selector: tenantFeature.selectCurrent, value: tenants[0] ?? null },
        ],
      }),
    ],
  });
  const fixture = TestBed.createComponent(TenantSwitcher);
  fixture.detectChanges();
  return fixture;
}

describe('TenantSwitcher', () => {
  it('shows only the name, without a menu, for a single tenant', () => {
    const root = render([acme]).nativeElement as HTMLElement;

    expect(root.textContent).toContain('Acme');
    expect(root.querySelector('button')).toBeNull();
  });

  it('renders nothing without tenants', () => {
    expect((render([]).nativeElement as HTMLElement).textContent?.trim()).toBe('');
  });

  it('offers a menu with the role of each tenant when there are several, and switches on selection', () => {
    const fixture = render([acme, globex]);
    const root = fixture.nativeElement as HTMLElement;
    const trigger = root.querySelector<HTMLButtonElement>('button.switcher')!;
    const store = TestBed.inject<MockStore>(MockStore);
    vi.spyOn(store, 'dispatch');

    expect(trigger.getAttribute('aria-label')).toContain('Acme');
    trigger.click();
    fixture.detectChanges();
    const items = [...document.querySelectorAll<HTMLElement>('[role="menuitemradio"]')];

    expect(items.map((item) => item.getAttribute('aria-checked'))).toEqual(['true', 'false']);
    items[1].click();
    expect(store.dispatch).toHaveBeenCalledWith(TenantActions.tenantSwitched({ tenantId: 'g' }));
  });

  it('offers a retry when the tenants failed to load', () => {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { en: {} },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [
        provideMockStore({
          selectors: [
            { selector: tenantFeature.selectTenants, value: [] },
            { selector: tenantFeature.selectCurrent, value: null },
            { selector: tenantFeature.selectStatus, value: 'error' },
          ],
        }),
      ],
    });
    const fixture = TestBed.createComponent(TenantSwitcher);
    fixture.detectChanges();
    const store = TestBed.inject<MockStore>(MockStore);
    vi.spyOn(store, 'dispatch');

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button')!.click();

    expect(store.dispatch).toHaveBeenCalledWith(TenantActions.retryClicked());
  });

  it('has no accessibility violations', async () => {
    await expectNoAxeViolations(render([acme, globex]));
    TestBed.resetTestingModule();
    await expectNoAxeViolations(render([acme]));
  });
});
