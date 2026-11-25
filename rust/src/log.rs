//! Reusable logging for plugin authors. Writes single-line, leveled messages to stderr, which
//! systemd already journals per plugin instance (`mcp@<instanceId>.service`, no
//! `StandardOutput=`/`StandardError=` override). No new FFI
//! surface: plugin and host share one OS process per instance, so this needs nothing from the
//! host side to reach the journal.
//!
//! [`export_mcp_plugin!`](crate::export_mcp_plugin) already calls [`log`] automatically on
//! `call_tool` logical errors/panics and `configure` failures — plugin authors only need this
//! module directly for extra context (`log_info!`, `log_debug!`, ...).

use std::io::{IsTerminal, Write};

/// Maximum message length written to a single journal line; longer messages are truncated so a
/// single embedded `\n` (replaced below) or an unbounded payload can't split across journal
/// entries and desynchronize the leading level prefix.
const MAX_MESSAGE_LEN: usize = 4000;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Level {
    Error,
    Warn,
    Info,
    Debug,
    Trace,
}

impl Level {
    fn label(self) -> &'static str {
        match self {
            Level::Error => "ERROR",
            Level::Warn => "WARN",
            Level::Info => "INFO",
            Level::Debug => "DEBUG",
            Level::Trace => "TRACE",
        }
    }

    /// Standard syslog priority number (see `sd-daemon(3)`, "Log Level Prefixes"). systemd's
    /// journal stdio bridge recognizes a leading `<N>` on a stderr/stdout line, strips it, and
    /// uses it to set the journal entry's `PRIORITY` field — giving accurate level-based
    /// filtering/coloring in `journalctl` and the host log UI without any text parsing.
    fn syslog_priority(self) -> u8 {
        match self {
            Level::Error => 3,
            Level::Warn => 4,
            Level::Info => 6,
            Level::Debug | Level::Trace => 7,
        }
    }
}

/// Builds the exact line [`log`] writes, without touching I/O — kept separate so the wire format
/// (the actual contract systemd/journald and the host log UI parse) is unit-testable
/// without redirecting the real stderr fd.
fn format_line(level: Level, message: &str, is_terminal: bool) -> String {
    let mut single_line = message.replace('\n', " ");
    if single_line.len() > MAX_MESSAGE_LEN {
        single_line.truncate(MAX_MESSAGE_LEN);
        single_line.push_str("...(truncated)");
    }

    // Under systemd, stderr is a pipe the journal reads: the numeric prefix is required there and
    // gets stripped before the message is stored. In a plain terminal (local `cargo run`/tests)
    // there's no journal bridge to interpret it, so skip it — otherwise every line would show a
    // literal "<6>" before the level tag.
    if is_terminal {
        format!("[{}] {}", level.label(), single_line)
    } else {
        format!("<{}>[{}] {}", level.syslog_priority(), level.label(), single_line)
    }
}

/// Writes one leveled line to stderr. Plugin authors should prefer the [`crate::log_error`],
/// [`crate::log_warn`], [`crate::log_info`], [`crate::log_debug`], [`crate::log_trace`] macros
/// over calling this directly.
pub fn log(level: Level, message: &str) {
    let stderr = std::io::stderr();
    let mut handle = stderr.lock();
    let line = format_line(level, message, handle.is_terminal());
    let _ = writeln!(handle, "{line}");
    let _ = handle.flush();
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn non_terminal_gets_syslog_priority_prefix() {
        assert_eq!(format_line(Level::Error, "boom", false), "<3>[ERROR] boom");
        assert_eq!(format_line(Level::Warn, "careful", false), "<4>[WARN] careful");
        assert_eq!(format_line(Level::Info, "hi", false), "<6>[INFO] hi");
        assert_eq!(format_line(Level::Debug, "d", false), "<7>[DEBUG] d");
        assert_eq!(format_line(Level::Trace, "t", false), "<7>[TRACE] t");
    }

    #[test]
    fn terminal_omits_priority_prefix() {
        assert_eq!(format_line(Level::Error, "boom", true), "[ERROR] boom");
    }

    #[test]
    fn embedded_newlines_are_collapsed_to_a_single_journal_line() {
        assert_eq!(format_line(Level::Info, "line one\nline two", false), "<6>[INFO] line one line two");
    }

    #[test]
    fn overlong_messages_are_truncated() {
        let huge = "x".repeat(MAX_MESSAGE_LEN + 500);
        let line = format_line(Level::Warn, &huge, false);
        assert!(line.ends_with("...(truncated)"));
        assert!(line.len() < huge.len());
    }
}

/// `format!`-style logging macros re-exported from the crate root, e.g.
/// `aiproxy_mcp_sdk::log_warn!("MediaWiki search failed: {e}")`.
#[macro_export]
macro_rules! log_error {
    ($($arg:tt)*) => {
        $crate::log::log($crate::log::Level::Error, &::std::format!($($arg)*))
    };
}

#[macro_export]
macro_rules! log_warn {
    ($($arg:tt)*) => {
        $crate::log::log($crate::log::Level::Warn, &::std::format!($($arg)*))
    };
}

#[macro_export]
macro_rules! log_info {
    ($($arg:tt)*) => {
        $crate::log::log($crate::log::Level::Info, &::std::format!($($arg)*))
    };
}

#[macro_export]
macro_rules! log_debug {
    ($($arg:tt)*) => {
        $crate::log::log($crate::log::Level::Debug, &::std::format!($($arg)*))
    };
}

#[macro_export]
macro_rules! log_trace {
    ($($arg:tt)*) => {
        $crate::log::log($crate::log::Level::Trace, &::std::format!($($arg)*))
    };
}
