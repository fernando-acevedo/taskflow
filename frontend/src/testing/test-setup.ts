import { MockStore } from '@ngrx/store/testing';

// NgRx selectors (including the ones of `createFeature`) are module-level singletons, and `MockStore` never
// restores an override: a `selectors: [...]` entry in one spec leaked into later specs of the same worker
// (e.g. `tenantFeature.selectCurrentId = 't1'` made the auth interceptor send X-Tenant: t1). Record every
// override and release it after each test; also leave no storage behind.
type Override = MockStore['overrideSelector'];
const overridden = new Set<{ release(): void; clearResult(): void }>();
const original: Override = MockStore.prototype.overrideSelector;

MockStore.prototype.overrideSelector = function (this: MockStore, ...args: Parameters<Override>) {
  const result: unknown = original.apply(this, args);
  if (typeof args[0] !== 'string') {
    overridden.add(args[0]);
  }
  return result as never;
} as Override;

afterEach(() => {
  for (const selector of overridden) {
    selector.release();
    selector.clearResult();
  }
  overridden.clear();
  localStorage.clear();
  sessionStorage.clear();
});
