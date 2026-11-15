import { SchoolYear, dayOffOn, dayOffSpans } from './school-year.model';

const year: SchoolYear = {
  id: 1,
  name: '2026/27',
  from: '2026-09-01',
  to: '2027-08-31',
  teachingStart: '2026-09-02',
  teachingEnd: '2027-06-30',
  current: true,
  daysOff: [
    {
      id: 1,
      from: '2026-10-29',
      to: '2026-10-30',
      name: 'Podzimní prázdniny',
      kind: 'Holidays',
      classIds: [],
    },
    {
      id: 2,
      from: '2026-11-16',
      to: '2026-11-16',
      name: 'Ředitelské volno',
      kind: 'DirectorsDay',
      classIds: [7],
    },
  ],
  periods: [],
};

describe('dayOffSpans', () => {
  it('adds the days of the year outside its lessons', () => {
    expect(dayOffSpans([year]).map((s) => [s.name, s.from, s.to])).toEqual([
      ['Hlavní prázdniny', '2026-09-01', '2026-09-01'],
      ['Podzimní prázdniny', '2026-10-29', '2026-10-30'],
      ['Ředitelské volno', '2026-11-16', '2026-11-16'],
      ['Hlavní prázdniny', '2027-07-01', '2027-08-31'],
    ]);
  });
});

describe('dayOffOn', () => {
  const spans = dayOffSpans([year]);

  it('finds the whole school’s days off and a class’s own', () => {
    expect(dayOffOn(spans, '2026-10-30')?.name).toBe('Podzimní prázdniny');
    expect(dayOffOn(spans, '2026-10-31')).toBeNull();
    expect(dayOffOn(spans, '2026-11-16')).toBeNull();
    expect(dayOffOn(spans, '2026-11-16', 8)).toBeNull();
    expect(dayOffOn(spans, '2026-11-16', 7)?.name).toBe('Ředitelské volno');
    expect(dayOffOn(spans, '2027-07-15', 7)?.name).toBe('Hlavní prázdniny');
  });
});
