import { TestBed } from '@angular/core/testing';
import { MainBlocks, MainBlocksApiService } from '@soovioo/shared';
import { of } from 'rxjs';
import { NotifyService } from '../notify.service';
import { MainBlockSettings, mainBlockText } from './main-block-settings';

describe('mainBlockText', () => {
  it('names the periods from–to, or the one', () => {
    expect(mainBlockText({ from: 1, to: 4 })).toBe('1.–4. hodina');
    expect(mainBlockText({ from: 2, to: 2 })).toBe('2. hodina');
  });
});

describe('MainBlockSettings', () => {
  const blocks: MainBlocks = {
    from: 1,
    to: 5,
    grades: [
      { grade: 1, from: 1, to: 4 },
      { grade: 2, from: null, to: null },
    ],
    classes: [
      { id: 10, name: '1.A', grade: 1, from: null, to: null },
      { id: 11, name: '1.B', grade: 1, from: 2, to: 5 },
      { id: 12, name: '2.A', grade: 2, from: null, to: null },
    ],
  };

  function render(api: Partial<MainBlocksApiService>) {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: MainBlocksApiService,
          useValue: { get: () => of(blocks), ...api },
        },
        {
          provide: NotifyService,
          useValue: {
            success: vi.fn(),
            error: vi.fn(),
            serverFieldErrors: () => null,
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(MainBlockSettings);
    fixture.detectChanges();
    return fixture;
  }

  function rows(element: HTMLElement, table: string): string[] {
    return [...element.querySelectorAll(`table[aria-label="${table}"] tr`)].map(
      (tr) => tr.textContent!.replace(/\s+/g, ' ').trim(),
    );
  }

  it('shows each grade and class with its block and where it comes from', () => {
    const fixture = render({});

    expect(rows(fixture.nativeElement, 'Ročníky')).toEqual([
      '1. ročník 1.–4. hodina',
      '2. ročník 1.–5. hodina (podle školy)',
    ]);
    expect(rows(fixture.nativeElement, 'Třídy')).toEqual([
      '1.A 1.–4. hodina (podle ročníku)',
      '1.B 2.–5. hodina',
      '2.A 1.–5. hodina (podle školy)',
    ]);
  });

  it('gives a class back to its grade', () => {
    const setClass = vi.fn(() => of(undefined));
    const fixture = render({ setClass });

    (
      fixture.nativeElement.querySelector(
        'button[aria-label="Podle ročníku — 1.B"]',
      ) as HTMLButtonElement
    ).click();

    expect(setClass).toHaveBeenCalledWith(11, null);
  });
});
