import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { SchoolYear } from './school-year.model';
import { SchoolYearsApiService } from './school-years-api.service';
import { SchoolYearsStore } from './school-years.store';

function year(id: number): SchoolYear {
  return {
    id,
    name: '2026/27',
    from: '2026-09-01',
    to: '2027-08-31',
    teachingStart: '2026-09-01',
    teachingEnd: '2027-06-30',
    current: true,
    daysOff: [],
    periods: [],
  };
}

describe('SchoolYearsStore', () => {
  let responses: Subject<SchoolYear[]>[];
  let store: SchoolYearsStore;

  beforeEach(() => {
    responses = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: SchoolYearsApiService,
          useValue: {
            getSchoolYears: () => {
              const response = new Subject<SchoolYear[]>();
              responses.push(response);
              return response;
            },
          },
        },
      ],
    });
    store = TestBed.inject(SchoolYearsStore);
  });

  it('drops the years asked for before the account switched', () => {
    store.ensure();
    store.clear();
    store.ensure();

    responses[1].next([year(2)]);
    responses[0].next([year(1)]);

    expect(responses.length).toBe(2);
    expect(store.years()?.map((y) => y.id)).toEqual([2]);
  });

  it('keeps the years while they load again', () => {
    store.ensure();
    responses[0].next([year(1)]);

    store.reload();
    expect(store.years()?.map((y) => y.id)).toEqual([1]);
    responses[1].next([year(3)]);
    expect(store.years()?.map((y) => y.id)).toEqual([3]);
  });
});
