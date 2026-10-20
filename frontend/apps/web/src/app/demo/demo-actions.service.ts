import { Injectable, inject } from '@angular/core';
import { ConfirmationService, MenuItem } from '@openng/optimus-ui/api';
import { DemoApiService } from '@soovioo/shared';
import { NotifyService } from '../notify.service';
import { demoEntryUrl } from '../school-context';

export const SUPPORT_EMAIL = 'info@soovioo.cz';

/** The demo's "…" menu — in the demo bar and under the user picker. */
@Injectable({ providedIn: 'root' })
export class DemoActionsService {
  private readonly demoApi = inject(DemoApiService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly notify = inject(NotifyService);

  menu(slug: string): MenuItem[] {
    return [
      {
        label: 'Resetovat data',
        icon: 'pi pi-refresh',
        command: () => this.reset(slug),
      },
      {
        label: 'Smazat demo',
        icon: 'pi pi-trash',
        command: () => this.delete(slug),
      },
      { separator: true },
      {
        label: 'Kontaktovat podporu',
        icon: 'pi pi-envelope',
        url: supportMailto(slug),
      },
    ];
  }

  /** The wizard again on the entry page, then fresh data at the same address. */
  reset(slug: string): void {
    this.confirmation.confirm({
      header: 'Resetovat data',
      message:
        'Všechna data ukázkové školy se vygenerují znovu — vaše změny i nahrané soubory zmizí. Datum smazání dema se nemění.',
      icon: 'pi pi-refresh',
      acceptLabel: 'Pokračovat',
      rejectLabel: 'Zrušit',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        window.location.assign(demoEntryUrl(`reset=${encodeURIComponent(slug)}`)),
    });
  }

  delete(slug: string): void {
    this.confirmation.confirm({
      header: 'Smazat demo',
      message:
        'Ukázková škola se smaže i se všemi daty, nevratně. Kdykoli si můžete vytvořit novou.',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Smazat',
      rejectLabel: 'Zrušit',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.demoApi.delete(slug).subscribe({
          // The server dropped the session cookie; the access token lives
          // in memory only and goes with the page.
          next: () => window.location.assign(demoEntryUrl('new=1')),
          error: (err) => this.notify.actionFailed('demo delete failed', err),
        }),
    });
  }
}

export function supportMailto(slug: string): string {
  const subject = encodeURIComponent(`Soovioo demo ${slug}`);
  return `mailto:${SUPPORT_EMAIL}?subject=${subject}`;
}
