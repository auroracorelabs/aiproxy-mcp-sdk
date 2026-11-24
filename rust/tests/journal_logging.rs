//! Verifies the actual contract this whole feature exists for: a plugin built with
//! `export_mcp_plugin!` writes journal-shaped lines to stderr — with no logging code of its own —
//! whenever a tool call fails, panics, or `configure` fails. This is the same generated FFI code
//! path McpServerHost calls via dlopen in production, exercised
//! here directly and with the real stderr fd captured (redirected via `dup2`, since systemd's
//! journal bridge is what actually reads that fd in production — not something a unit test can
//! stand up, so this is the closest faithful proxy).

use aiproxy_mcp_sdk::{
    export_mcp_plugin, CallToolResult, McpPlugin, PluginDescriptor, PluginResult,
};
use serde_json::Value;
use std::collections::HashMap;
use std::ffi::{CStr, CString};
use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use std::os::unix::io::AsRawFd;
use std::sync::Mutex;

struct TestPlugin;

impl TestPlugin {
    fn new() -> Self {
        Self
    }
}

impl McpPlugin for TestPlugin {
    fn descriptor(&self) -> PluginDescriptor {
        PluginDescriptor {
            server_name: "journal-logging-fixture".into(),
            server_version: "0.0.0".into(),
            instructions: String::new(),
            config_parameters: vec![],
            connection: None,
            tools: vec![],
        }
    }

    fn configure(&self, config: HashMap<String, Option<String>>) -> PluginResult<()> {
        if config.get("fail").and_then(|v| v.as_deref()) == Some("yes") {
            return Err("bad config".to_string());
        }
        Ok(())
    }

    fn call_tool(&self, name: &str, _args: Value) -> PluginResult<CallToolResult> {
        match name {
            "boom" => Err("could not reach upstream".to_string()),
            "panics" => panic!("unexpected failure inside tool"),
            _ => Ok(CallToolResult::text("ok")),
        }
    }
}

export_mcp_plugin!(TestPlugin, TestPlugin::new());

/// Serializes access to the process-wide stderr fd across tests in this file (they run on
/// separate threads by default under `cargo test`).
static STDERR_REDIRECT_LOCK: Mutex<()> = Mutex::new(());

/// Redirects fd 2 to a fresh temp file for the duration of `body`, then restores it and returns
/// everything written while redirected.
fn capture_stderr(body: impl FnOnce()) -> String {
    // unwrap_or_else, not unwrap: an assertion failure inside `body` (which runs below while this
    // lock is held) must not poison every other test in this file — each test's own assertions
    // already report failures on their own.
    let _guard = STDERR_REDIRECT_LOCK.lock().unwrap_or_else(|e| e.into_inner());

    let mut tmp = tempfile();
    let original_fd = unsafe { libc::dup(2) };
    assert!(original_fd >= 0, "failed to dup(2)");

    let rc = unsafe { libc::dup2(tmp.as_raw_fd(), 2) };
    assert!(rc >= 0, "failed to dup2 onto stderr"); // dup2 returns the target fd (2) on success, not 0

    body();

    unsafe {
        libc::dup2(original_fd, 2);
        libc::close(original_fd);
    }

    tmp.seek(SeekFrom::Start(0)).unwrap();
    let mut captured = String::new();
    tmp.read_to_string(&mut captured).unwrap();
    captured
}

fn tempfile() -> File {
    let path = std::env::temp_dir().join(format!(
        "aiproxy-mcp-sdk-journal-logging-test-{}-{}",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    ));
    File::options().create(true).read(true).write(true).open(path).unwrap()
}

fn call_tool_and_free(name: &str) -> String {
    let name_c = CString::new(name).unwrap();
    let args_c = CString::new("{}").unwrap();
    unsafe {
        let result_ptr = mcp_plugin_call_tool(name_c.as_ptr(), args_c.as_ptr());
        let result = CStr::from_ptr(result_ptr).to_string_lossy().into_owned();
        mcp_free_string(result_ptr);
        result
    }
}

fn configure_with(fail: bool) -> i32 {
    let json = if fail { r#"{"fail":"yes"}"# } else { "{}" };
    let json_c = CString::new(json).unwrap();
    mcp_plugin_configure(json_c.as_ptr())
}

#[test]
fn tool_logical_error_is_logged_at_warn_with_journal_priority_prefix() {
    let stderr = capture_stderr(|| {
        let result = call_tool_and_free("boom");
        assert!(result.contains("\"isError\":true"));
    });

    // <4> is the sd-daemon "warning" syslog priority — journald
    // strips it and sets PRIORITY=4 on the entry; asserting on it here is what proves a plugin
    // author gets accurate level-based filtering/coloring for free, not just any stderr output.
    assert!(
        stderr.contains("<4>[WARN]") || stderr.contains("[WARN]"),
        "expected a WARN-level line, got: {stderr:?}"
    );
    assert!(stderr.contains("tool 'boom' failed"), "expected tool name in log line, got: {stderr:?}");
    assert!(stderr.contains("could not reach upstream"), "expected the real error text, got: {stderr:?}");
}

#[test]
fn tool_panic_is_logged_at_error_with_the_panic_message() {
    let stderr = capture_stderr(|| {
        let result = call_tool_and_free("panics");
        assert!(result.contains("plugin panicked while handling tool call"));
    });

    assert!(
        stderr.contains("<3>[ERROR]") || stderr.contains("[ERROR]"),
        "expected an ERROR-level line, got: {stderr:?}"
    );
    assert!(stderr.contains("tool 'panics' panicked"), "expected tool name in log line, got: {stderr:?}");
    assert!(
        stderr.contains("unexpected failure inside tool"),
        "expected the panic message, got: {stderr:?}"
    );
}

#[test]
fn successful_tool_call_logs_nothing() {
    let stderr = capture_stderr(|| {
        let result = call_tool_and_free("fine");
        assert!(!result.contains("\"isError\":true"));
    });

    assert!(stderr.is_empty(), "expected no log output for a successful call, got: {stderr:?}");
}

#[test]
fn configure_failure_is_logged_at_error() {
    let stderr = capture_stderr(|| {
        let rc = configure_with(true);
        assert_eq!(rc, 1);
    });

    assert!(
        stderr.contains("<3>[ERROR]") || stderr.contains("[ERROR]"),
        "expected an ERROR-level line, got: {stderr:?}"
    );
    assert!(stderr.contains("configure failed"), "expected 'configure failed' in log line, got: {stderr:?}");
    assert!(stderr.contains("bad config"), "expected the real error text, got: {stderr:?}");
}
