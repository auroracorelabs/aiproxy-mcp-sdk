//! Native SDK for authoring `mcpserverhost` MCP plugins in Rust (or any language capable of the
//! same C ABI — this crate is the Rust reference implementation of that contract).
//!
//! A plugin implements [`McpPlugin`] and calls [`export_mcp_plugin!`] once at crate root to
//! generate the `extern "C"` exports (`mcp_plugin_descriptor`, `mcp_plugin_configure`,
//! `mcp_plugin_call_tool`, `mcp_free_string`, `mcp_plugin_last_error`, plus the ABI v1.1
//! `mcp_plugin_call_tool_ctx` / `mcp_plugin_list_tools_ctx` carrying the verified caller) the host
//! (`McpServerHost`, .NET, `NativeLibrary.Load`s this crate's compiled `cdylib`) calls via
//! `dlopen`.
//!
//! See the [`log`] module for the standard logging surface (`log_error!`/`log_warn!`/etc.);
//! `export_mcp_plugin!` already logs `call_tool` errors/panics and `configure` failures
//! automatically, with no plugin-author effort required.

use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::HashMap;

pub mod log;

#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct ConfigParameter {
    pub name: String,
    pub required: bool,
    #[serde(rename = "type")]
    pub value_type: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub label: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub hint: Option<String>,
    #[serde(rename = "default", skip_serializing_if = "Option::is_none")]
    pub default_value: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub secret: Option<bool>,
}

impl ConfigParameter {
    pub fn new(name: impl Into<String>, required: bool, value_type: impl Into<String>) -> Self {
        Self {
            name: name.into(),
            required,
            value_type: value_type.into(),
            label: None,
            hint: None,
            default_value: None,
            secret: None,
        }
    }
}

/// Optional connection manifest (AiProxy setup). Same object as `GET /.well-known/mcp-connection.json`.
#[derive(Serialize, Deserialize, Debug, Clone, Default)]
pub struct ConnectionManifest {
    #[serde(rename = "connectionSchemaVersion")]
    pub connection_schema_version: i32,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub identity: Option<ConnectionIdentity>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub auth: Option<ConnectionAuth>,
    #[serde(rename = "platformPrerequisites", skip_serializing_if = "Option::is_none")]
    pub platform_prerequisites: Option<Vec<PlatformPrerequisite>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub oauth: Option<ConnectionOAuth>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub inject: Option<Vec<ConnectionInject>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub derived: Option<Vec<ConnectionDerived>>,
    #[serde(rename = "instanceConfig", skip_serializing_if = "Option::is_none")]
    pub instance_config: Option<Vec<ConfigParameter>>,
}

#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct ConnectionIdentity {
    pub id: String,
    #[serde(rename = "displayName")]
    pub display_name: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub portable: Option<bool>,
    #[serde(rename = "integrationType", skip_serializing_if = "Option::is_none")]
    pub integration_type: Option<String>,
    /// Optional inline SVG markup for AiProxy wizard / catalogue branding.
    #[serde(rename = "iconSvg", skip_serializing_if = "Option::is_none")]
    pub icon_svg: Option<String>,
}

#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct ConnectionAuth {
    /// `none` | `instance_config` | `mcp_oauth` | `platform_oauth`
    pub kind: String,
}

#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct PlatformPrerequisite {
    pub key: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub label: Option<String>,
}

#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct ConnectionOAuth {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub provider: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub scopes: Option<String>,
    #[serde(rename = "clientId", skip_serializing_if = "Option::is_none")]
    pub client_id: Option<String>,
}

#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct ConnectionInject {
    pub from: String,
    #[serde(rename = "intoToolProperty")]
    pub into_tool_property: String,
}

#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct ConnectionDerived {
    pub id: String,
    pub kind: String,
}

#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct ToolDescriptor {
    pub name: String,
    pub description: String,
    #[serde(rename = "inputSchema")]
    pub input_schema: Value,
}

#[derive(Serialize, Deserialize, Debug, Clone)]
pub struct PluginDescriptor {
    #[serde(rename = "serverName")]
    pub server_name: String,
    #[serde(rename = "serverVersion")]
    pub server_version: String,
    pub instructions: String,
    #[serde(rename = "configParameters")]
    pub config_parameters: Vec<ConfigParameter>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub connection: Option<ConnectionManifest>,
    pub tools: Vec<ToolDescriptor>,
}

