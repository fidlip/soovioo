import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { LinkedAccount, LinkedAccountsApiService } from '@soovioo/shared';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { LinkAccountDialogService } from '../../link-account/link-account-dialog.service';
import { NotifyService } from '../../notify.service';
import { UnreadCountsService } from '../../unread-counts.service';
import { LinkedAccountsCard } from './linked-accounts-card';

const ACCOUNT: LinkedAccount = {
  userId: 7,
  schoolName: 'SŠ Údolí',
  username: 'novak',
  unread: 2,
};

describe('LinkedAccountsCard', () => {
  const api = { unlink: vi.fn() };
  const refresh = vi.fn();
  const confirm = vi.fn();

  function open() {
    TestBed.configureTestingModule({
      imports: [LinkedAccountsCard],
      providers: [
        { provide: LinkedAccountsApiService, useValue: api },
        {
          provide: UnreadCountsService,
          useValue: { refresh, linkedAccounts: signal([ACCOUNT]) },
        },
        { provide: ConfirmationService, useValue: { confirm } },
        {
          provide: NotifyService,
          useValue: { success: vi.fn(), actionFailed: vi.fn() },
        },
      ],
    });
    const fixture = TestBed.createComponent(LinkedAccountsCard);
    fixture.detectChanges();
    return fixture;
  }

  const button = (fixture: ReturnType<typeof open>, label: string) =>
    [...fixture.nativeElement.querySelectorAll('button')].find(
      (b: HTMLButtonElement) => b.textContent?.includes(label),
    ) as HTMLButtonElement;

  beforeEach(() => vi.clearAllMocks());

  it('lists the linked accounts', () => {
    expect(open().nativeElement.textContent).toContain('SŠ Údolí – novak');
  });

  it('asks for no password itself: the button opens the dialog', () => {
    const fixture = open();
    expect(fixture.nativeElement.querySelector('input')).toBeNull();

    button(fixture, 'Propojit další účet').click();

    expect(TestBed.inject(LinkAccountDialogService).open()).toBe(true);
  });

  it('unlinks only after confirming', () => {
    api.unlink.mockReturnValue(of(undefined));
    const fixture = open();

    button(fixture, 'Zrušit propojení').click();

    expect(api.unlink).not.toHaveBeenCalled();
    confirm.mock.calls[0][0].accept();
    expect(api.unlink).toHaveBeenCalledWith(7);
    expect(refresh).toHaveBeenCalled();
  });
});
