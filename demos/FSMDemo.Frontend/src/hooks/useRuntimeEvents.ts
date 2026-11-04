import { useEffect } from 'react';
import { runtime } from '../runtime/index';
import { useEngineStore } from '../stores/engineStore';

/** The UI observes domain events, not SignalR connections or .NET callbacks. */
export function useRuntimeEvents() {
  useEffect(() => runtime.subscribe((event) => {
    useEngineStore.getState().handleRuntimeEvent(event);
  }), []);
}
