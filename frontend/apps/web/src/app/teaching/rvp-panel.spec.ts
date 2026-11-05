import { TestBed } from '@angular/core/testing';
import { TeachingApiService, TeachingOverview } from '@soovioo/shared';
import { NotifyService } from '../notify.service';
import { RvpPanel, rvpAreaChoice } from './rvp-panel';

const subject = { subjectId: 1, subjectName: 'Matematika' };

describe('rvpAreaChoice', () => {
  it('is automatic until the school chooses, whatever was recognised', () => {
    expect(
      rvpAreaChoice({ ...subject, areaKey: 'matematika', chosen: false }),
    ).toBe('automatic');
    expect(rvpAreaChoice({ ...subject, areaKey: null, chosen: false })).toBe(
      'automatic',
    );
  });

  it('is the chosen area, or none for disponibilní hours only', () => {
    expect(
      rvpAreaChoice({ ...subject, areaKey: 'matematika', chosen: true }),
    ).toBe('matematika');
    expect(rvpAreaChoice({ ...subject, areaKey: null, chosen: true })).toBe(
      'none',
    );
  });
});

describe('RvpPanel', () => {
  it('says each hours once', () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: TeachingApiService, useValue: {} },
        { provide: NotifyService, useValue: {} },
      ],
    });
    const fixture = TestBed.createComponent(RvpPanel);
    fixture.componentRef.setInput('overview', {
      versionId: 1,
      canEdit: true,
      isDraft: true,
      rvp: {
        classesWithout: [],
        programs: [
          {
            code: 'ZV2021',
            name: 'Základní vzdělávání',
            classes: ['1.A'],
            problems: [],
            subjects: [],
            areas: [],
            stages: [
              {
                label: '1. stupeň',
                fromGrade: 1,
                toGrade: 5,
                missingGrades: [],
                total: 118,
                have: 118,
                disponible: 16,
                disponibleUsed: 16,
                areas: [],
              },
            ],
          },
        ],
      },
    } as unknown as TeachingOverview);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent.replace(/\s+/g, ' ');
    expect(text).toContain('118 h z 118 h · disponibilní využito 16 h z 16 h');
    expect(text).not.toContain('h h');
  });
});
