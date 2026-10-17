/** "Everyone": every student in the school. "Selected": the union of
 * whatever recipient ids are set — no "OnlyMe" (unlike EventAudience), a
 * payment always has an actual payer. */
export type PaymentAudience = 'Everyone' | 'Selected';

/** FixedPerStudent: every targeted student owes `amount` as-is.
 * TotalSplitEvenly: `amount` is the total, split evenly (remainder cents
 * aren't redistributed). */
export type PaymentAmountType = 'FixedPerStudent' | 'TotalSplitEvenly';

/** How often a new installment is generated. None = a single one-off batch,
 * generated synchronously at creation. */
export type PaymentRecurrence = 'None' | 'Monthly' | 'Semiannually';

export interface Payment {
  id: number;
  title: string;
  description?: string;
  /** "YYYY-MM-DD" (DateOnly on the backend) — due date of the first/only installment. */
  dueDate: string;
  amountType: PaymentAmountType;
  amount: number;
  recurrence: PaymentRecurrence;
  /** "YYYY-MM-DD", only meaningful when recurrence isn't "None". */
  recurrenceEndDate?: string;
  audience: PaymentAudience;
  schoolClassIds: number[];
  parentUserIds: number[];
  studentIds: number[];
  /** Already filtered to what the caller may see — see PaymentsController.GetPayments. */
  recipients: PaymentRecipient[];
}

export interface PaymentRecipient {
  id: number;
  studentId: number;
  studentName: string;
  /** Frozen per-student amount, computed once at generation time. */
  amount: number;
  /** Per-payment override, or null = fall back to the student's own default. */
  variableSymbol: string | null;
  /** `variableSymbol ?? ` the student's default — resolved server-side,
   * "" if neither is set (no QR code is rendered in that case). */
  effectiveVariableSymbol: string;
  /** "YYYY-MM-DD" — which installment batch this belongs to. */
  installmentDueDate: string;
  paid: boolean;
  paidAt?: string;
}

export interface CreatePaymentRequest {
  title: string;
  description?: string;
  dueDate: string;
  amountType: PaymentAmountType;
  amount: number;
  recurrence: PaymentRecurrence;
  audience: PaymentAudience;
  recurrenceEndDate?: string;
  schoolClassIds?: number[];
  parentUserIds?: number[];
  studentIds?: number[];
}

export interface SetPaymentRecipientPaidRequest {
  paid: boolean;
}

export interface SetPaymentRecipientVariableSymbolRequest {
  variableSymbol: string | null;
}

export interface PaymentSchoolClassOption {
  id: number;
  name: string;
  rocnik?: number;
}

export interface PaymentPersonOption {
  id: number;
  username: string;
}

export interface PaymentStudentOption {
  id: number;
  firstName: string;
  lastName: string;
  schoolClassId: number;
  schoolClassName: string;
}

/** What the current caller (Teacher/Admin) may pick as payment recipients —
 * scoped server-side per role, same restriction as Events' RecipientOptions
 * (no teachers list — a Payment never targets teachers). */
export interface PaymentRecipientOptions {
  classes: PaymentSchoolClassOption[];
  parents: PaymentPersonOption[];
  students: PaymentStudentOption[];
}

export interface PaymentBankAccount {
  bankAccountNumber: string | null;
}
