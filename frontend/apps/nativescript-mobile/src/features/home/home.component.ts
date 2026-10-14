import { Component, NO_ERRORS_SCHEMA, inject, signal } from '@angular/core';
import { NativeScriptCommonModule } from '@nativescript/angular';
import { GradesApiService, Grade } from '@sova/shared';

@Component({
  selector: 'ns-home',
  templateUrl: './home.component.html',
  imports: [NativeScriptCommonModule],
  schemas: [NO_ERRORS_SCHEMA],
})
export class HomeComponent {
  private readonly gradesApi = inject(GradesApiService);

  protected readonly grades = signal<Grade[]>([]);

  constructor() {
    this.reload();
  }

  protected addDemoGrade(): void {
    this.gradesApi
      .addGrade({
        studentName: 'Jan Novák',
        subject: 'Matematika',
        value: 1,
        note: 'Pololetní práce',
      })
      .subscribe(() => this.reload());
  }

  private reload(): void {
    this.gradesApi.getGrades().subscribe((grades) => this.grades.set(grades));
  }
}
