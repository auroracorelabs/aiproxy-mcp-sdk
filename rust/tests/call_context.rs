//! ABI v1.1: the generated `mcp_plugin_call_tool_ctx` / `mcp_plugin_list_tools_ctx` exports and
//! the `requiresUserContext` descriptor flag, exercised through the same FFI the host dlopens.

use aiproxy_mcp_sdk::{
    export_mcp_plugin, CallContext, CallToolResult, McpPlugin, PluginDescriptor, PluginResult,
};
use serde_json::Value;
use std::collections::HashMap;
use std::ffi::{CStr, CString};

struct ContextPlugin;

impl McpPlugin for ContextPlugin {
    fn descriptor(&self) -> PluginDescriptor {
        PluginDescriptor {
            server_name: "ctx-fixture".into(),
            server_version: "0.0.0".into(),
            title: None,
            description: None,
            icons: None,
            instructions: String::new(),
            config_parameters: vec![],
            connection: None,
            tools: vec![],
        }
    }

    fn configure(&self, _config: HashMap<String, Option<String>>) -> PluginResult<()> {
        Ok(())
    }

    fn call_tool(&self, _name: &str, _args: Value) -> PluginResult<CallToolResult> {
        Err("legacy path must not be used".to_string())
    }

    fn call_tool_with_context(
        &self,
        name: &str,
        _args: Value,
        context: &CallContext,
    ) -> PluginResult<CallToolResult> {
        match (name, context.user_id.as_deref()) {
            ("panics", _) => panic!("boom"),
            (_, Some(user)) => Ok(CallToolResult::text(format!("{name} by {user}"))),
            (_, None) => Err("no user".to_string()),
        }
    }

    fn allowed_tools(&self, context: &CallContext) -> Option<Vec<String>> {
        if context.in_group("admins") {
            return None;
        }
        if context.user_id.as_deref() == Some("explode") {
            panic!("census failure");
        }
        Some(if context.has_user() { vec!["read".into()] } else { vec![] })
    }

    fn requires_user_context(&self) -> bool {
        true
    }
}

export_mcp_plugin!(ContextPlugin, ContextPlugin);

fn take(ptr: *mut std::os::raw::c_char) -> String {
    let s = unsafe { CStr::from_ptr(ptr) }.to_string_lossy().into_owned();
    mcp_free_string(ptr);
    s
}

fn call_ctx(tool: &str, context: &str) -> Value {
    let name = CString::new(tool).unwrap();
    let args = CString::new("{}").unwrap();
    let ctx = CString::new(context).unwrap();
    serde_json::from_str(&take(mcp_plugin_call_tool_ctx(name.as_ptr(), args.as_ptr(), ctx.as_ptr()))).unwrap()
}

fn list_ctx(context: &str) -> String {
    let ctx = CString::new(context).unwrap();
    take(mcp_plugin_list_tools_ctx(ctx.as_ptr()))
}

#[test]
fn call_tool_ctx_receives_the_user() {
    let result = call_ctx("read", r#"{"userId":"u-1","groups":["g"],"roles":["user"],"requestId":"r"}"#);
    assert_eq!(result["content"][0]["text"], "read by u-1");
    assert!(result.get("isError").is_none());
}

#[test]
fn legacy_export_routes_through_context_method_with_anonymous_context() {
    let name = CString::new("read").unwrap();
    let args = CString::new("{}").unwrap();
    let result: Value =
        serde_json::from_str(&take(mcp_plugin_call_tool(name.as_ptr(), args.as_ptr()))).unwrap();
    assert_eq!(result["isError"], true);
    assert_eq!(result["content"][0]["text"], "no user");
}

#[test]
fn invalid_context_json_is_anonymous() {
    let result = call_ctx("read", "not json");
    assert_eq!(result["isError"], true);
}

#[test]
fn panics_in_call_tool_ctx_are_contained() {
    let result = call_ctx("panics", r#"{"userId":"u-1"}"#);
    assert_eq!(result["isError"], true);
}

#[test]
fn list_tools_ctx_returns_census() {
    assert_eq!(list_ctx(r#"{"userId":"u-1"}"#), r#"["read"]"#);
    assert_eq!(list_ctx("{}"), "[]");
    assert_eq!(list_ctx(r#"{"userId":"u-2","groups":["admins"]}"#), "null");
}

#[test]
fn list_tools_ctx_panic_fails_closed() {
    assert_eq!(list_ctx(r#"{"userId":"explode"}"#), "[]");
}

#[test]
fn descriptor_publishes_requires_user_context() {
    let descriptor: Value = serde_json::from_str(&take(mcp_plugin_descriptor())).unwrap();
    assert_eq!(descriptor["requiresUserContext"], true);
    assert_eq!(descriptor["serverName"], "ctx-fixture");
}
