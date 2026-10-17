import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  CreatePaymentRequest,
  Payment,
  PaymentBankAccount,
  PaymentRecipient,
  PaymentRecipientOptions,
  SetPaymentRecipientPaidRequest,
  SetPaymentRecipientVariableSymbolRequest,
} from './payment.model';

@Injectable({ providedIn: 'root' })
export class PaymentsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Unscoped, like BehaviorApiService.getBehaviorRecords — Parent/Student
   * see only their own accessible students' recipient rows (see
   * PaymentsController.GetPayments). */
  getPayments(): Observable<Payment[]> {
    return this.http.get<Payment[]>(`${this.baseUrl}/api/payments`);
  }

  /** Available to every role — needed to render a QR code even for a
   * Parent/Student, unlike MySchoolApiService's Admin-only settings. */
  getBankAccount(): Observable<PaymentBankAccount> {
    return this.http.get<PaymentBankAccount>(
      `${this.baseUrl}/api/payments/bank-account`,
    );
  }

  /** Scoped server-side to what the caller (Teacher/Admin) may target. */
  getRecipientOptions(): Observable<PaymentRecipientOptions> {
    return this.http.get<PaymentRecipientOptions>(
      `${this.baseUrl}/api/payments/recipient-options`,
    );
  }

  addPayment(request: CreatePaymentRequest): Observable<Payment> {
    return this.http.post<Payment>(`${this.baseUrl}/api/payments`, request);
  }

  deletePayment(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/payments/${id}`);
  }

  /** Teacher/Admin-only — see PaymentRecipient.PaidAt's doc comment. */
  setRecipientPaid(
    id: number,
    request: SetPaymentRecipientPaidRequest,
  ): Observable<PaymentRecipient> {
    return this.http.put<PaymentRecipient>(
      `${this.baseUrl}/api/payments/recipients/${id}/paid`,
      request,
    );
  }

  setRecipientVariableSymbol(
    id: number,
    request: SetPaymentRecipientVariableSymbolRequest,
  ): Observable<PaymentRecipient> {
    return this.http.put<PaymentRecipient>(
      `${this.baseUrl}/api/payments/recipients/${id}/variable-symbol`,
      request,
    );
  }
}
