import { TestBed } from '@angular/core/testing';
import { AvailabilitySlot } from '@soovioo/shared';
import { TeacherAvailability } from './teacher-availability';

describe('TeacherAvailability', () => {
  function create(value: AvailabilitySlot[] = []) {
    const fixture = TestBed.createComponent(TeacherAvailability);
    fixture.componentRef.setInput('periods', [1, 2, 3]);
    fixture.componentRef.setInput('value', value);
    fixture.detectChanges();
    const button = (label: string) =>
      fixture.nativeElement.querySelector(
        `button[aria-label^="${label}"]`,
      ) as HTMLButtonElement;
    const click = (label: string) => {
      button(label).click();
      fixture.detectChanges();
    };
    return { fixture, button, click };
  }

  it('cycles a period through může, raději ne and nemůže', () => {
    const { fixture, button, click } = create();
    const value = () => fixture.componentInstance.value();

    expect(button('pondělí 1. hod.').getAttribute('aria-label')).toBe(
      'pondělí 1. hod.: může',
    );
    click('pondělí 1. hod.');
    expect(value()).toEqual([{ day: 'Monday', period: 1, kind: 'Avoid' }]);
    click('pondělí 1. hod.');
    expect(value()).toEqual([
      { day: 'Monday', period: 1, kind: 'Unavailable' },
    ]);
    expect(button('pondělí 1. hod.').getAttribute('aria-label')).toBe(
      'pondělí 1. hod.: nemůže',
    );
    click('pondělí 1. hod.');
    expect(value()).toEqual([]);
  });

  it('a day off covers its periods and gives them back', () => {
    const { fixture, button, click } = create([
      { day: 'Tuesday', period: 2, kind: 'Avoid' },
    ]);
    const value = () => fixture.componentInstance.value();

    click('Celý den nemůže — úterý');

    expect(value()).toEqual([
      { day: 'Tuesday', period: null, kind: 'Unavailable' },
    ]);
    expect(button('Celý den nemůže — úterý').getAttribute('aria-pressed')).toBe(
      'true',
    );
    expect(button('úterý 1. hod.').disabled).toBe(true);
    expect(button('úterý 1. hod.').getAttribute('aria-label')).toBe(
      'úterý 1. hod.: nemůže',
    );

    click('Celý den nemůže — úterý');

    expect(value()).toEqual([]);
    expect(button('úterý 1. hod.').disabled).toBe(false);
  });
});