/// Mirrors MCP's `CallToolResult` JSON shape (`ModelContextProtocol.Protocol.CallToolResult` on
/// the host side).
#[derive(Serialize, Debug, Clone)]
pub struct CallToolResult {
    pub content: Vec<ContentBlock>,
    #[serde(rename = "isError", skip_serializing_if = "Option::is_none")]
    pub is_error: Option<bool>,
}

#[derive(Serialize, Debug, Clone)]
pub struct ContentBlock {
    #[serde(rename = "type")]
    pub kind: String,
    pub text: String,
}

impl CallToolResult {
    pub fn text(text: impl Into<String>) -> Self {
        Self {
            content: vec![ContentBlock {
                kind: "text".to_string(),
                text: text.into(),
            }],
            is_error: None,
        }
    }

    pub fn error(message: impl Into<String>) -> Self {
        Self {
            content: vec![ContentBlock {
                kind: "text".to_string(),
                text: message.into(),
            }],
            is_error: Some(true),
        }
    }
}

pub type PluginResult<T> = Result<T, String>;

/// Verified caller of a request (ABI v1.1). Built by the host only from a `message_signed`
/// signature it has verified; when the request was not signed every field is empty
/// ([`CallContext::has_user`] is false). Never derive identity from tool arguments instead.
#[derive(Deserialize, Serialize, Debug, Clone, Default, PartialEq, Eq)]
#[serde(rename_all = "camelCase", default)]
pub struct CallContext {
    /// AiProxy user id (Guid string).
    pub user_id: Option<String>,
    /// Login name (LDAP uid for directory users): the key to look up this server's own permissions.
    pub username: Option<String>,
    pub name: Option<String>,
    pub email: Option<String>,
    /// AiProxy group names.
    pub groups: Vec<String>,
    /// `user` / `admin`, or `service` for calls without a user.
    pub roles: Vec<String>,
    /// Signed request id (`jti`), useful for audit logs.
    pub request_id: Option<String>,
}

impl CallContext {
    /// True when the request carries a verified user (`user_id` and/or `username`).
    /// Authorization on the MCP side still keys off [`CallContext::username`].
    pub fn has_user(&self) -> bool {
        self.user_id.as_deref().is_some_and(|u| !u.is_empty())
            || self.username.as_deref().is_some_and(|u| !u.is_empty())
    }

    pub fn in_group(&self, group: &str) -> bool {
        self.groups.iter().any(|g| g == group)
    }

    pub fn has_role(&self, role: &str) -> bool {
        self.roles.iter().any(|r| r == role)
    }
}

/// Implemented once per plugin crate; [`export_mcp_plugin!`] wires this up to the native ABI.
pub trait McpPlugin: Send + Sync + 'static {
    /// Static server/tool metadata — called once at startup (`mcp_plugin_descriptor`).
    fn descriptor(&self) -> PluginDescriptor;

    /// Bind the host's bound instance configuration (`mcp_plugin_configure`); keys are exactly
    /// the `name`s declared in `descriptor().config_parameters`.
    fn configure(&self, config: HashMap<String, Option<String>>) -> PluginResult<()>;

    /// Dispatch one `tools/call` invocation without caller information (legacy entry point).
    fn call_tool(&self, name: &str, args: Value) -> PluginResult<CallToolResult>;

    /// Dispatch one `tools/call` with the verified caller (`mcp_plugin_call_tool_ctx`). Every
    /// generated export routes through this method; the default ignores the context and calls
    /// [`McpPlugin::call_tool`], so existing plugins behave exactly as before.
    ///
    /// Plugins that authorize from the caller **must** override this method (or set
    /// [`McpPlugin::requires_user_context`] so the host refuses unsigned traffic). Relying on the
    /// default drops `context` on the floor.
    fn call_tool_with_context(
        &self,
        name: &str,
        args: Value,
        _context: &CallContext,
    ) -> PluginResult<CallToolResult> {
        self.call_tool(name, args)
    }

    /// Tool census for this caller (`mcp_plugin_list_tools_ctx`): names the caller may see and
    /// call, or `None` for no restriction (default). The host filters `tools/list` with it and
    /// refuses `tools/call` for tools not listed.
    fn allowed_tools(&self, _context: &CallContext) -> Option<Vec<String>> {
        None
    }

    /// Declare that every call needs a verified user. Published as `requiresUserContext` in the
    /// descriptor; the host then refuses to start unless `MessageSignature required`.
    fn requires_user_context(&self) -> bool {
        false
    }
}

