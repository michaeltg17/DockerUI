export const themes = [
  'system',
  'light',
  'dark',
  'dark-blue',
  'docker',
] as const;

export type Theme = (typeof themes)[number];

const THEME_STORAGE_KEY = 'DockerUI:theme';

export const PREFERS_DARK_QUERY = '(prefers-color-scheme: dark)';

export const isTheme = (value: unknown): value is Theme =>
  typeof value === 'string' && (themes as readonly string[]).includes(value);

export const getStoredTheme = (): Theme => {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    return isTheme(stored) ? stored : 'system';
  } catch {
    return 'system';
  }
};

export const storeTheme = (theme: Theme): void => {
  try {
    localStorage.setItem(THEME_STORAGE_KEY, theme);
  } catch {
    // Storage may be unavailable; the theme still applies for this session.
  }
};

export const applyTheme = (theme: Theme): void => {
  // 'system' is a stored preference, not a DOM theme: it always resolves to a
  // concrete light or dark, matching the OS's color scheme.
  const resolved =
    theme === 'system' && window.matchMedia(PREFERS_DARK_QUERY).matches
      ? 'dark'
      : theme;
  document.documentElement.dataset.theme = resolved;
};
