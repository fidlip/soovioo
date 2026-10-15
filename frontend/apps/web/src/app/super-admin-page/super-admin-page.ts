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
import { CreateSchoolRequest, School, SchoolsApiService } from '@sova/shared';

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

  protected error: string | null = null;

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
    };
    this.schoolsApi.addSchool(request).subscribe({
      next: () => {
        this.newSchoolName = '';
        this.newSchoolSlug = '';
        this.newAdminUsername = '';
        this.newAdminPassword = '';
        this.error = null;
        this.reload();
      },
      error: (err) => {
        console.error('addSchool failed', err);
        this.error = (err as { error?: string })?.error ?? 'Něco se nepovedlo.';
      },
    });
  }
}
