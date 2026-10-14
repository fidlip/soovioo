import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL, Grade, Student, Subject } from '@sova/shared';
import { App } from './app';

describe('App', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('renders grades loaded from the API', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const grades: Grade[] = [
      {
        id: 1,
        studentId: 1,
        studentName: 'Jan Novák',
        subjectId: 1,
        subjectName: 'Matematika',
        value: 1,
        createdAt: new Date().toISOString(),
      },
    ];
    httpMock.expectOne('http://test/api/grades').flush(grades);

    const students: Student[] = [
      { id: 1, firstName: 'Jan', lastName: 'Novák', schoolClassId: 1, schoolClassName: '5.A' },
    ];
    httpMock.expectOne('http://test/api/students').flush(students);

    const subjects: Subject[] = [{ id: 1, name: 'Matematika' }];
    httpMock.expectOne('http://test/api/subjects').flush(subjects);

    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Jan Novák');
  });
});