/// Internals used by [`export_mcp_plugin!`] — not part of the plugin-author-facing API surface,
/// but `pub` because macro-generated code lives in the plugin crate, not this one.
#[doc(hidden)]
pub mod ffi {
    use std::ffi::{CStr, CString};
    use std::os::raw::c_char;

    /// # Safety
    /// `ptr` must be null or a valid pointer to a null-terminated UTF-8 C string.
    pub unsafe fn c_str_to_string(ptr: *const c_char) -> String {
        if ptr.is_null() {
            return String::new();
        }
        unsafe { CStr::from_ptr(ptr) }.to_string_lossy().into_owned()
    }

    /// Allocates a new C string this module owns; must be freed via [`free_c_char`] (never by the
    /// caller's own allocator — that's the whole point of pairing every `mcp_plugin_*` export that
    /// returns a string with `mcp_free_string`).
    pub fn string_to_c_char(s: String) -> *mut c_char {
        CString::new(s)
            .unwrap_or_else(|_| CString::new("").unwrap())
            .into_raw()
    }

    /// Host context JSON → [`crate::CallContext`]. Unparseable input yields the anonymous
    /// context (no user), never a partially trusted one.
    pub fn parse_context(json: &str) -> crate::CallContext {
        serde_json::from_str(json).unwrap_or_default()
    }

    /// # Safety
    /// `ptr` must have been returned by [`string_to_c_char`] in this same module and not freed yet.
    pub unsafe fn free_c_char(ptr: *mut c_char) {
        if !ptr.is_null() {
            drop(unsafe { CString::from_raw(ptr) });
        }
    }
}

