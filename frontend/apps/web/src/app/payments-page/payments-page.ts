import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import QRCode from 'qrcode';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message as PMessage } from '@openng/optimus-ui/message';
import { MultiSelect } from '@openng/optimus-ui/multiselect';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import { Textarea } from '@openng/optimus-ui/textarea';
import {
  AuthApiService,
  CreatePaymentRequest,
  DateFormatPipe,
  Payment,
  PaymentAmountType,
  PaymentAudience,
  PaymentRecipient,
  PaymentRecipientOptions,
  PaymentRecurrence,
  PaymentsApiService,
  formatAmount,
  formatBankAccount,
  localizeStudentTerm,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { TermPipe } from '../term.pipe';
import { NotifyService } from '../notify.service';
import {
  ClearFilters,
  FloatField,
  FORM_FIELDS,
  studentClasses,
  studentPickOptions,
} from '../forms';
import { Loading } from '../loading/loading';

type AudienceOption = { value: PaymentAudience; label: string };
type PaidFilter = 'all' | 'unpaid' | 'paid';

/** One payment as listed: only the recipients matching the filters, plus
 * the totals the row header shows. */
interface PaymentView {
  payment: Payment;
  recipients: PaymentRecipient[];
  totalAmount: number;
  unpaidAmount: number;
  allPaid: boolean;
}

const PAID_FILTER_OPTIONS: { value: PaidFilter; label: string }[] = [
  { value: 'all', label: 'Vše' },
  { value: 'unpaid', label: 'Nezaplaceno' },
  { value: 'paid', label: 'Zaplaceno' },
];
type AmountTypeOption = { value: PaymentAmountType; label: string };
type RecurrenceOption = { value: PaymentRecurrence; label: string };

const AUDIENCE_OPTIONS: AudienceOption[] = [
  { value: 'Everyone', label: 'Všichni žáci' },
  { value: 'Selected', label: 'Výběr' },
];

const AMOUNT_TYPE_OPTIONS: AmountTypeOption[] = [
  { value: 'FixedPerStudent', label: 'Pevná částka na žáka' },
  {
    value: 'TotalSplitEvenly',
    label: 'Celková částka rozpočítaná rovným dílem',
  },
];

const RECURRENCE_OPTIONS: RecurrenceOption[] = [
  { value: 'None', label: 'Jednorázová platba' },
  { value: 'Monthly', label: 'Měsíčně' },
  { value: 'Semiannually', label: 'Pololetně' },
];

const RECURRENCE_LABELS: Record<PaymentRecurrence, string> = {
  None: 'Jednorázová',
  Monthly: 'Měsíčně',
  Semiannually: 'Pololetně',
};

const AMOUNT_TYPE_LABELS: Record<PaymentAmountType, string> = {
  FixedPerStudent: 'Pevná částka na žáka',
  TotalSplitEvenly: 'Rozpočítáno rovným dílem',
};

@Component({
  selector: 'app-payments-page',
  imports: [
    ClearFilters,
    FloatField,
    DateFormatPipe,
    ...FORM_FIELDS,
    Loading,
    MultiSelect,
    TermPipe,
    AppIcon,
    Button,
    Card,
    DatePicker,
    Dialog,
    FormsModule,
    InputNumber,
    InputText,
    PMessage,
    Select,
    Tag,
    Textarea,
  ],
  templateUrl: './payments-page.html',
})
export class PaymentsPage {
  protected readonly notify = inject(NotifyService);
  private readonly paymentsApi = inject(PaymentsApiService);
  protected readonly authApi = inject(AuthApiService);

  private readonly term = (text: string) =>
    localizeStudentTerm(text, this.authApi.getSchoolType());
  /** "Všichni žáci" only for whom payments reach the whole school. */
  private readonly wholeSchool =
    this.authApi.scope('PaymentsWrite') === 'School';
  protected readonly audienceOptions = AUDIENCE_OPTIONS.filter(
    (o) => this.wholeSchool || o.value !== 'Everyone',
  ).map((o) => ({
    ...o,
    label: this.term(o.label),
  }));
  protected readonly amountTypeOptions = AMOUNT_TYPE_OPTIONS.map((o) => ({
    ...o,
    label: this.term(o.label),
  }));
  protected readonly recurrenceOptions = RECURRENCE_OPTIONS;

  protected readonly payments = signal<Payment[]>([]);
  protected readonly paymentsLoaded = signal(false);
  protected readonly recipientOptions = signal<PaymentRecipientOptions | null>(
    null,
  );
  protected readonly bankAccountNumber = signal<string | null>(null);
  protected readonly expandedPaymentIds = signal<number[]>([]);

  protected readonly paidFilterOptions = PAID_FILTER_OPTIONS;
  protected readonly paidFilter = signal<PaidFilter>('all');
  protected readonly classFilter = signal<number | null>(null);
  protected readonly studentFilter = signal<number | null>(null);

  protected clearFilters(): void {
    this.paidFilter.set('all');
    this.classFilter.set(null);
    this.studentFilter.set(null);
  }

  /** Students appearing in the payments (works for parents too, who have
   * no recipient options) — for the "Žák" filter. */
  protected readonly studentFilterOptions = computed(() => {
    const byId = new Map<number, string>();
    for (const payment of this.payments()) {
      for (const r of payment.recipients) {
        byId.set(r.studentId, r.studentName);
      }
    }
    return [...byId]
      .map(([id, name]) => ({ id, name }))
      .filter((s) => {
        const classId = this.classFilter();
        return classId === null || this.studentClassId(s.id) === classId;
      })
      .sort((a, b) => a.name.localeCompare(b.name, 'cs'));
  });

  /** Payments sorted by due date (oldest first), recipients filtered by
   * paid state / class / student; payments with no matching recipient are
   * hidden while a filter is active. */
  protected readonly paymentViews = computed<PaymentView[]>(() => {
    const paid = this.paidFilter();
    const classId = this.classFilter();
    const studentId = this.studentFilter();
    const filtering = paid !== 'all' || classId !== null || studentId !== null;
    return [...this.payments()]
      .sort((a, b) => a.dueDate.localeCompare(b.dueDate))
      .map((payment) => {
        const recipients = payment.recipients
          .filter(
            (r) =>
              (paid === 'all' || r.paid === (paid === 'paid')) &&
              (studentId === null || r.studentId === studentId) &&
              (classId === null ||
                this.studentClassId(r.studentId) === classId),
          )
          .sort(
            (a, b) =>
              a.installmentDueDate.localeCompare(b.installmentDueDate) ||
              a.studentName.localeCompare(b.studentName, 'cs'),
          );
        const totalAmount = sum(recipients.map((r) => r.amount));
        const unpaidAmount = sum(
          recipients.filter((r) => !r.paid).map((r) => r.amount),
        );
        return {
          payment,
          recipients,
          totalAmount,
          unpaidAmount,
          allPaid: recipients.length > 0 && unpaidAmount === 0,
        };
      })
      .filter((view) => !filtering || view.recipients.length > 0);
  });

  /** Recipient opened in the "how to pay" dialog, with a large QR code. */
  protected readonly detail = signal<{
    payment: Payment;
    recipient: PaymentRecipient;
    qr: string | null;
  } | null>(null);
  protected readonly qrDataUrls = signal<Record<number, string>>({});

  protected readonly dialogVisible = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected newTitle = '';
  protected newDescription = '';
  protected newDueDate: Date = new Date();
  protected newAmountType: PaymentAmountType = 'FixedPerStudent';
  protected newAmount: number | null = null;
  protected newRecurrence: PaymentRecurrence = 'None';
  protected newRecurrenceEndDate: Date | null = null;
  protected newAudience: PaymentAudience = this.wholeSchool
    ? 'Everyone'
    : 'Selected';
  protected selectedClassIds: number[] = [];
  protected selectedParentIds: number[] = [];
  protected readonly selectedStudentIds = signal<number[]>([]);
  /** Narrows the student list to one class. */
  protected readonly pickClassId = signal<number | null>(null);

  /** Set just before window.print() — the receipt block (print:block,
   * hidden otherwise) renders from this while everything else in the
   * component is print:hidden. Not a real separate route: ROADMAP only
   * asks for "a simple printable page (browser print)", not a fiscal
   * document. */
  protected readonly printTarget = signal<{
    payment: Payment;
    recipient: PaymentRecipient;
  } | null>(null);

  constructor() {
    this.reload();
    this.paymentsApi.getBankAccount().subscribe({
      next: (result) => this.bankAccountNumber.set(result.bankAccountNumber),
      error: (err) => console.error('getBankAccount failed', err),
    });
    if (this.canManagePayments()) {
      this.paymentsApi.getRecipientOptions().subscribe({
        next: (options) => this.recipientOptions.set(options),
        error: (err) => console.error('getRecipientOptions failed', err),
      });
    }

    // Generates every QR code the currently visible unpaid recipients need,
    // in one batch, whenever the payment list or bank account changes —
    // deliberately NOT computed inline in the template (an async
    // QRCode.toDataURL call per template evaluation would re-trigger on
    // every change-detection pass since nothing would be cached yet).
    effect(() => {
      const account = this.bankAccountNumber();
      const currentPayments = this.payments();
      if (!account) {
        return;
      }
      for (const payment of currentPayments) {
        for (const recipient of payment.recipients) {
          if (recipient.paid || this.qrDataUrls()[recipient.id]) {
            continue;
          }
          const spayd = this.buildSpayd(account, recipient);
          QRCode.toDataURL(spayd)
            .then((url) =>
              this.qrDataUrls.update((m) => ({ ...m, [recipient.id]: url })),
            )
            .catch((err) => console.error('QRCode.toDataURL failed', err));
        }
      }
    });
  }

  protected canManagePayments(): boolean {
    return this.authApi.can('PaymentsWrite');
  }

  protected recurrenceLabel(recurrence: PaymentRecurrence): string {
    return RECURRENCE_LABELS[recurrence];
  }

  protected amountTypeLabel(amountType: PaymentAmountType): string {
    return this.term(AMOUNT_TYPE_LABELS[amountType]);
  }

  protected isExpanded(paymentId: number): boolean {
    return this.expandedPaymentIds().includes(paymentId);
  }

  protected toggleExpanded(paymentId: number): void {
    this.expandedPaymentIds.update((ids) =>
      ids.includes(paymentId)
        ? ids.filter((id) => id !== paymentId)
        : [...ids, paymentId],
    );
  }

  protected classOfStudent(studentId: number): string | null {
    return (
      this.recipientOptions()?.students.find((s) => s.id === studentId)
        ?.schoolClassName ?? null
    );
  }

  private studentClassId(studentId: number): number | null {
    return (
      this.recipientOptions()?.students.find((s) => s.id === studentId)
        ?.schoolClassId ?? null
    );
  }

  protected readonly formatAmount = formatAmount;

  protected formattedBankAccount(): string | null {
    const account = this.bankAccountNumber();
    return account ? formatBankAccount(account) : null;
  }

  protected openDetail(payment: Payment, recipient: PaymentRecipient): void {
    this.detail.set({ payment, recipient, qr: null });
    const account = this.bankAccountNumber();
    if (!account || recipient.paid) {
      return;
    }
    QRCode.toDataURL(this.buildSpayd(account, recipient), {
      width: 320,
      margin: 1,
    })
      .then((qr) =>
        this.detail.update((d) =>
          d?.recipient.id === recipient.id ? { ...d, qr } : d,
        ),
      )
      .catch((err) => console.error('QRCode.toDataURL failed', err));
  }

  protected qrDataUrlFor(recipientId: number): string | null {
    return this.qrDataUrls()[recipientId] ?? null;
  }

  protected readonly studentClassOptions = computed(() =>
    studentClasses(this.recipientOptions()?.students ?? []),
  );
  protected readonly studentOptions = computed(() =>
    studentPickOptions(
      this.recipientOptions()?.students ?? [],
      this.pickClassId(),
      this.selectedStudentIds(),
    ),
  );

  protected openCreateDialog(): void {
    this.formError.set(null);
    this.newTitle = '';
    this.newDescription = '';
    this.newDueDate = new Date();
    this.newAmountType = 'FixedPerStudent';
    this.newAmount = null;
    this.newRecurrence = 'None';
    this.newRecurrenceEndDate = null;
    this.newAudience = this.wholeSchool ? 'Everyone' : 'Selected';
    this.selectedClassIds = [];
    this.selectedParentIds = [];
    this.selectedStudentIds.set([]);
    this.pickClassId.set(null);
    this.notify.clear();
    this.dialogVisible.set(true);
  }

  protected onDialogVisibleChange(visible: boolean): void {
    this.dialogVisible.set(visible);
  }

  protected save(): void {
    if (!this.newTitle.trim() || !this.newAmount || this.newAmount <= 0) {
      return;
    }
    this.formError.set(null);

    const request: CreatePaymentRequest = {
      title: this.newTitle,
      description: this.newDescription || undefined,
      dueDate: this.toDateOnlyString(this.newDueDate),
      amountType: this.newAmountType,
      amount: this.newAmount,
      recurrence: this.newRecurrence,
      recurrenceEndDate:
        this.newRecurrence !== 'None' && this.newRecurrenceEndDate
          ? this.toDateOnlyString(this.newRecurrenceEndDate)
          : undefined,
      audience: this.newAudience,
      schoolClassIds: this.selectedClassIds,
      parentUserIds: this.selectedParentIds,
      studentIds: this.selectedStudentIds(),
    };

    this.paymentsApi.addPayment(request).subscribe({
      next: () => {
        this.dialogVisible.set(false);
        this.reload();
      },
      error: (err) => {
        console.error('addPayment failed', err);
        this.formError.set('Vytvoření platby se nezdařilo.');
      },
    });
  }

  protected deletePayment(payment: Payment): void {
    this.paymentsApi.deletePayment(payment.id).subscribe({
      next: () => this.reload(),
      error: (err) => this.notify.actionFailed('deletePayment failed', err),
    });
  }

  protected setPaid(recipient: PaymentRecipient, paid: boolean): void {
    this.paymentsApi.setRecipientPaid(recipient.id, { paid }).subscribe({
      next: () => this.reload(),
      error: (err) => this.notify.actionFailed('setRecipientPaid failed', err),
    });
  }

  protected printReceipt(payment: Payment, recipient: PaymentRecipient): void {
    this.printTarget.set({ payment, recipient });
    // The detail dialog would otherwise print over the receipt.
    this.detail.set(null);
    // Let the receipt block actually render (printTarget is a signal write,
    // picked up on the next change-detection pass) before invoking print.
    setTimeout(() => window.print(), 0);
  }

  /** Local (not UTC) y-m-d. */
  private toDateOnlyString(date: Date): string {
    const year = date.getFullYear();
    const month = `${date.getMonth() + 1}`.padStart(2, '0');
    const day = `${date.getDate()}`.padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  /** SPAYD (Czech "QR platba") — "*"-delimited KEY:value pairs. ACC is the
   * bank account, AM the amount as a plain decimal, CC the currency, X-VS
   * the variable symbol — omitted entirely (not sent as an empty value)
   * when there's no effective VS for this recipient. */
  private buildSpayd(
    bankAccountNumber: string,
    recipient: PaymentRecipient,
  ): string {
    const parts = [
      'SPD',
      '1.0',
      `ACC:${bankAccountNumber}`,
      `AM:${recipient.amount.toFixed(2)}`,
      'CC:CZK',
    ];
    if (recipient.effectiveVariableSymbol) {
      parts.push(`X-VS:${recipient.effectiveVariableSymbol}`);
    }
    return parts.join('*');
  }

  private reload(): void {
    this.paymentsApi.getPayments().subscribe({
      next: (payments) => {
        this.payments.set(payments);
        this.paymentsLoaded.set(true);
      },
      error: (err) => {
        console.error('getPayments failed', err);
        this.paymentsLoaded.set(true);
      },
    });
  }
}

function sum(values: number[]): number {
  return values.reduce((total, value) => total + value, 0);
}
