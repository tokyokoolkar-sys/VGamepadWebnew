export interface Pos { x: number; y: number; w?: number; h?: number; round?: boolean; hidden?: boolean; label?: string; color?: string }
export interface GamepadLayout { version: number; controls: Record<string, Pos> }
export type ThemeType = 'xbox' | 'ps' | 'nintendo';
export interface BtnCfg { label: string; color: string; w: number; h: number; round: boolean }
export interface Profile { id: string; name: string }