/// Generates the `extern "C"` exports (5 legacy + 2 ABI v1.1) the host
/// resolves via `dlopen`. Call once at
/// crate root: `export_mcp_plugin!(MyPlugin, MyPlugin::new());`.
#[macro_export]
macro_rules! export_mcp_plugin {
    ($plugin_type:ty, $ctor:expr) => {
        static __MCP_PLUGIN: ::std::sync::OnceLock<$plugin_type> = ::std::sync::OnceLock::new();
        static __MCP_LAST_ERROR: ::std::sync::Mutex<Option<String>> =
            ::std::sync::Mutex::new(None);

        fn __mcp_plugin_instance() -> &'static $plugin_type {
            __MCP_PLUGIN.get_or_init(|| $ctor)
        }

        #[no_mangle]
        pub extern "C" fn mcp_plugin_descriptor() -> *mut ::std::os::raw::c_char {
            let plugin = __mcp_plugin_instance();
            let descriptor = $crate::McpPlugin::descriptor(plugin);
            let mut value = ::serde_json::to_value(&descriptor).unwrap_or(::serde_json::Value::Null);
            if $crate::McpPlugin::requires_user_context(plugin) {
                if let Some(obj) = value.as_object_mut() {
                    obj.insert("requiresUserContext".to_string(), ::serde_json::Value::Bool(true));
                }
            }
            let json = ::serde_json::to_string(&value).unwrap_or_else(|_| "{}".to_string());
            $crate::ffi::string_to_c_char(json)
        }

        // C ABI entry point: the host owns the pointer contract (documented in PLUGIN_ABI.md).
        #[allow(clippy::not_unsafe_ptr_arg_deref)]
        #[no_mangle]
        pub extern "C" fn mcp_plugin_configure(
            config_json: *const ::std::os::raw::c_char,
        ) -> ::std::os::raw::c_int {
            let json = unsafe { $crate::ffi::c_str_to_string(config_json) };
            let parsed: ::std::collections::HashMap<String, Option<String>> =
                match ::serde_json::from_str(&json) {
                    Ok(v) => v,
                    Err(e) => {
                        *__MCP_LAST_ERROR.lock().unwrap() =
                            Some(format!("invalid config JSON: {e}"));
                        return 1;
                    }
                };
            match $crate::McpPlugin::configure(__mcp_plugin_instance(), parsed) {
                Ok(()) => 0,
                Err(e) => {
                    $crate::log_error!("configure failed: {e}");
                    *__MCP_LAST_ERROR.lock().unwrap() = Some(e);
                    1
                }
            }
        }

        fn __mcp_dispatch(
            name: &str,
            args_str: &str,
            context: &$crate::CallContext,
        ) -> *mut ::std::os::raw::c_char {
            let args: ::serde_json::Value =
                ::serde_json::from_str(args_str).unwrap_or(::serde_json::Value::Null);

            let outcome = ::std::panic::catch_unwind(::std::panic::AssertUnwindSafe(|| {
                $crate::McpPlugin::call_tool_with_context(__mcp_plugin_instance(), name, args, context)
            }));

            let call_result = match outcome {
                Ok(Ok(r)) => r,
                Ok(Err(e)) => {
                    $crate::log_warn!("tool '{name}' failed: {e}");
                    $crate::CallToolResult::error(e)
                }
                Err(panic_payload) => {
                    let detail = panic_payload
                        .downcast_ref::<&str>()
                        .map(|s| s.to_string())
                        .or_else(|| panic_payload.downcast_ref::<String>().cloned())
                        .unwrap_or_else(|| "no panic message available".to_string());
                    $crate::log_error!("tool '{name}' panicked: {detail}");
                    $crate::CallToolResult::error("plugin panicked while handling tool call")
                }
            };

            let json = ::serde_json::to_string(&call_result).unwrap_or_else(|_| {
                r#"{"content":[{"type":"text","text":"failed to serialize tool result"}],"isError":true}"#
                    .to_string()
            });
            $crate::ffi::string_to_c_char(json)
        }

        // C ABI entry point: the host owns the pointer contract (documented in PLUGIN_ABI.md).
        #[allow(clippy::not_unsafe_ptr_arg_deref)]
        #[no_mangle]
        pub extern "C" fn mcp_plugin_call_tool(
            tool_name: *const ::std::os::raw::c_char,
            args_json: *const ::std::os::raw::c_char,
        ) -> *mut ::std::os::raw::c_char {
            let name = unsafe { $crate::ffi::c_str_to_string(tool_name) };
            let args_str = unsafe { $crate::ffi::c_str_to_string(args_json) };
            __mcp_dispatch(&name, &args_str, &$crate::CallContext::default())
        }

        /// ABI v1.1: same as `mcp_plugin_call_tool` plus the host-verified caller.
        // C ABI entry point: the host owns the pointer contract (documented in PLUGIN_ABI.md).
        #[allow(clippy::not_unsafe_ptr_arg_deref)]
        #[no_mangle]
        pub extern "C" fn mcp_plugin_call_tool_ctx(
            tool_name: *const ::std::os::raw::c_char,
            args_json: *const ::std::os::raw::c_char,
            context_json: *const ::std::os::raw::c_char,
        ) -> *mut ::std::os::raw::c_char {
            let name = unsafe { $crate::ffi::c_str_to_string(tool_name) };
            let args_str = unsafe { $crate::ffi::c_str_to_string(args_json) };
            let context = $crate::ffi::parse_context(unsafe { &$crate::ffi::c_str_to_string(context_json) });
            __mcp_dispatch(&name, &args_str, &context)
        }

        /// ABI v1.1: JSON array of tool names this caller may use, or `null` = no restriction.
        /// A panic returns `[]` (fail closed).
        // C ABI entry point: the host owns the pointer contract (documented in PLUGIN_ABI.md).
        #[allow(clippy::not_unsafe_ptr_arg_deref)]
        #[no_mangle]
        pub extern "C" fn mcp_plugin_list_tools_ctx(
            context_json: *const ::std::os::raw::c_char,
        ) -> *mut ::std::os::raw::c_char {
            let context = $crate::ffi::parse_context(unsafe { &$crate::ffi::c_str_to_string(context_json) });
            let outcome = ::std::panic::catch_unwind(::std::panic::AssertUnwindSafe(|| {
                $crate::McpPlugin::allowed_tools(__mcp_plugin_instance(), &context)
            }));
            let json = match outcome {
                Ok(Some(names)) => ::serde_json::to_string(&names).unwrap_or_else(|_| "[]".to_string()),
                Ok(None) => "null".to_string(),
                Err(_) => {
                    $crate::log_error!("allowed_tools panicked; denying all tools for this caller");
                    "[]".to_string()
                }
            };
            $crate::ffi::string_to_c_char(json)
        }

        // C ABI entry point: the host owns the pointer contract (documented in PLUGIN_ABI.md).
        #[allow(clippy::not_unsafe_ptr_arg_deref)]
        #[no_mangle]
        pub extern "C" fn mcp_free_string(ptr: *mut ::std::os::raw::c_char) {
            unsafe { $crate::ffi::free_c_char(ptr) }
        }

        #[no_mangle]
        pub extern "C" fn mcp_plugin_last_error() -> *mut ::std::os::raw::c_char {
            let message = __MCP_LAST_ERROR.lock().unwrap().clone().unwrap_or_default();
            $crate::ffi::string_to_c_char(message)
        }
    };
}

