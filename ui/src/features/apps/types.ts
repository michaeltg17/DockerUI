export type AppState = 'running' | 'stopped';

export type AppSource = 'docker' | 'shortcut' | 'lan';

export interface AppService {
  name: string;
  containerId: string;
  image: string | null;
  isRunning: boolean;
}

export interface App {
  name: string;
  /**
   * The name shown on the card: for docker apps the per-app override, for
   * shortcuts the enriched page title (LAN) or the shortcut name, else `name`.
   */
  displayName: string;
  icon: string | null;
  state: AppState;
  url: string | null;
  services: AppService[];
  source: AppSource;
  /**
   * The hue of the initials icon, persisted for shortcuts so renaming never
   * changes the color. Null for docker apps (derived from the name) and for
   * shortcuts that predate the field.
   */
  color: number | null;
}

export interface Shortcut {
  name: string;
  icon: string | null;
  url: string;
  color: number | null;
}
