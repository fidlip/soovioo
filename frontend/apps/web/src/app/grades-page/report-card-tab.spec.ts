import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ConfirmationService, MessageService } from '@openng/optimus-ui/api';
import {
  API_BASE_URL,
  AuthApiService,
  ReportCard,
  ReportCardSubject,
  Student,
  Subject,
} from '@soovioo/shared';
import { ReportCardTab, halfYearOf } from './report-card-tab';

describe('halfYearOf', () => {
  it('puts September to January in the first half, the rest in the second', () => {
    expect(halfYearOf(new Date(2026, 8, 1))).toEqual({
      schoolYearStartYear: 2026,
      halfYear: 'First',
    });
    expect(halfYearOf(new Date(2027, 0, 31))).toEqual({
      schoolYearStartYear: 2026,
      halfYear: 'First',
    });
    expect(halfYearOf(new Date(2027, 1, 1))).toEqual({
      schoolYearStartYear: 2026,
      halfYear: 'Second',
    });
    expect(halfYearOf(new Date(2027, 7, 31))).toEqual({
      schoolYearStartYear: 2026,
      halfYear: 'Second',
    });
  });
});

describe('ReportCardTab', () => {
  let httpMock: HttpTestingController;
  let staff: boolean;

  const student = (id: number, classId: number, className: string) =>
    ({
      id,
      firstName: 'Jan',
      lastName: `Novák ${id}`,
      schoolClassId: classId,
      schoolClassName: className,
    }) as Student;

  const maths: ReportCardSubject = {
    subjectId: 1,
    subjectName: 'Matematika',
    average: 1.5,
    gradeCount: 2,
    suggested: 2,
    kind: 'Graded',
    value: 2,
    changed: false,
    confirmed: false,
    confirmedByName: null,
    confirmedAt: null,
    mine: true,
    canEdit: true,
    canReopen: false,
  };

  const card = (subjects: ReportCardSubject[], issued = true): ReportCard => ({
    studentId: 7,
    studentName: 'Jan Novák',
    gender: 'Male',
    schoolClassId: 2,
    schoolClassName: '5.A',
    behavior: 'VelmiDobre',
    excusedHours: 3,
    unexcusedHours: 1,
    result: null,
    issuedOn: '2027-01-31',
    issued,
    subjects,
  });

  beforeEach(async () => {
    staff = true;
    await TestBed.configureTestingModule({
      imports: [ReportCardTab],
      providers: [
        MessageService,
        ConfirmationService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
        {
          provide: AuthApiService,
          useValue: { isStaff: () => staff, getSchoolType: () => 'Elementary' },
        },
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const render = (students: Student[]) => {
    const fixture = TestBed.createComponent(ReportCardTab);
    fixture.componentRef.setInput('students', students);
    fixture.componentRef.setInput('subjects', [] as Subject[]);
    fixture.detectChanges();
    return fixture;
  };

  const reportCardsRequest = () =>
    httpMock.expectOne(
      (r) => r.method === 'GET' && r.url === 'http://test/api/report-cards',
    );

  const classes = (
    fixture: { detectChanges(): void },
    all: boolean,
    ...list: [number, string, boolean][]
  ) => {
    httpMock.expectOne('http://test/api/report-cards/classes').flush({
      all,
      classes: list.map(([id, name, own]) => ({ id, name, own })),
    });
    fixture.detectChanges();
  };

  const buttonLabelled = (element: HTMLElement, label: string) =>
    [...element.querySelectorAll('button')].find(
      (b) => b.getAttribute('aria-label') === label,
    ) as HTMLButtonElement;

  it("shows a teacher their own class's current half-year, with the suggestion to confirm", () => {
    const fixture = render([student(7, 2, '5.A'), student(8, 1, '4.B')]);
    classes(fixture, false, [1, '4.B', false], [2, '5.A', true]);

    const request = reportCardsRequest();
    const current = halfYearOf(new Date());
    expect(request.request.params.get('schoolClassId')).toBe('2');
    expect(request.request.params.get('halfYear')).toBe(current.halfYear);
    expect(request.request.params.get('schoolYearStartYear')).toBe(
      `${current.schoolYearStartYear}`,
    );
    request.flush([card([maths])]);
    fixture.detectChanges();

    const text = (
      (fixture.nativeElement as HTMLElement).textContent ?? ''
    ).replace(/\s+/g, ' ');
    expect(text).toContain('průměr 1,50 · počet známek: 2');
    expect(text).toContain('návrh z průměru');
    expect(text).toContain('Potvrdit zobrazené');
    expect(text).toContain('Celkové hodnocení: neuzavřeno');
  });

  it('confirms a grade and shows the report cards again', () => {
    const fixture = render([student(7, 2, '5.A')]);
    classes(fixture, true, [2, '5.A', false]);
    reportCardsRequest().flush([card([maths])]);
    fixture.detectChanges();

    const confirm = [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ].find(
      (b) =>
        b.getAttribute('aria-label') === 'Potvrdit – Matematika, Jan Novák',
    );
    confirm?.click();

    const request = httpMock.expectOne(
      'http://test/api/report-cards/grades/confirm',
    );
    expect(request.request.body.grades).toEqual([
      { studentId: 7, subjectId: 1 },
    ]);
    request.flush({ confirmed: 1, skipped: 0 });
    reportCardsRequest().flush([
      card([{ ...maths, confirmed: true, canEdit: false }]),
    ]);
  });

  it("shows all of a teacher's classes at once, filtered to theirs and the unconfirmed", () => {
    const fixture = render([student(7, 2, '5.A')]);
    classes(fixture, false, [1, '4.B', false], [2, '5.A', false]);
    reportCardsRequest().flush([]);
    const component = fixture.componentInstance as unknown as {
      onClassChange(id: number): void;
      onlyMine: { set(value: boolean): void };
      onlyUnconfirmed: { set(value: boolean): void };
    };

    component.onClassChange(0);
    fixture.detectChanges();
    const request = reportCardsRequest();
    expect(request.request.params.has('schoolClassId')).toBe(false);
    const czech = { ...maths, subjectId: 2, subjectName: 'Český jazyk' };
    const music = {
      ...maths,
      subjectId: 3,
      subjectName: 'Hudební výchova',
      mine: false,
      canEdit: false,
    };
    request.flush([
      card([{ ...maths, confirmed: true, canEdit: false }, czech, music]),
    ]);
    const shown = (mine: boolean, unconfirmed: boolean) => {
      component.onlyMine.set(mine);
      component.onlyUnconfirmed.set(unconfirmed);
      fixture.detectChanges();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      return ['Matematika', 'Český jazyk', 'Hudební výchova'].filter((name) =>
        text.includes(name),
      );
    };

    expect(shown(true, false)).toEqual(['Matematika', 'Český jazyk']);
    expect(shown(false, true)).toEqual(['Český jazyk', 'Hudební výchova']);
    expect(shown(true, true)).toEqual(['Český jazyk']);
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain(
      'PDF za třídu',
    );
  });

  it("opens the pupil's grades in the subject from the grade count", () => {
    const fixture = render([student(7, 2, '5.A')]);
    classes(fixture, true, [2, '5.A', false]);
    reportCardsRequest().flush([card([maths])]);
    fixture.detectChanges();
    let shown: unknown;
    fixture.componentInstance.showGrades.subscribe((of) => (shown = of));

    buttonLabelled(
      fixture.nativeElement,
      'Známky – Matematika, Jan Novák',
    ).click();

    expect(shown).toEqual({ studentId: 7, subjectId: 1 });
  });

  it('tells the family from when they see the report card', () => {
    staff = false;
    const fixture = render([student(7, 2, '5.A')]);
    const request = reportCardsRequest();
    expect(request.request.params.has('schoolClassId')).toBe(false);
    request.flush([
      card(
        [
          {
            ...maths,
            suggested: null,
            kind: null,
            value: null,
            canEdit: false,
          },
        ],
        false,
      ),
    ]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Známky na vysvědčení uvidíte od');
    expect(text).not.toContain('Celkové hodnocení');
    expect(text).not.toContain('návrh z průměru');
  });
});
