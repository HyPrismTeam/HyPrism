#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::collections::{HashMap, VecDeque};
use std::sync::Arc;
use std::time::Duration;

use serde::{Deserialize, Serialize};
use serde_json::Value;
use tauri::{AppHandle, Emitter, Manager, State};
use tauri_plugin_shell::process::{CommandChild, CommandEvent};
use tauri_plugin_shell::ShellExt;
use tokio::sync::{oneshot, Mutex};

const PROTOCOL_PREFIX: &str = "@@HYPRISM_IPC@@";

#[derive(Debug, Serialize)]
struct BridgeInbound {
    r#type: String,
    channel: String,
    payload: Option<Value>,
}

#[derive(Debug, Deserialize)]
struct BridgeOutbound {
    r#type: String,
    channel: String,
    payload: Option<String>,
}

#[derive(Clone)]
struct BridgeState {
    child: Arc<Mutex<CommandChild>>,
    pending: Arc<Mutex<HashMap<String, VecDeque<oneshot::Sender<Value>>>>>,
}

impl BridgeState {
    fn new(child: CommandChild) -> Self {
        Self {
            child: Arc::new(Mutex::new(child)),
            pending: Arc::new(Mutex::new(HashMap::new())),
        }
    }

    async fn send_line(&self, line: String) -> Result<(), String> {
        let mut child = self.child.lock().await;
        child
            .write(format!("{line}\n").as_bytes())
            .map_err(|e| format!("failed writing to sidecar stdin: {e}"))
    }

    async fn enqueue_waiter(&self, reply_channel: String, tx: oneshot::Sender<Value>) {
        let mut pending = self.pending.lock().await;
        pending
            .entry(reply_channel)
            .or_default()
            .push_back(tx);
    }

    async fn resolve_waiter(&self, channel: &str, payload: Value) -> bool {
        let mut pending = self.pending.lock().await;
        if let Some(queue) = pending.get_mut(channel) {
            if let Some(tx) = queue.pop_front() {
                let _ = tx.send(payload);
                if queue.is_empty() {
                    pending.remove(channel);
                }
                return true;
            }
        }
        false
    }
}

#[tauri::command]
async fn bridge_send(
    channel: String,
    payload: Option<Value>,
    state: State<'_, BridgeState>,
) -> Result<(), String> {
    let msg = BridgeInbound {
        r#type: "send".into(),
        channel,
        payload,
    };

    let line = serde_json::to_string(&msg).map_err(|e| e.to_string())?;
    state.send_line(line).await
}

#[tauri::command]
async fn bridge_invoke(
    channel: String,
    payload: Option<Value>,
    timeout_ms: Option<u64>,
    state: State<'_, BridgeState>,
) -> Result<Value, String> {
    let reply_channel = format!("{channel}:reply");
    let (tx, rx) = oneshot::channel::<Value>();
    state.enqueue_waiter(reply_channel, tx).await;

    let msg = BridgeInbound {
        r#type: "invoke".into(),
        channel,
        payload,
    };

    let line = serde_json::to_string(&msg).map_err(|e| e.to_string())?;
    state.send_line(line).await?;

    let timeout = Duration::from_millis(timeout_ms.unwrap_or(30_000));
    match tokio::time::timeout(timeout, rx).await {
        Ok(Ok(value)) => Ok(value),
        Ok(Err(_)) => Err("bridge response channel closed".into()),
        Err(_) => Err("bridge invoke timeout".into()),
    }
}

fn parse_backend_line(line: &str) -> Option<BridgeOutbound> {
    if !line.starts_with(PROTOCOL_PREFIX) {
        return None;
    }

    let json = &line[PROTOCOL_PREFIX.len()..];
    serde_json::from_str::<BridgeOutbound>(json).ok()
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .setup(|app| {
            let sidecar = app.shell().sidecar("hyprism-bridge").map_err(|e| {
                let msg = format!("failed to resolve HyPrism bridge sidecar: {e}");
                Box::<dyn std::error::Error>::from(msg)
            })?;

            let (rx, child) = sidecar
                .args(["--runtime", "tauri"])
                .spawn()
                .map_err(|e| {
                    let msg = format!("failed to start HyPrism bridge sidecar: {e}");
                    Box::<dyn std::error::Error>::from(msg)
                })?;

            let state = BridgeState::new(child);
            app.manage(state.clone());

            let app_handle: AppHandle = app.handle().clone();
            tauri::async_runtime::spawn(async move {
                let mut rx = rx;
                while let Some(event) = rx.recv().await {
                    match event {
                        CommandEvent::Stdout(bytes) => {
                            let line = String::from_utf8_lossy(&bytes).trim().to_string();
                            if line.is_empty() {
                                continue;
                            }

                            if let Some(msg) = parse_backend_line(&line) {
                                if msg.r#type == "emit" {
                                    let payload = msg
                                        .payload
                                        .as_ref()
                                        .and_then(|raw| serde_json::from_str::<Value>(raw).ok())
                                        .unwrap_or(Value::Null);

                                    let resolved = state.resolve_waiter(&msg.channel, payload.clone()).await;

                                    let envelope = serde_json::json!({
                                        "channel": msg.channel,
                                        "payload": payload,
                                        "resolvedPendingInvoke": resolved
                                    });

                                    let _ = app_handle.emit(
                                        "hyprism:bridge:event",
                                        envelope.clone(),
                                    );

                                    if let Some(window) = app_handle.get_webview_window("main") {
                                        if let Ok(envelope_json) = serde_json::to_string(&envelope) {
                                            let script = format!(
                                                "window.dispatchEvent(new CustomEvent('hyprism:bridge:event', {{ detail: {envelope_json} }}));"
                                            );
                                            let _ = window.eval(&script);
                                        }
                                    }
                                }
                            }
                        }
                        CommandEvent::Stderr(bytes) => {
                            eprintln!("hyprism-bridge stderr: {}", String::from_utf8_lossy(&bytes));
                        }
                        CommandEvent::Terminated(payload) => {
                            eprintln!("hyprism-bridge terminated: {:?}", payload);
                            break;
                        }
                        _ => {}
                    }
                }
            });

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![bridge_send, bridge_invoke])
        .run(tauri::generate_context!())
        .expect("error while running HyPrism Tauri wrapper");
}
