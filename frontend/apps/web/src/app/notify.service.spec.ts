import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { MessageService } from '@openng/optimus-ui/api';
import { FORBIDDEN_MESSAGE, NotifyService } from './notify.service';

describe('NotifyService.actionFailed', () => {
  let notify: NotifyService;
  let shown: string[];

  beforeEach(() => {
    shown = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: MessageService,
          useValue: { add: (m: { summary: string }) => shown.push(m.summary) },
        },
      ],
    });
    notify = TestBed.inject(NotifyService);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  it('says a bare 403 is about a missing permission', () => {
    notify.actionFailed('save failed', new HttpErrorResponse({ status: 403 }));

    expect(shown).toEqual([FORBIDDEN_MESSAGE]);
  });

  it('keeps the server’s own words for a 403 that has them', () => {
    notify.actionFailed(
      'save failed',
      new HttpErrorResponse({ status: 403, error: 'Účet už není aktivní.' }),
    );

    expect(shown).toEqual(['Účet už není aktivní.']);
  });

  it('falls back to the generic message otherwise', () => {
    notify.actionFailed('save failed', new HttpErrorResponse({ status: 409 }));

    expect(shown).toEqual(['Akci se nepodařilo provést.']);
  });
});
