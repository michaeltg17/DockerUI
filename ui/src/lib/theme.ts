export const themes = ['light', 'dark', 'dark-blue', 'docker'] as const;

export type Theme = (typeof themes)[number];

const THEME_STORAGE_KEY = 'DockerUI:theme';

/** Values stored before the theme keys were renamed, mapped to their new keys. */
const legacyThemes: Record<string, Theme> = {
  docker: 'dark-blue',
  'docker-v2': 'docker',
};

export const isTheme = (value: unknown): value is Theme =>
  typeof value === 'string' && (themes as readonly string[]).includes(value);

export const getStoredTheme = (): Theme => {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    return isTheme(stored) ? stored : (legacyThemes[stored ?? ''] ?? 'light');
  } catch {
    return 'light';
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
  document.documentElement.dataset.theme = theme;
};
