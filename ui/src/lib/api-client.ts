import Axios, { type InternalAxiosRequestConfig } from 'axios';

import { useNotifications } from '@/components/ui/notifications';
import { env } from '@/config/env';

function requestInterceptor(config: InternalAxiosRequestConfig) {
  if (config.headers) {
    config.headers.Accept = 'application/json';
  }

  return config;
}

export const api = Axios.create({
  baseURL: env.API_URL,
});

api.interceptors.request.use(requestInterceptor);
api.interceptors.response.use(
  (response) => {
    return response.data;
  },
  (error) => {
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

/**
 * The response interceptor above already unwraps `response.data` at runtime,
 * so the promise resolves to the response body (T) rather than an
 * `AxiosResponse<T>`. The `R = T` generic tells axios the resolved value is
 * the body itself — no second `.data` access (which would yield `undefined`).
 */
export const http = {
  get: <T>(url: string) => api.get<T, T>(url),
  post: <T>(url: string) => api.post<T, T>(url),
};
