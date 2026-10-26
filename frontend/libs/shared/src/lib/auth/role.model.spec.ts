import { PermissionInfo, scopeLabel, scopesFor } from './role.model';

describe('scopeLabel', () => {
  it('reads as whose', () => {
    expect(scopeLabel('ClassTeacher', 'Staff', 'GradesRead')).toBe('Své třídy');
    expect(scopeLabel('MyClasses', 'Staff', 'GradesRead')).toBe('Kde učí');
    expect(scopeLabel('School', 'Staff', 'GradesRead')).toBe('Celé školy');
    expect(scopeLabel('None', 'Parent', 'GradesRead')).toBe('—');
  });

  it("says whose a parent's or pupil's own is", () => {
    expect(scopeLabel('Own', 'Parent', 'GradesRead')).toBe('Svých dětí');
    expect(scopeLabel('Own', 'Student', 'GradesRead')).toBe('Své');
  });

  it("names the children's classes for what goes to whole classes", () => {
    for (const permission of [
      'EventsWrite',
      'PaymentsWrite',
      'MessagesBroadcastClass',
    ] as const) {
      expect(scopeLabel('Own', 'Parent', permission)).toBe('Tříd svých dětí');
    }
  });
});

describe('scopesFor', () => {
  it("offers the scopes of the role's kind", () => {
    const info: PermissionInfo = {
      permission: 'GradesRead',
      agenda: 'Známky',
      label: 'Vidět známky',
      staffScopes: ['None', 'ClassTeacher', 'MyClasses', 'School'],
      parentScopes: ['None', 'Own'],
      studentScopes: ['None'],
    };

    expect(scopesFor(info, 'Staff')).toHaveLength(4);
    expect(scopesFor(info, 'Parent')).toEqual(['None', 'Own']);
    expect(scopesFor(info, 'Student')).toEqual(['None']);
  });
});
