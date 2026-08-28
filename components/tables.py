from dash import html
import pandas as pd
from zoneinfo import ZoneInfo


LOCAL_TZ = ZoneInfo("Asia/Karachi")


def _safe_text(value):
    if pd.isna(value):
        return ""
    return str(value)


def _status_class(value):
    value = str(value).lower().strip()
    return value.replace(" / ", "-").replace(" ", "-")


def _safe_float(value, default=0.0):
    try:
        return float(value)
    except Exception:
        return default


def _format_stopped_since(value):
    ts = pd.to_datetime(value, errors="coerce", utc=True)
    if pd.isna(ts):
        return ""
    return ts.tz_convert(LOCAL_TZ).strftime("%d %b, %I:%M %p")


def _machine_name_cell(value):
    return html.Span(
        [html.Span("●", className="stopped-machine-dot"), html.Span(_safe_text(value), className="stopped-machine-name")],
        className="stopped-machine-cell",
    )


def _duration_badge(value):
    return html.Span(_safe_text(value), className="duration-pill")


def stopped_machines_card_header(title="Stopped Machines", subtitle="Real-time stopped machine detail", link_text="View All", link_href="#"):
    return html.Div(
        className="stopped-card-header",
        children=[
            html.Div(
                className="stopped-card-title-wrap",
                children=[
                    html.Div("■", className="stopped-card-icon"),
                    html.Div(
                        className="stopped-card-copy",
                        children=[
                            html.H3(title, className="stopped-card-title"),
                            html.P(subtitle, className="stopped-card-subtitle"),
                        ],
                    ),
                ],
            ),
            html.A(link_text, href=link_href, className="stopped-view-all"),
        ],
    )


def stopped_machines_table(df, max_rows=None, table_class="stopped-machine-table"):
    if df is None or df.empty:
        return html.Div("No data available", className="empty-state")

    df = df.copy()
    if max_rows:
        df = df.head(max_rows)

    columns = [c for c in df.columns if c in {"Machine Name", "Department", "Process Name", "Process", "Since From", "Duration", "Reason", "Work Order", "Job ID"}]
    if not columns:
        columns = list(df.columns)

    def _render_cell(col, value):
        col_lower = str(col).lower()
        if col_lower in {"machine name", "machine"}:
            return _machine_name_cell(value)
        if col_lower in {"since from", "stopped since"}:
            return html.Span(_format_stopped_since(value), className="stopped-since-text")
        if col_lower in {"duration", "downtime"}:
            return _duration_badge(value)
        return _safe_text(value)

    header_map = {
        "Machine Name": "Machine",
        "Process Name": "Process",
        "Since From": "Stopped Since",
    }

    return html.Div(
        className="table-wrapper compact-table-wrapper stopped-table-wrapper",
        children=[
            html.Table(
                className=f"data-table {table_class}",
                children=[
                    html.Thead(html.Tr([html.Th(header_map.get(col, col)) for col in columns])),
                    html.Tbody([
                        html.Tr([
                            html.Td(_render_cell(col, row[col])) for col in columns
                        ])
                        for _, row in df.iterrows()
                    ]),
                ],
            )
        ],
    )



def _format_idle_since(value):
    ts = pd.to_datetime(value, errors="coerce", utc=True)
    if pd.isna(ts):
        return ""
    return ts.tz_convert(LOCAL_TZ).strftime("%d %b, %I:%M %p")


def _idle_machine_name_cell(value):
    return html.Span(
        [
            html.Span("●", className="idle-machine-dot"),
            html.Span(_safe_text(value), className="idle-machine-name"),
        ],
        className="idle-machine-cell",
    )


def _idle_duration_badge(value):
    return html.Span(_safe_text(value), className="idle-duration-pill")


