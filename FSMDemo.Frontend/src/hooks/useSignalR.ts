import { useEffect, useRef } from 'react';
import * as signalR from '@microsoft/signalr';
import { useEngineStore } from '../stores/engineStore';

const MIN_ACTIVE_NODE_MS = 600;

export function useSignalR() {
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    let clearActiveTimer: number | null = null;

    const clearActiveTimerIfNeeded = () => {
      if (clearActiveTimer !== null) {
        window.clearTimeout(clearActiveTimer);
        clearActiveTimer = null;
      }
    };

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('http://localhost:5079/hubs/execution')
      .withAutomaticReconnect()
      .build();

    connection.on('StateChanged', (data: { state: string; currentNode?: string }) => {
      const store = useEngineStore.getState();
      const previousState = store.execution.state;
      useEngineStore.getState().setExecution({
        state: data.state,
        currentNodeName: data.currentNode,
      });
      if (data.state === 'Finished') {
        store.appendExecutionLog({ kind: 'state', message: 'Finished' });
      } else if (data.state === 'Paused') {
        store.appendExecutionLog({ kind: 'state', message: `Paused at ${data.currentNode || 'current node'}` });
      } else if (data.state === 'Idle' && previousState !== 'Idle' && previousState !== 'Finished') {
        store.appendExecutionLog({ kind: 'state', message: 'Stopped' });
      }
      if (data.state === 'Idle') {
        clearActiveTimerIfNeeded();
        store.setActiveNode(null);
        store.setActiveTransition(null);
      }
    });

    connection.on('NodeActivated', (nodeName: string) => {
      clearActiveTimerIfNeeded();
      const store = useEngineStore.getState();
      const previousActiveNode = store.activeNodeName;
      store.setActiveNode(nodeName);
      store.setActiveTransition(
        previousActiveNode && previousActiveNode !== nodeName
          ? { fromNodeName: previousActiveNode, toNodeName: nodeName }
          : null
      );
      store.appendExecutionLog({ kind: 'enter', message: `Enter ${nodeName}`, nodeName });
    });

    connection.on('NodeDeactivated', (nodeName: string) => {
      useEngineStore.getState().appendExecutionLog({ kind: 'exit', message: `Exit ${nodeName}`, nodeName });
      clearActiveTimerIfNeeded();
      clearActiveTimer = window.setTimeout(() => {
        const store = useEngineStore.getState();
        if (store.activeNodeName === nodeName) {
          store.setActiveNode(null);
          store.setActiveTransition(null);
        }
        clearActiveTimer = null;
      }, MIN_ACTIVE_NODE_MS);
    });

    connection.on('EngineImported', () => {
      useEngineStore.getState().clearExecutionLog();
      import('../api/client').then(({ api }) => {
        Promise.all([api.getNodes(), api.getConnections(), api.getStatus(), api.getModuleInstances()]).then(
          ([nodes, connections, status, modules]) => {
            useEngineStore.getState().setNodes(nodes);
            useEngineStore.getState().setConnections(connections);
            useEngineStore.getState().setStatus(status);
            useEngineStore.getState().setModuleInstances(modules);
            api.getModuleDefinitions().then((defs) => {
              useEngineStore.getState().setModuleDefs(defs);
            }).catch(() => {});
          }
        );
      });
    });

    const subscribeToExecution = async () => {
      try {
        await connection.invoke('SubscribeToExecution');
      } catch (error) {
        console.error('Failed to subscribe to execution updates', error);
      }
    };

    connection.onreconnected(() => {
      void subscribeToExecution();
    });

    connection.start()
      .then(subscribeToExecution)
      .catch((error) => {
        console.error('Failed to connect to execution hub', error);
      });

    connectionRef.current = connection;

    return () => {
      clearActiveTimerIfNeeded();
      connection.stop();
    };
  }, []);

  return connectionRef;
}
