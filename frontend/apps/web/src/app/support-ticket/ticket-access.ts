import {
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Tag } from '@openng/optimus-ui/tag';
import {
  DateFormatPipe,
  SupportAccessKey,
  SupportAccessKeyStatus,
  SupportApiService,
  SupportAsUserRef,
  SupportTicketDetail,
} from '@soovioo/shared';
import { Markdown, MarkdownEditor, NotifyService } from '@fidlip/soovioo-ui';
import { FORM_FIELDS, FloatField } from '../forms';
import { SupportAsUsersSelect } from '../support-as/support-as-users-select';

/** The support's request to enter the school for the ticket: the school's
 * admin approves its key (till when, as whom) or rejects it, and revokes it
 * any time. Without a key the school approved, nobody gets in. */
@Component({
  selector: 'app-ticket-access',
  imports: [
    Button,
    DateFormatPipe,
    DatePicker,
    Dialog,
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    Markdown,
    MarkdownEditor,
    SupportAsUsersSelect,
    Tag,
  ],
  templateUrl: './ticket-access.html',
})
export class TicketAccess {
  private readonly api = inject(SupportApiService);
  private readonly notify = inject(NotifyService);
  private readonly confirmation = inject(ConfirmationService);

  readonly ticketId = input.required<number>();
  readonly access = input.required<SupportAccessKey>();
  readonly readOnly = input(false);
  readonly changed = output<SupportTicketDetail>();

  protected readonly busy = signal(false);
  protected readonly mayDecide = computed(
    () => this.access().canDecide && !this.readOnly(),
  );
  /** The key no longer matters — shown small. */
  protected readonly over = computed(() =>
    ['Rejected', 'Revoked', 'Expired'].includes(this.access().status),
  );

  protected statusLabel(
    status: SupportAccessKeyStatus,
    usable: boolean,
  ): string {
    switch (status) {
      case 'Pending':
        return 'Čeká na vaše rozhodnutí';
      case 'Approved':
        return usable ? 'Schváleno' : 'Schválení vypršelo';
      case 'Rejected':
        return 'Zamítnuto';
      case 'Revoked':
        return 'Odvoláno';
      default:
        return 'Neplatí';
    }
  }

  protected statusSeverity(
    status: SupportAccessKeyStatus,
    usable: boolean,
  ): 'warn' | 'success' | 'secondary' {
    return status === 'Pending'
      ? 'warn'
      : status === 'Approved' && usable
        ? 'success'
        : 'secondary';
  }

  protected names(users: SupportAsUserRef[]): string {
    return users.map((u) => u.name).join(', ');
  }

  /** Approving: till when, as whom. */
  protected readonly approving = signal(false);
  protected until: Date = new Date();
  protected userIds: number[] = [];
  protected readonly minDate = new Date();

  protected openApprove(): void {
    const access = this.access();
    this.until = new Date(Date.now() + access.approvalMinutes * 60_000);
    this.userIds = access.requestedAs.map((u) => u.id);
    this.approving.set(true);
  }

  protected approve(): void {
    this.run(
      this.api.approveAccess(
        this.ticketId(),
        this.access().id,
        this.until.toISOString(),
        this.access().supportAs === 'Users' ? this.userIds : [],
      ),
      'Vstup podpory schválen.',
      () => this.approving.set(false),
    );
  }

  protected readonly rejecting = signal(false);
  protected rejectNote = '';

  protected openReject(): void {
    this.rejectNote = '';
    this.rejecting.set(true);
  }

  protected reject(): void {
    this.run(
      this.api.rejectAccess(
        this.ticketId(),
        this.access().id,
        this.rejectNote.trim(),
      ),
      'Vstup podpory zamítnut.',
      () => this.rejecting.set(false),
    );
  }

  protected revoke(): void {
    this.confirmation.confirm({
      header: 'Odvolat vstup podpory',
      message:
        'Klíč podpory přestane platit a podpora, je-li ve škole, z ní hned odejde.',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Odvolat',
      acceptButtonProps: { severity: 'danger' },
      rejectLabel: 'Zpět',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(
          this.api.revokeAccess(this.ticketId(), this.access().id),
          'Vstup podpory odvolán.',
        ),
    });
  }

  private run(
    call: ReturnType<SupportApiService['revokeAccess']>,
    success: string,
    then?: () => void,
  ): void {
    this.busy.set(true);
    call.subscribe({
      next: (detail) => {
        this.busy.set(false);
        then?.();
        this.notify.success(success);
        this.changed.emit(detail);
      },
      error: (err) => {
        this.busy.set(false);
        this.notify.actionFailed('support access action failed', err);
      },
    });
  }
}
