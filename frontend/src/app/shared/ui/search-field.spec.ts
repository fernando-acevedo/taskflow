import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { SearchField } from './search-field';

describe('SearchField', () => {
  function setup(value = '') {
    const fixture = TestBed.createComponent(SearchField);
    fixture.componentRef.setInput('label', 'Search');
    fixture.componentRef.setInput('value', value);
    fixture.detectChanges();
    const searched: string[] = [];
    fixture.componentInstance.searched.subscribe((search) => searched.push(search));
    const input = fixture.debugElement.query(By.css('input')).nativeElement as HTMLInputElement;
    return { input, searched };
  }

  it('searches once when Enter fires both keyup.enter and search', () => {
    const { input, searched } = setup();

    input.value = '  acme ';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));
    input.dispatchEvent(new Event('search'));

    expect(searched).toEqual(['acme']);
  });

  it('searches again with an empty text when the box is cleared', () => {
    const { input, searched } = setup('acme');

    input.value = '';
    input.dispatchEvent(new Event('search'));

    expect(searched).toEqual(['']);
  });
});
