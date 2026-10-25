import nx from '@nx/eslint-plugin';
import baseConfig from '../../eslint.config.mjs';

export default [
  ...nx.configs['flat/angular'],
  ...nx.configs['flat/angular-template'],
  ...baseConfig,
  {
    files: ['**/*.ts'],
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'app',
          style: 'kebab-case',
        },
      ],
      // Dates, times and amounts go through the named formats in
      // @soovioo/shared (date-format.ts), not hand-picked patterns/locales.
      'no-restricted-syntax': [
        'error',
        {
          selector:
            'CallExpression[callee.property.name=/^toLocale(Date|Time)?String$/]',
          message:
            'Use formatDate()/formatAmount() from @soovioo/shared instead.',
        },
        {
          selector: "ImportSpecifier[imported.name='DatePipe']",
          message: 'Use the dateFormat pipe from @soovioo/shared instead.',
        },
      ],
    },
  },
  {
    files: ['**/*.html'],
    rules: {
      // A <label> wrapping an optimus-ui form component is associated with
      // its inner <input>, which the rule can't see on its own.
      '@angular-eslint/template/label-has-associated-control': [
        'error',
        {
          controlComponents: ['p-checkbox', 'p-radiobutton', 'p-toggleswitch'],
        },
      ],
      'no-restricted-syntax': [
        'error',
        {
          selector: "BindingPipe[name='date']",
          message: "Use `| dateFormat: '…'` from @soovioo/shared instead.",
        },
      ],
    },
  },
];
