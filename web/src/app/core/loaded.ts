import { WritableSignal, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { Problem, toProblem } from './api';

export interface Loaded<T> {
  state: 'loading' | 'ready' | 'error';
  value: T | null;
  problem: Problem | null;
}

export function loaded<T>(): WritableSignal<Loaded<T>> {
  return signal<Loaded<T>>({ state: 'loading', value: null, problem: null });
}

export function load<T>(source: Observable<T>, target: WritableSignal<Loaded<T>>): void {
  target.update((current) => ({ ...current, state: 'loading', problem: null }));
  source.subscribe({
    next: (value) => target.set({ state: 'ready', value, problem: null }),
    error: (error: unknown) =>
      target.set({ state: 'error', value: null, problem: toProblem(error) }),
  });
}
