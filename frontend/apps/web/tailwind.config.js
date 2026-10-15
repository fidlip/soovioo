const { join } = require('node:path');

/** @type {import('tailwindcss').Config} */
module.exports = {
  // Absolute (via __dirname) rather than cwd-relative: this file's location
  // relative to the Angular/Nx build's working directory isn't guaranteed.
  content: [
    join(__dirname, 'src/**/*.{html,ts}'),
    join(__dirname, '../../libs/shared/src/**/*.{html,ts}'),
  ],
  theme: {
    extend: {},
  },
  plugins: [require('@openng/optimus-ui-tailwindcss')],
  corePlugins: { preflight: false },
};
