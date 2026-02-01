declare global {
    interface External {
        sendMessage: (message: string) => void;
        receiveMessage: (callback: (message: any) => void) => void;
    }
    interface Window {
        _photinoListenerAdded?: boolean;
    }
}

interface RpcMessage {
    Id: string;
    Method: string;
    Args: any[];
}

interface RpcResponse {
    Id: string;
    Result: any;
    Error: string | null;
}

interface EventMessage {
    type: 'event' | 'progress';
    eventName: string;
    data: any;
}

const pendingCalls = new Map<string, { resolve: (value: any) => void; reject: (reason: any) => void }>();
let callId = 0;

// #region Listener Setup
if (!window._photinoListenerAdded) {
    window._photinoListenerAdded = true;

    const setupListener = () => {
        if (!window.external || typeof window.external.receiveMessage !== 'function') {
            console.warn('Photino external provider not found');
            return false;
        }

        window.external.receiveMessage((rawMessage: any) => {
            try {
                const message = typeof rawMessage === 'string' ? JSON.parse(rawMessage) : rawMessage;

                // Handle events
                if (message.type === 'event' || message.type === 'progress') {
                    const eventMsg = message as EventMessage;
                    const eventName = eventMsg.eventName || 'progress-update';
                    const eventData = eventMsg.data || message;
                    window.dispatchEvent(new CustomEvent(eventName, { detail: eventData }));
                    return;
                }

                // Handle RPC responses
                const response = message as RpcResponse;
                if (response.Id && pendingCalls.has(response.Id)) {
                    const { resolve, reject } = pendingCalls.get(response.Id)!;
                    pendingCalls.delete(response.Id);

                    if (response.Error) {
                        reject(new Error(response.Error));
                    } else {
                        resolve(response.Result);
                    }
                }
            } catch (error) {
                console.error('Failed to process message from backend:', error);
            }
        });
        return true;
    };

    if (!setupListener()) {
        let attempts = 0;
        const interval = setInterval(() => {
            if (setupListener() || attempts++ > 20) clearInterval(interval);
        }, 100);
    }
}
// #endregion

/**
 * Invokes a method on the C# backend via Photino bridge
 * @param method - The name of the backend method to call
 * @param args - Arguments to pass to the method
 * @returns Promise that resolves with the result or rejects with an error
 */
export function callBackend<T = any>(method: string, ...args: any[]): Promise<T> {
    return new Promise((resolve, reject) => {
        const id = `call_${++callId}`;
        pendingCalls.set(id, { resolve, reject });

        const payload: RpcMessage = {
            Id: id,
            Method: method,
            Args: args
        };

        if (window.external?.sendMessage) {
            window.external.sendMessage(JSON.stringify(payload));
        } else {
            // Dev mode / Fallback
            console.warn(`[Mock] Backend call: ${method}`, args);
            reject(new Error("Native bridge not available"));
        }
    });
}

// #region Event System
const eventListeners = new Map<string, Array<{ callback: (...args: any[]) => void; maxCallbacks: number; callCount: number }>>();

/**
 * Subscribes to an event from the backend
 * @param eventName - Name of the event to listen for
 * @param callback - Function to call when event is received
 * @returns Unsubscribe function
 */
export function EventsOn(eventName: string, callback: (...args: any[]) => void): () => void {
    if (!eventListeners.has(eventName)) {
        eventListeners.set(eventName, []);
        
        const handler = (e: Event) => {
            const customEvent = e as CustomEvent;
            const listeners = eventListeners.get(eventName);
            if (!listeners) return;

            for (let i = listeners.length - 1; i >= 0; i--) {
                const listener = listeners[i];
                
                if (Array.isArray(customEvent.detail)) {
                    listener.callback(...customEvent.detail);
                } else {
                    listener.callback(customEvent.detail);
                }
                
                listener.callCount++;

                if (listener.maxCallbacks !== -1 && listener.callCount >= listener.maxCallbacks) {
                    listeners.splice(i, 1);
                }
            }
        };
        window.addEventListener(eventName, handler);
    }
    
    const listenerObj = { callback, maxCallbacks: -1, callCount: 0 };
    const listeners = eventListeners.get(eventName)!;
    listeners.push(listenerObj);

    return () => {
        const index = listeners.indexOf(listenerObj);
        if (index !== -1) {
            listeners.splice(index, 1);
        }
    };
}

/**
 * Subscribes to an event once
 * @param eventName - Name of the event
 * @param callback - Function to call
 * @returns Unsubscribe function
 */
export function EventsOnce(eventName: string, callback: (...args: any[]) => void): () => void {
     if (!eventListeners.has(eventName)) {
        eventListeners.set(eventName, []);
        const handler = (e: Event) => {
            const customEvent = e as CustomEvent;
            const listeners = eventListeners.get(eventName);
            if (!listeners) return;
            for (let i = listeners.length - 1; i >= 0; i--) {
                const listener = listeners[i];
                if (Array.isArray(customEvent.detail)) {
                    listener.callback(...customEvent.detail);
                } else {
                    listener.callback(customEvent.detail);
                }
                listener.callCount++;
                if (listener.maxCallbacks !== -1 && listener.callCount >= listener.maxCallbacks) {
                    listeners.splice(i, 1);
                }
            }
        };
        window.addEventListener(eventName, handler);
    }
    
    const listenerObj = { callback, maxCallbacks: 1, callCount: 0 };
    const listeners = eventListeners.get(eventName)!;
    listeners.push(listenerObj);

    return () => {
        const index = listeners.indexOf(listenerObj);
        if (index !== -1) {
            listeners.splice(index, 1);
        }
    };
}

/**
 * Unsubscribes all listeners for an event (Internal use mostly)
 * @param eventName - Name of the event
 */
export function EventsOff(eventName: string) {
    eventListeners.delete(eventName);
}

/**
 * Emits an event manually (e.g. for testing)
 * @param eventName - Name of the event
 * @param args - Data to pass
 */
export function EventsEmit(eventName: string, ...args: any[]) {
    window.dispatchEvent(new CustomEvent(eventName, { detail: args }));
}
// #endregion

// #region Window Controls
export const WindowMinimize = () => callBackend('WindowMinimize');
export const WindowMaximize = () => callBackend('WindowMaximize');
export const WindowToggleMaximize = () => callBackend('WindowMaximize');
export const WindowUnmaximize = () => callBackend('WindowMaximize');
export const WindowRestore = () => callBackend('WindowRestore');
export const WindowClose = () => callBackend('WindowClose');
export const WindowHide = () => callBackend('WindowHide');
export const Quit = () => callBackend('WindowClose');

/**
 * Opens a URL in the default system browser
 * @param url - URL to open
 */
export const BrowserOpenURL = (url: string) => callBackend('BrowserOpenURL', url);

/**
 * Copies text to system clipboard
 * @param text - Text to copy
 */
export const ClipboardSetText = (text: string) => navigator.clipboard.writeText(text);

/**
 * Gets text from system clipboard
 * @returns Promise with text content
 */
export const ClipboardGetText = () => navigator.clipboard.readText();
// #endregion

// #region Logging
export const LogInfo = (m: string) => console.info(m);
export const LogError = (m: string) => console.error(m);
export const LogWarning = (m: string) => console.warn(m);
export const LogDebug = (m: string) => console.debug(m);
// #endregion
