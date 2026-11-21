import { parseEmail, parsePhone } from './contact-format';

describe('parseEmail', () => {
  it.each([
    ['user@example.com', 'user@example.com'],
    ['  USER@EXAMPLE.COM  ', 'USER@example.com'],
    ['test@sub.example.com', 'test@sub.example.com'],
    ['user+tag@example.com', 'user+tag@example.com'],
    ['user.name@example.com', 'user.name@example.com'],
    ['user_name@example.com', 'user_name@example.com'],
    ['u@e.cz', 'u@e.cz'],
    ['jan@škola-žižkov.cz', 'jan@škola-žižkov.cz'],
  ])('accepts %s as %s', (input, expected) => {
    expect(parseEmail(input)).toEqual({ valid: true, value: expected });
  });

  it.each([
    'userexample.com',
    'user@@example.com',
    'a@b@example.com',
    'user @example.com',
    'user@example',
    'user@example.c',
    'user@example.123',
    '.user@example.com',
    'user.@example.com',
    'user..name@example.com',
    'user@.example.com',
    'user@example..com',
    'user@example.com.',
    'user@-example.com',
    'user,x@example.com',
    '@example.com',
  ])('refuses %s', (input) => {
    expect(parseEmail(input)).toEqual({ valid: false, value: null });
  });

  it.each([null, undefined, '', '   '])('takes %p as none', (input) => {
    expect(parseEmail(input)).toEqual({ valid: true, value: null });
  });
});

describe('parsePhone', () => {
  it.each([
    ['777123456', '+420 777 123 456'],
    ['777 123 456', '+420 777 123 456'],
    ['+420777123456', '+420 777 123 456'],
    ['+420 777 123 456', '+420 777 123 456'],
    ['00420 777 123 456', '+420 777 123 456'],
    ['420777123456', '+420 777 123 456'],
    ['777-123-456', '+420 777 123 456'],
    ['  234 567 890 ', '+420 234 567 890'],
    ['+421 905 123 456', '+421 905 123 456'],
    ['+421905-123/456', '+421905 123 456'],
    ['0049 30 1234567', '+49 30 1234567'],
    ['+44(20)7946 0958', '+44 20 7946 0958'],
    ['+ 421 905 123 456', '+421 905 123 456'],
  ])('accepts %s as %s', (input, expected) => {
    expect(parsePhone(input)).toEqual({ valid: true, value: expected });
  });

  it.each([
    'abc',
    '777 123 45a',
    '12345678',
    '1234567890',
    '077712345',
    '177123456',
    '+42077712345',
    '+4207771234567',
    '+420177123456',
    '+0123456789',
    '+1234567',
    '+1234567890123456',
    '777*123*456',
    '777+123456',
    '42017712345 6',
    '0042',
  ])('refuses %s', (input) => {
    expect(parsePhone(input)).toEqual({ valid: false, value: null });
  });

  it.each([null, undefined, '', '   '])('takes %p as none', (input) => {
    expect(parsePhone(input)).toEqual({ valid: true, value: null });
  });
});
