import {
  type HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';

import type { App } from '../types';

import { APPS_QUERY_KEY } from './use-apps';

const APPS_UPDATED_EVENT = 'appsUpdated';

/**
 * Subscribes to the SignalR hub and keeps the apps cache in sync
 * with whatever the Docker daemon is doing (including changes made
 * outside of this UI).
 */
export const useAppsHub = () => {
  const queryClient = useQueryClient();

  useEffect(() => {
    let connection: HubConnection | undefined;
    let isStopping = false;

    const start = async () => {
      const builder = new HubConnectionBuilder().withUrl('/api/apps/hub');

      // LogLevel.None: the client's default logger writes its connection
      // trace/debug lines straight to the browser console.
      connection = builder
        .withAutomaticReconnect()
        .configureLogging(LogLevel.None)
        .build();

      connection.on(APPS_UPDATED_EVENT, (apps: App[]) => {
        queryClient.setQueryData(APPS_QUERY_KEY, apps);
      });

      connection.onreconnected(() => {
        // The monitor only re-broadcasts on change, so fetch the
        // current state after the connection drops and recovers.
        void queryClient.invalidateQueries({ queryKey: APPS_QUERY_KEY });
      });

      try {
        await connection.start();
      } catch {
        // The API (or the Docker daemon) may be unreachable; the
        // apps query will surface the error to the user.
        if (!isStopping) {
          void queryClient.invalidateQueries({ queryKey: APPS_QUERY_KEY });
        }
      }
    };

    void start();

    return () => {
      isStopping = true;
      void connection?.stop();
    };
  }, [queryClient]);
};
