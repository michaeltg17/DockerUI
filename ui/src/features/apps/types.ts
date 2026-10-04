export type AppState = 'running' | 'stopped';

export interface AppService {
  name: string;
  containerId: string;
  image: string | null;
  isRunning: boolean;
}

export interface App {
  name: string;
  icon: string | null;
  state: AppState;
  url: string | null;
  services: AppService[];
  isShortcut: boolean;
}

export interface Shortcut {
  name: string;
  icon: string | null;
  url: string;
}
