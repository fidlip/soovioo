import { Injectable, inject } from '@angular/core';
import { ConfirmationService, MenuItem } from '@openng/optimus-ui/api';
import { DemoApiService } from '@soovioo/shared';
import { NotifyService } from '../notify.service';
import { switchMap } from 'rxjs';
import { demoEntryUrl, schoolUrl } from '../school-context';

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
        label: 'Vygenerovat nová ukázková data',
        icon: 'pi pi-refresh',
        title: 'Znovu průvodce a čerstvá ukázková data, vaše změny zmizí',
        command: () => this.reset(slug),
      },
      {
        label: 'Vymazat data (prázdná škola)',
        icon: 'pi pi-eraser',
        title: 'Smaže všechna data kromě logů, zůstane jen účet vedení školy',
        command: () => this.clear(slug),
      },
      { separator: true },
      {
        label: 'Smazat celé demo',
        icon: 'pi pi-trash',
        title: 'Nevratně odstraní ukázkovou školu i s adresou',
        styleClass: 'demo-menu-danger',
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
      header: 'Vygenerovat nová ukázková data',
      message:
        'Projdete znovu průvodce a škola se naplní čerstvými ukázkovými daty — vaše změny i nahrané soubory zmizí. Datum smazání dema se nemění.',
      icon: 'pi pi-refresh',
      acceptLabel: 'Pokračovat',
      rejectLabel: 'Zrušit',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        window.location.assign(
          demoEntryUrl(`reset=${encodeURIComponent(slug)}`),
        ),
    });
  }

  /** Empty school to set up from scratch; logged in as the admin that's
   * left (the server dropped any other session). */
  clear(slug: string): void {
    this.confirmation.confirm({
      header: 'Vymazat data školy',
      message:
        'Opravdu chcete data školy vymazat? Data budou nenávratně odstraněna — třídy, žáci, rodiče, učitelé, známky, docházka, rozvrh, zprávy, akce, platby i nahrané soubory. Zůstanou jen logy a váš účet vedení školy, abyste mohli školu zaplnit od začátku.',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Vymazat data',
      rejectLabel: 'Zrušit',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.demoApi
          .clear(slug)
          .pipe(
            switchMap(({ adminUserId }) =>
              this.demoApi.login(slug, adminUserId),
            ),
          )
          .subscribe({
            next: () => window.location.assign(schoolUrl(slug, 'home')),
            error: (err) => this.notify.actionFailed('demo clear failed', err),
          }),
    });
  }

  delete(slug: string): void {
    this.confirmation.confirm({
      header: 'Smazat celé demo',
      message:
        'Ukázková škola se smaže i se všemi daty a logy, nevratně, a její adresa přestane fungovat. Kdykoli si můžete vytvořit novou.',
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