#[cfg(test)]
mod tests {
    use super::*;

    // The wire shapes below are the ABI contract the host parses. They intentionally use the protocol's camelCase
    // keys, not product snake_case.
    #[test]
    fn descriptor_serializes_with_abi_keys() {
        let descriptor = PluginDescriptor {
            server_name: "fixture".into(),
            server_version: "1.0.0".into(),
            instructions: String::new(),
            config_parameters: vec![ConfigParameter::new("BaseUrl", false, "string")],
            connection: Some(ConnectionManifest {
                connection_schema_version: 1,
                identity: Some(ConnectionIdentity {
                    id: "fixture".into(),
                    display_name: "Fixture".into(),
                    portable: Some(false),
                    integration_type: None,
                    icon_svg: None,
                }),
                auth: Some(ConnectionAuth {
                    kind: "instance_config".into(),
                }),
                platform_prerequisites: None,
                oauth: None,
                inject: None,
                derived: None,
                instance_config: None,
            }),
            tools: vec![ToolDescriptor {
                name: "echo".into(),
                description: "d".into(),
                input_schema: serde_json::json!({ "type": "object" }),
            }],
        };

        let json = serde_json::to_value(&descriptor).unwrap();
        assert!(json.get("serverName").is_some());
        assert!(json.get("serverVersion").is_some());
        assert!(json.get("configParameters").is_some());
        assert!(json.get("connection").is_some());
        assert_eq!(json["connection"]["connectionSchemaVersion"], 1);
        assert_eq!(json["tools"][0].get("inputSchema").unwrap()["type"], "object");
        assert!(json["configParameters"][0].get("type").is_some());
    }

    struct LegacyPlugin;

    impl McpPlugin for LegacyPlugin {
        fn descriptor(&self) -> PluginDescriptor {
            unimplemented!()
        }

        fn configure(&self, _config: HashMap<String, Option<String>>) -> PluginResult<()> {
            Ok(())
        }

        fn call_tool(&self, name: &str, _args: Value) -> PluginResult<CallToolResult> {
            Ok(CallToolResult::text(name))
        }
    }

    #[test]
    fn defaults_keep_legacy_plugins_unchanged() {
        let ctx = CallContext {
            user_id: Some("u".into()),
            ..Default::default()
        };
        let plugin = LegacyPlugin;
        assert_eq!(
            plugin.call_tool_with_context("t", Value::Null, &ctx).unwrap().content[0].text,
            "t"
        );
        assert!(plugin.allowed_tools(&ctx).is_none());
        assert!(!plugin.requires_user_context());
    }

    #[test]
    fn call_context_parses_abi_camel_case() {
        let ctx: CallContext = serde_json::from_str(
            r#"{"userId":"u","name":"N","groups":["a"],"roles":["admin"],"requestId":"r","extra":1}"#,
        )
        .unwrap();
        assert!(ctx.has_user());
        assert!(ctx.in_group("a"));
        assert!(ctx.has_role("admin"));
        assert_eq!(ctx.request_id.as_deref(), Some("r"));
        assert!(!ffi::parse_context("{}").has_user());
        assert!(!ffi::parse_context(r#"{"userId":""}"#).has_user());
        assert!(ffi::parse_context(r#"{"username":"mrossi"}"#).has_user());
        assert!(!ffi::parse_context("garbage").has_user());
    }

    #[test]
    fn call_tool_result_omits_is_error_when_success() {
        let ok = serde_json::to_value(CallToolResult::text("hi")).unwrap();
        assert_eq!(ok["content"][0]["type"], "text");
        assert_eq!(ok["content"][0]["text"], "hi");
        assert!(ok.get("isError").is_none());

        let err = serde_json::to_value(CallToolResult::error("boom")).unwrap();
        assert_eq!(err["isError"], true);
    }
}
