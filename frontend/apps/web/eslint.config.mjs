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
    },
  },
  {
    files: ['**/*.html'],
    rules: {
      // A <label> wrapping an optimus-ui form component is associated with
      // its inner <input>, which the rule can't see on its own.
      '@angular-eslint/template/label-has-associated-control': [
        'error',
        { controlComponents: ['p-checkbox', 'p-radiobutton', 'p-toggleswitch'] },
      ],
    },
  },
];
