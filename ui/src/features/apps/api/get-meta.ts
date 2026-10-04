import { http } from '@/lib/api-client';

export type Meta = {
  name: string;
};

export const getMeta = () => http.get<Meta>('/meta');
