import { createTheme, type MantineColorsTuple } from '@mantine/core';

// One warm amber accent (a lit porch light), used sparingly for primary actions and unread items.
const amber: MantineColorsTuple = [
  '#fff8eb', '#fdecc8', '#fbd98c', '#f8c357', '#f5b031',
  '#f2a31a', '#e8930c', '#c27508', '#9b5d0b', '#7e4c0e',
];

// Warm grays and off-whites for everything else.
const gray: MantineColorsTuple = [
  '#faf8f5', '#f2efea', '#e6e1da', '#d4cdc4', '#b5ada3',
  '#978e84', '#7a7268', '#5f5850', '#46403a', '#2e2a26',
];

const dark: MantineColorsTuple = [
  '#ddd8d1', '#b9b2a9', '#948c82', '#6f685f', '#4c463f',
  '#3b3631', '#2e2a26', '#25221f', '#1c1a17', '#141210',
];

export const theme = createTheme({
  primaryColor: 'amber',
  primaryShade: { light: 5, dark: 5 },
  autoContrast: true,
  luminanceThreshold: 0.45,
  colors: { amber, gray, dark },
  fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
  headings: { fontWeight: '650' },
  defaultRadius: 'md',
  black: '#24211e',
  components: {
    Button: { defaultProps: { size: 'md' } },
    TextInput: { defaultProps: { size: 'md' } },
    Textarea: { defaultProps: { size: 'md', autosize: true, minRows: 2 } },
    Select: { defaultProps: { size: 'md' } },
    Card: { defaultProps: { withBorder: true, radius: 'md', padding: 'md' } },
    Badge: {
      defaultProps: { variant: 'light', radius: 'sm' },
      styles: { root: { textTransform: 'none', flexShrink: 0, overflow: 'visible' }, label: { overflow: 'visible' } },
    },
  },
});
