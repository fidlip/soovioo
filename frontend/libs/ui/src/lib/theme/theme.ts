import { definePreset } from '@openng/optimus-ui-themes';
import Aura from '@openng/optimus-ui-themes/aura';

/** Aura with the primary palette swapped from its default emerald to
 * indigo — matches the "Soovioo" (owl/night) brand instead of a generic
 * component-library green. */
export const sooviooTheme = definePreset(Aura, {
  semantic: {
    primary: {
      50: '{indigo.50}',
      100: '{indigo.100}',
      200: '{indigo.200}',
      300: '{indigo.300}',
      400: '{indigo.400}',
      500: '{indigo.500}',
      600: '{indigo.600}',
      700: '{indigo.700}',
      800: '{indigo.800}',
      900: '{indigo.900}',
      950: '{indigo.950}',
    },
    // Aura's light primary is the 500 shade, and indigo-500 on white is
    // 4.46:1 — just under WCAG AA's 4.5:1 for normal text (primary
    // buttons, active tabs, links). One shade darker passes comfortably.
    colorScheme: {
      light: {
        primary: {
          color: '{primary.600}',
          hoverColor: '{primary.700}',
          activeColor: '{primary.800}',
        },
      },
    },
  },
  // Same story for the coloured message/toast text: Aura's 600 shades on
  // their pale 50 backgrounds land at 2.9–4.4:1 (yellow worst); 700 passes.
  components: {
    message: {
      colorScheme: {
        light: {
          info: { color: '{blue.700}', simple: { color: '{blue.700}' } },
          success: { color: '{green.700}', simple: { color: '{green.700}' } },
          warn: { color: '{yellow.700}', simple: { color: '{yellow.700}' } },
          error: { color: '{red.700}', simple: { color: '{red.700}' } },
        },
      },
    },
    toast: {
      colorScheme: {
        light: {
          info: { color: '{blue.700}' },
          success: { color: '{green.700}' },
          warn: { color: '{yellow.700}' },
          error: { color: '{red.700}' },
        },
      },
    },
  },
});
