import { TestBed } from '@angular/core/testing';
import { TenantStorage } from './tenant-storage';

describe('TenantStorage', () => {
  let storage: TenantStorage;

  beforeEach(() => {
    localStorage.clear();
    storage = TestBed.inject(TenantStorage);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('remembers the tenant of each user separately', () => {
    storage.save('u1', 'a');
    storage.save('u2', 'b');

    expect(storage.read('u1')).toBe('a');
    expect(storage.read('u2')).toBe('b');
    expect(storage.read('u3')).toBeNull();
  });

  it('survives blocked storage: nothing is remembered and nothing throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(() => storage.save('u1', 'a')).not.toThrow();
    expect(storage.read('u1')).toBeNull();
  });
});
