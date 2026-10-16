import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { Password } from '@openng/optimus-ui/password';
import { TableModule } from '@openng/optimus-ui/table';
import { CreateSchoolRequest, School, SchoolsApiService, UpdateSchoolRequest } from '@sova/shared';

@Component({
  selector: 'app-super-admin-page',
  imports: [DatePipe, FormsModule, Button, Card, InputText, Message, Password, PrimeTemplate, TableModule],
  templateUrl: './super-admin-page.html',
})
export class SuperAdminPage {
  private readonly schoolsApi = inject(SchoolsApiService);

  protected readonly schools = signal<School[]>([]);

  protected newSchoolName = '';
  protected newSchoolSlug = '';
  protected newAdminUsername = '';
  protected newAdminPassword = '';
  protected newSchoolIzo = '';
  protected newSchoolRedIzo = '';

  protected editingSchoolId: number | null = null;
  protected editName = '';
  protected editSlug = '';

  /** A signal, not a plain field — this app has no zone.js polyfill, so a
   * plain-field write inside an RxJS subscribe callback doesn't reliably
   * schedule change detection on its own. */
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.reload();
  }

  private reload(): void {
    this.schoolsApi.getSchools().subscribe({
      next: (schools) => this.schools.set(schools),
      error: (err) => console.error('getSchools failed', err),
    });
  }

  protected addSchool(): void {
    if (
      !this.newSchoolName.trim() ||
      !this.newSchoolSlug.trim() ||
      !this.newAdminUsername.trim() ||
      !this.newAdminPassword.trim()
    ) {
      return;
    }
    const request: CreateSchoolRequest = {
      name: this.newSchoolName,
      slug: this.newSchoolSlug,
      adminUsername: this.newAdminUsername,
      adminPassword: this.newAdminPassword,
      izo: this.newSchoolIzo || undefined,
      redIzo: this.newSchoolRedIzo || undefined,
    };
    this.schoolsApi.addSchool(request).subscribe({
      next: () => {
        this.newSchoolName = '';
        this.newSchoolSlug = '';
        this.newAdminUsername = '';
        this.newAdminPassword = '';
        this.newSchoolIzo = '';
        this.newSchoolRedIzo = '';
        this.error.set(null);
        this.reload();
      },
      error: (err) => {
        console.error('addSchool failed', err);
        this.error.set((err as { error?: string })?.error ?? 'Něco se nepovedlo.');
      },
    });
  }

  protected startEdit(school: School): void {
    this.editingSchoolId = school.id;
    this.editName = school.name;
    this.editSlug = school.slug;
    this.error.set(null);
  }

  protected cancelEdit(): void {
    this.editingSchoolId = null;
  }

  protected saveEdit(): void {
    if (this.editingSchoolId === null || !this.editName.trim() || !this.editSlug.trim()) {
      return;
    }
    const request: UpdateSchoolRequest = { name: this.editName, slug: this.editSlug };
    this.schoolsApi.updateSchool(this.editingSchoolId, request).subscribe({
      next: () => {
        this.editingSchoolId = null;
        this.error.set(null);
        this.reload();
      },
      error: (err) => {
        console.error('updateSchool failed', err);
        this.error.set((err as { error?: string })?.error ?? 'Něco se nepovedlo.');
      },
    });
  }

  protected deleteSchool(school: School): void {
    this.schoolsApi.deleteSchool(school.id).subscribe({
      next: () => {
        this.error.set(null);
        this.reload();
      },
      error: (err) => {
        console.error('deleteSchool failed', err);
        this.error.set((err as { error?: string })?.error ?? 'Něco se nepovedlo.');
      },
    });
  }
}
