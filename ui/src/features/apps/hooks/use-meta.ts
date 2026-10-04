import { useQuery } from '@tanstack/react-query';

import { getMeta } from '../api/get-meta';

export const META_QUERY_KEY = ['meta'] as const;

export const useMeta = () =>
  useQuery({
    queryKey: META_QUERY_KEY,
    queryFn: getMeta,
    staleTime: Infinity,
  });