def idle_machines_table(df, max_rows=None):
    if df is None or df.empty:
        return html.Div("No data available", className="empty-state")

    df = df.copy()
    if max_rows:
        df = df.head(max_rows)

    columns = [c for c in df.columns if c in {"Machine Name", "Since From", "Duration"}]
    if not columns:
        columns = list(df.columns)

    header_map = {
        "Machine Name": "Machine",
        "Since From": "Idle Since",
    }

    def _render_cell(col, value):
        col_lower = str(col).lower()
        if col_lower in {"machine name", "machine"}:
            return _idle_machine_name_cell(value)
        if col_lower in {"since from", "idle since"}:
            return html.Span(_format_idle_since(value), className="idle-since-text")
        if col_lower in {"duration", "idle duration"}:
            return _idle_duration_badge(value)
        return _safe_text(value)

    return html.Div(
        className="table-wrapper compact-table-wrapper idle-table-wrapper",
        children=[
            html.Table(
                className="data-table idle-machine-table",
                children=[
                    html.Thead(html.Tr([html.Th(header_map.get(col, col)) for col in columns])),
                    html.Tbody([
                        html.Tr([html.Td(_render_cell(col, row[col])) for col in columns])
                        for _, row in df.iterrows()
                    ]),
                ],
            )
        ],
    )

def status_badge(status):
    return html.Span(
        _safe_text(status),
        className=f"status-pill {_status_class(status)}",
    )


def card_header(title, link_text=None, link_href=None):
    children = [
        html.H3(title, className="card-title"),
    ]

    if link_text and link_href:
        children.append(
            html.A(
                link_text,
                href=link_href,
                className="card-view-link",
            )
        )

    return html.Div(className="card-header-row", children=children)

def idle_machines_card_header(
    title="Idle Machines",
    subtitle="Currently idle machines",
):
    return html.Div(
        className="idle-card-header",
        children=[
            html.Div(
                className="idle-card-icon",
                children=html.Span(className="idle-card-icon-glyph"),
            ),
            html.Div(
                className="idle-card-copy",
                children=[
                    html.H3(title, className="idle-card-title"),
                    html.P(subtitle, className="idle-card-subtitle"),
                ],
            ),
        ],
    )


def data_table(df, status_col=None, max_rows=None, **kwargs):
    if df is None or df.empty:
        return html.Div("No data available", className="empty-state")

    df = df.copy()

    if max_rows:
        df = df.head(max_rows)

    return html.Div(
        className="table-wrapper compact-table-wrapper",
        children=[
            html.Table(
                className="data-table",
                children=[
                    html.Thead(
                        html.Tr([html.Th(col) for col in df.columns])
                    ),
                    html.Tbody(
                        [
                            html.Tr(
                                [
                                    html.Td(
                                        status_badge(row[col])
                                        if (
                                            status_col
                                            and col == status_col
                                        )
                                        or (
                                            not status_col
                                            and str(col).lower() == "status"
                                        )
                                        else _safe_text(row[col])
                                    )
                                    for col in df.columns
                                ]
                            )
                            for _, row in df.iterrows()
                        ]
                    ),
                ],
            )
        ],
    )

def alert_list(alerts, max_items=None):
    if not alerts:
        return html.Div("No alerts available", className="empty-state")

    if max_items is not None:
        alerts = alerts[:max_items]

    return html.Div(
        className="alert-list",
        children=[
            html.Div(
                className="alert-item",
                children=[
                    html.Div("⚠", className="alert-icon"),
                    html.Div(
                        className="alert-content",
                        children=[
                            html.Div(
                                item.get("Message") or item.get("Alert") or "Alert",
                                className="alert-message",
                            ),
                            html.Div(
                                item.get("Priority", ""),
                                className="alert-priority",
                            ),
                        ],
                    ),
                    html.Div(
                        item.get("Time", ""),
                        className="alert-time",
                    ),
                ],
            )
            for item in alerts
        ],
    )


def maintenance_list(df, max_items=4):
    if df is None or df.empty:
        return html.Div("No maintenance data available", className="empty-state")

    df = df.copy()

    if "Maintenance" not in df.columns:
        if "Maintenance Type" in df.columns:
            df["Maintenance"] = df["Maintenance Type"]
        elif "Status" in df.columns:
            df["Maintenance"] = df["Status"]
        else:
            df["Maintenance"] = "Scheduled Maintenance"

    if "Count" not in df.columns:
        df["Count"] = 1

    df = df.head(max_items)

    return html.Div(
        className="maintenance-list compact-side-list",
        children=[
            html.Div(
                className="maintenance-item-pro",
                children=[
                    html.Div(
                        className="maintenance-left",
                        children=[
                            html.Div("🔧", className="maintenance-icon-pro"),
                            html.Div(_safe_text(row["Maintenance"])),
                        ],
                    ),
                    html.Strong(_safe_text(row["Count"])),
                ],
            )
            for _, row in df.iterrows()
        ],
    )
