import Axios, {
  type AxiosRequestConfig,
  type InternalAxiosRequestConfig,
} from 'axios';

import { useNotifications } from '@/components/ui/notifications';

// Requests flagged 'silentError' skip the global error toast; the caller handles
// the (expected) failure itself, e.g. the dashboard restart dropping the socket.
declare module 'axios' {
  export interface AxiosRequestConfig {
    silentError?: boolean;
  }
}

function requestInterceptor(config: InternalAxiosRequestConfig) {
  if (config.headers) {
    config.headers.Accept = 'application/json';
  }

  return config;
}

export const api = Axios.create({
  baseURL: '/api',
});

// A 401 ends the session: instead of an error toast, the app switches to the
// login page. The handler lives in the auth feature, which this module must
// not import (that would cycle back through the app shell), so it is
// registered as a callback.
let unauthorizedHandler: (() => void) | null = null;

export const setUnauthorizedHandler = (handler: () => void) => {
  unauthorizedHandler = handler;
};

api.interceptors.request.use(requestInterceptor);
api.interceptors.response.use(
  (response) => {
    return response.data;
  },
  (error) => {
    if (error.response?.status === 401) {
      unauthorizedHandler?.();
      return Promise.reject(error);
    }

    if (error.config?.silentError) {
      return Promise.reject(error);
    }

    const data = error.response?.data;
    const message =
      (typeof data === 'object' &&
        data !== null &&
        (data.detail || data.message)) ||
      error.message;

    useNotifications.getState().addNotification({
      type: 'error',
      title: 'Error',
      message,
    });

    return Promise.reject(error);
  },
);

export const http = {
  get: <T>(url: string, config?: AxiosRequestConfig) =>
    api.get<T, T>(url, config),
  post: <T>(url: string, body?: unknown, config?: AxiosRequestConfig) =>
    api.post<T, T>(url, body, config),
  put: <T>(url: string, body?: unknown, config?: AxiosRequestConfig) =>
    api.put<T, T>(url, body, config),
  delete: <T>(url: string, config?: AxiosRequestConfig) =>
    api.delete<T, T>(url, config),
};
