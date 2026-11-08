import { ConfirmationService, MessageService } from '@openng/optimus-ui/api';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import {
  API_BASE_URL,
  Grade,
  Student,
  Subject,
  TokenStorage,
} from '@soovioo/shared';
import { GradesPage } from './grades-page';

class FakeTokenStorage extends TokenStorage {
  getToken() {
    return null;
  }
  setToken() {
    /* unused */
  }
  clearToken() {
    /* unused */
  }
}

describe('GradesPage', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GradesPage],
      providers: [
        MessageService,
        ConfirmationService,
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
        { provide: TokenStorage, useValue: new FakeTokenStorage() },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const gradesRequest = () =>
    httpMock.expectOne(
      (r) => r.method === 'GET' && r.url === 'http://test/api/grades',
    );

  const grade = (id: number): Grade => ({
    id,
    studentId: 1,
    studentName: 'Jan Novák',
    subjectId: 1,
    subjectName: 'Matematika',
    value: 1,
    weight: 1,
    gradedOn: '2026-10-18',
    createdAt: '2026-10-18T08:00:00Z',
  });

  /** The page's protected handlers, called as the template would. */
  const component = (fixture: { componentInstance: GradesPage }) =>
    fixture.componentInstance as unknown as {
      filterStudentId: { set(id: number | null): void };
      onFilterChange(): void;
      clearFilters(): void;
      onPageChange(state: { page: number; rows: number }): void;
      deleteGrade(grade: Grade): void;
    };

  it('renders grades loaded from the API', async () => {
    const fixture = TestBed.createComponent(GradesPage);
    fixture.detectChanges();

    const grades: Grade[] = [
      {
        id: 1,
        studentId: 1,
        studentName: 'Jan Novák',
        subjectId: 1,
        subjectName: 'Matematika',
        value: 1,
        weight: 1,
        gradedOn: '2026-10-18',
        createdAt: new Date().toISOString(),
      },
    ];
    gradesRequest().flush({
      items: grades,
      totalCount: 1,
      page: 1,
      pageSize: 50,
    });

    const students: Student[] = [
      {
        id: 1,
        firstName: 'Jan',
        lastName: 'Novák',
        schoolClassId: 1,
        schoolClassName: '5.A',
      },
    ];
    httpMock.expectOne('http://test/api/students').flush(students);

    const subjects: Subject[] = [{ id: 1, name: 'Matematika' }];
    httpMock.expectOne('http://test/api/subjects').flush(subjects);

    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Matematika');
    expect(compiled.querySelector('p-paginator')).toBeNull();
  });

  describe('with more grades than a page', () => {
    function open() {
      const fixture = TestBed.createComponent(GradesPage);
      fixture.detectChanges();
      httpMock.expectOne('http://test/api/students').flush([]);
      httpMock.expectOne('http://test/api/subjects').flush([]);
      return fixture;
    }
    const page = (id: number, totalCount: number, items = [grade(id)]) => ({
      items,
      totalCount,
      page: 1,
      pageSize: 50,
    });

    it('offers pages and asks for the one chosen', () => {
      const fixture = open();
      const first = gradesRequest();
      expect(first.request.params.get('page')).toBe('1');
      expect(first.request.params.get('pageSize')).toBe('50');
      first.flush(page(1, 120));
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('p-paginator')).not.toBeNull();

      component(fixture).onPageChange({ page: 2, rows: 20 });
      const third = gradesRequest();
      expect(third.request.params.get('page')).toBe('3');
      expect(third.request.params.get('pageSize')).toBe('20');
      third.flush(page(2, 120));
    });

    it('a filter asks the server and starts from the first page', () => {
      const fixture = open();
      gradesRequest().flush(page(1, 120));
      component(fixture).onPageChange({ page: 1, rows: 50 });
      gradesRequest().flush(page(2, 120));

      component(fixture).filterStudentId.set(7);
      component(fixture).onFilterChange();
      const filtered = gradesRequest();
      expect(filtered.request.params.get('studentId')).toBe('7');
      expect(filtered.request.params.get('page')).toBe('1');
      filtered.flush(page(3, 1));
    });

    it('clearing the filters asks the server for everything again', () => {
      const fixture = open();
      gradesRequest().flush(page(1, 120));
      component(fixture).filterStudentId.set(7);
      component(fixture).onFilterChange();
      gradesRequest().flush(page(2, 1));

      component(fixture).clearFilters();
      const cleared = gradesRequest();
      expect(cleared.request.params.has('studentId')).toBe(false);
      expect(cleared.request.params.get('page')).toBe('1');
      cleared.flush(page(1, 120));
    });

    it('a slower answer to an older filter does not replace the newer one', () => {
      const fixture = open();
      gradesRequest().flush(page(1, 120));

      component(fixture).filterStudentId.set(7);
      component(fixture).onFilterChange();
      const older = gradesRequest();
      component(fixture).filterStudentId.set(8);
      component(fixture).onFilterChange();

      expect(older.cancelled).toBe(true);
      gradesRequest().flush(page(8, 1));
    });

    it('after deleting the only grade of the last page shows the page before it', () => {
      const fixture = open();
      gradesRequest().flush(page(1, 101));
      component(fixture).onPageChange({ page: 2, rows: 50 });
      gradesRequest().flush(page(101, 101));

      component(fixture).deleteGrade(grade(101));
      httpMock.expectOne('http://test/api/grades/101').flush(null);
      gradesRequest().flush({ ...page(0, 100), items: [] });

      const previous = gradesRequest();
      expect(previous.request.params.get('page')).toBe('2');
      previous.flush(page(100, 100));
    });
  });
});
