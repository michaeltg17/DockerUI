import { http } from '@/lib/api-client';

import type { App } from '../types';

export const getApps = () => http.get<App[]>('/apps');
