import { useMemo } from 'react';
import { Navigate, RouterProvider, createBrowserRouter } from 'react-router';

import { paths } from '@/config/paths';
import { AppsPage } from '@/features/apps';

export const AppRouter = () => {
  const router = useMemo(
    () =>
      createBrowserRouter([
        {
          path: paths.home.path,
          element: <AppsPage />,
        },
        {
          path: '*',
          element: <Navigate to={paths.home.getHref()} replace />,
        },
      ]),
    [],
  );

  return <RouterProvider router={router} />;
};
