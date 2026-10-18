import * as React from 'react';

import {
  applyTheme,
  getStoredTheme,
  storeTheme,
  type Theme,
} from '@/lib/theme';

export const useTheme = () => {
  const [theme, setTheme] = React.useState<Theme>(getStoredTheme);

  React.useEffect(() => {
    applyTheme(theme);
    storeTheme(theme);
  }, [theme]);

  return { theme, setTheme };
};
