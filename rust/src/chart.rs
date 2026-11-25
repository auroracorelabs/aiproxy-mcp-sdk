//! Declarative AiProxy chart specs for MCP tool results.
//!
//! Product JSON uses snake_case. Embed via [`crate::CallToolResult::chart`] as an MCP
//! `resource` block with [`CHART_MIME_TYPE`].

use serde::{Deserialize, Serialize};

/// MIME type for chart JSON in an MCP embedded resource.
pub const CHART_MIME_TYPE: &str = "application/vnd.aiproxy.chart+json";

/// Closed chart kinds (v1).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ChartKind {
    Line,
    Bar,
    Donut,
    Gauge,
}

/// One series on a line chart.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct ChartSeries {
    pub id: String,
    pub label: String,
    /// One value per x label; `null` leaves a gap.
    pub values: Vec<Option<f64>>,
}

/// Horizontal threshold on a line chart.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct ChartReferenceLine {
    pub value: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub label: Option<String>,
}

/// One bar on a bar chart.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct ChartBar {
    pub label: String,
    pub value: f64,
}

/// One slice on a donut chart.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct ChartSlice {
    pub id: String,
    pub label: String,
    pub value: f64,
}

/// Declarative chart payload (snake_case product JSON).
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(tag = "kind", rename_all = "snake_case")]
pub enum ChartSpec {
    Line {
        #[serde(skip_serializing_if = "Option::is_none")]
        title: Option<String>,
        aria_label: String,
        labels: Vec<String>,
        series: Vec<ChartSeries>,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        reference_lines: Option<Vec<ChartReferenceLine>>,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        include_zero: Option<bool>,
    },
    Bar {
        #[serde(skip_serializing_if = "Option::is_none")]
        title: Option<String>,
        aria_label: String,
        bars: Vec<ChartBar>,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        series_label: Option<String>,
    },
    Donut {
        #[serde(skip_serializing_if = "Option::is_none")]
        title: Option<String>,
        aria_label: String,
        slices: Vec<ChartSlice>,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        total: Option<f64>,
    },
    Gauge {
        #[serde(skip_serializing_if = "Option::is_none")]
        title: Option<String>,
        aria_label: String,
        value: f64,
        min: f64,
        max: f64,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        points: Option<Vec<f64>>,
    },
}

impl ChartSpec {
    /// Rejects empty series/bars/slices, mismatched lengths, and gauge out of range.
    pub fn validate(&self) -> Result<(), String> {
        match self {
            ChartSpec::Line {
                aria_label,
                labels,
                series,
                ..
            } => {
                require_aria(aria_label)?;
                if series.is_empty() {
                    return Err("line chart requires at least one series".into());
                }
                for s in series {
                    if s.id.trim().is_empty() || s.label.trim().is_empty() {
                        return Err("line series id and label are required".into());
                    }
                    if s.values.len() != labels.len() {
                        return Err(format!(
                            "line series '{}' values length ({}) must match labels ({})",
                            s.id,
                            s.values.len(),
                            labels.len()
                        ));
                    }
                }
                Ok(())
            }
            ChartSpec::Bar {
                aria_label, bars, ..
            } => {
                require_aria(aria_label)?;
                if bars.is_empty() {
                    return Err("bar chart requires at least one bar".into());
                }
                Ok(())
            }
            ChartSpec::Donut {
                aria_label,
                slices,
                total,
                ..
            } => {
                require_aria(aria_label)?;
                if slices.is_empty() {
                    return Err("donut chart requires at least one slice".into());
                }
                if slices.iter().any(|s| s.id.trim().is_empty() || s.label.trim().is_empty()) {
                    return Err("donut slice id and label are required".into());
                }
                if let Some(t) = total {
                    if !t.is_finite() || *t < 0.0 {
                        return Err("donut total must be a non-negative finite number".into());
                    }
                }
                Ok(())
            }
            ChartSpec::Gauge {
                aria_label,
                value,
                min,
                max,
                ..
            } => {
                require_aria(aria_label)?;
                if !min.is_finite() || !max.is_finite() || !value.is_finite() {
                    return Err("gauge min, max, and value must be finite".into());
                }
                if min >= max {
                    return Err("gauge min must be less than max".into());
                }
                if *value < *min || *value > *max {
                    return Err(format!(
                        "gauge value {value} is outside [{min}, {max}]"
                    ));
                }
                Ok(())
            }
        }
    }

    /// Serializes validated chart JSON (snake_case).
    pub fn to_json_string(&self) -> Result<String, String> {
        self.validate()?;
        serde_json::to_string(self).map_err(|e| e.to_string())
    }
}

fn require_aria(aria_label: &str) -> Result<(), String> {
    if aria_label.trim().is_empty() {
        return Err("aria_label is required".into());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn line_round_trips_snake_case() {
        let spec = ChartSpec::Line {
            title: Some("Monthly KPI".into()),
            aria_label: "Monthly KPI".into(),
            labels: vec!["Jan".into(), "Feb".into()],
            series: vec![ChartSeries {
                id: "main".into(),
                label: "Value".into(),
                values: vec![Some(42.0), None],
            }],
            reference_lines: Some(vec![ChartReferenceLine {
                value: 50.0,
                label: Some("Target".into()),
            }]),
            include_zero: Some(true),
        };
        let json = serde_json::to_value(&spec).unwrap();
        assert_eq!(json["kind"], "line");
        assert_eq!(json["aria_label"], "Monthly KPI");
        assert!(json["series"][0]["values"][1].is_null());
        let back: ChartSpec = serde_json::from_value(json).unwrap();
        assert_eq!(back, spec);
    }

    #[test]
    fn gauge_rejects_out_of_range() {
        let spec = ChartSpec::Gauge {
            title: None,
            aria_label: "g".into(),
            value: 150.0,
            min: 0.0,
            max: 100.0,
            points: None,
        };
        assert!(spec.validate().is_err());
    }

    #[test]
    fn unknown_kind_fails_deserialize() {
        let err = serde_json::from_str::<ChartSpec>(r#"{"kind":"pie","aria_label":"x"}"#);
        assert!(err.is_err());
    }
}
