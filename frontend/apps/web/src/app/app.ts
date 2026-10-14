import { Component, inject, signal } from '@angular/core';
import { GradesApiService, Grade } from '@sova/shared';

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
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
      .subscribe({
        next: () => this.reload(),
        error: (err) => console.error('addGrade failed', err),
      });
  }

  private reload(): void {
    this.gradesApi.getGrades().subscribe({
      next: (grades) => this.grades.set(grades),
      error: (err) => console.error('getGrades failed', err),
    });
  }
}
