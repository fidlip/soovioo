import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import QRCode from 'qrcode';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { Card } from '@openng/optimus-ui/card';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message as PMessage } from '@openng/optimus-ui/message';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import { Textarea } from '@openng/optimus-ui/textarea';
import {
  AuthApiService,
  CreatePaymentRequest,
  Payment,
  PaymentAmountType,
  PaymentAudience,
  PaymentPersonOption,
  PaymentRecipient,
  PaymentRecipientOptions,
  PaymentRecurrence,
  PaymentSchoolClassOption,
  PaymentStudentOption,
  PaymentsApiService,
  localizeStudentTerm,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { TermPipe } from '../term.pipe';
import { NotifyService } from '../notify.service';

type AudienceOption = { value: PaymentAudience; label: string };
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
    Checkbox,
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
  protected readonly audienceOptions = AUDIENCE_OPTIONS.map((o) => ({
    ...o,
    label: this.term(o.label),
  }));
  protected readonly amountTypeOptions = AMOUNT_TYPE_OPTIONS.map((o) => ({
    ...o,
    label: this.term(o.label),
  }));
  protected readonly recurrenceOptions = RECURRENCE_OPTIONS;

  protected readonly payments = signal<Payment[]>([]);
  protected readonly recipientOptions = signal<PaymentRecipientOptions | null>(
    null,
  );
  protected readonly bankAccountNumber = signal<string | null>(null);
  protected readonly expandedPaymentIds = signal<number[]>([]);
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
  protected newAudience: PaymentAudience = 'Everyone';
  protected selectedClassIds: number[] = [];
  protected selectedParentIds: number[] = [];
  protected selectedStudentIds: number[] = [];

  protected classFilterName = '';
  protected studentFilterName = '';
  protected parentFilterName = '';

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
    return this.authApi.isTeacherOrAdmin();
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

  protected qrDataUrlFor(recipientId: number): string | null {
    return this.qrDataUrls()[recipientId] ?? null;
  }

  protected filteredClasses(): PaymentSchoolClassOption[] {
    const name = this.classFilterName.trim().toLowerCase();
    return (this.recipientOptions()?.classes ?? []).filter(
      (c) => !name || c.name.toLowerCase().includes(name),
    );
  }

  protected filteredStudents(): PaymentStudentOption[] {
    const name = this.studentFilterName.trim().toLowerCase();
    return (this.recipientOptions()?.students ?? []).filter(
      (s) =>
        !name || `${s.firstName} ${s.lastName}`.toLowerCase().includes(name),
    );
  }

  protected filteredParents(): PaymentPersonOption[] {
    const name = this.parentFilterName.trim().toLowerCase();
    return (this.recipientOptions()?.parents ?? []).filter(
      (p) => !name || p.name.toLowerCase().includes(name),
    );
  }

  protected toggleClassId(id: number): void {
    this.selectedClassIds = this.selectedClassIds.includes(id)
      ? this.selectedClassIds.filter((x) => x !== id)
      : [...this.selectedClassIds, id];
  }

  protected toggleParentId(id: number): void {
    this.selectedParentIds = this.selectedParentIds.includes(id)
      ? this.selectedParentIds.filter((x) => x !== id)
      : [...this.selectedParentIds, id];
  }

  protected toggleStudentId(id: number): void {
    this.selectedStudentIds = this.selectedStudentIds.includes(id)
      ? this.selectedStudentIds.filter((x) => x !== id)
      : [...this.selectedStudentIds, id];
  }

  protected openCreateDialog(): void {
    this.formError.set(null);
    this.newTitle = '';
    this.newDescription = '';
    this.newDueDate = new Date();
    this.newAmountType = 'FixedPerStudent';
    this.newAmount = null;
    this.newRecurrence = 'None';
    this.newRecurrenceEndDate = null;
    this.newAudience = 'Everyone';
    this.selectedClassIds = [];
    this.selectedParentIds = [];
    this.selectedStudentIds = [];
    this.classFilterName = '';
    this.studentFilterName = '';
    this.parentFilterName = '';
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
      studentIds: this.selectedStudentIds,
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
    // Let the receipt block actually render (printTarget is a signal write,
    // picked up on the next change-detection pass) before invoking print.
    setTimeout(() => window.print(), 0);
  }

  /** "YYYY-MM-DD" (DateOnly on the backend) → "DD.MM.YYYY". */
  protected formatDate(date: string): string {
    const [year, month, day] = date.split('-');
    return `${day}.${month}.${year}`;
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
      next: (payments) => this.payments.set(payments),
      error: (err) => console.error('getPayments failed', err),
    });
  }
}
