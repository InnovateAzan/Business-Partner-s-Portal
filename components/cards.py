from dash import html


def page_header(title, subtitle):
    return html.Div(className="section-header", children=[html.H2(title), html.P(subtitle)])


def _safe_value(data, key, default=0):
    return data.get(key, default) if isinstance(data, dict) else default


def metric_box(label, value, subtitle, color):
    return html.Div(
        className="summary-metric",
        children=[html.P(label), html.H2(str(value), className=f"{color}-text"), html.Span(subtitle)],
    )


def machine_summary_card(kpis):
    total = int(kpis.get("total", 0) or 0)
    running = int(kpis.get("running", 0) or 0)
    idle = int(kpis.get("idle", 0) or 0)
    halt_stop = int(kpis.get("halt_stop", 0) or 0)
    stopped = int(kpis.get("stopped", 0) or 0)

    return html.Div(
        className="machine-summary-prototype",
        children=[
            html.Div(
                className="machine-summary-header",
                children=[
                    html.Div(className="machine-summary-icon icon-machine-summary"),
                    html.Div("Machine Summary", className="machine-summary-title"),
                ],
            ),
            html.Div(
                className="machine-summary-main",
                children=[
                    html.Div(str(total), className="machine-summary-total"),
                    html.Div("Total Machines", className="machine-summary-total-label"),
                ],
            ),
            html.Div(
                className="machine-summary-progress",
                children=[html.Div(className="machine-summary-progress-fill", style={"width": "100%"})],
            ),
            html.Div(
                className="machine-summary-footer",
                children=[
                    html.Span([html.Strong(str(running)), " Running"], className="machine-summary-running"),
                    html.Span("|", className="machine-summary-separator"),
                    html.Span([html.Strong(str(idle)), " Idle"], className="machine-summary-idle"),
                    html.Span("|", className="machine-summary-separator"),
                    html.Span([html.Strong(str(halt_stop)), " Halt"], className="machine-summary-halt"),
                    html.Span("|", className="machine-summary-separator"),
                    html.Span([html.Strong(str(stopped)), " Stopped"], className="machine-summary-stopped"),
                ],
            ),
        ],
    )


def kpi_card(title, value, subtitle, icon, color, spark_value=None):
    try:
        progress = max(0, min(100, float(spark_value or 0)))
    except (TypeError, ValueError):
        progress = 0

    return html.Div(
        className=f"kpi-card kpi-card-pro kpi-card-{color}",
        children=[
            html.Div(className=f"kpi-icon kpi-icon-{color} kpi-glyph-{color}"),
            html.Div(
                className="kpi-content",
                children=[
                    html.P(title),
                    html.H2(str(value)),
                    html.Span(str(subtitle), className="kpi-subtitle"),
                    html.Div(
                        className="kpi-mini-visual",
                        children=[html.Span(className="kpi-mini-fill", style={"width": f"{progress}%"})],
                    ),
                ],
            ),
        ],
    )


def production_kpi_card(title, actual, target, subtitle, spark_value=None):
    try:
        progress = max(0, min(100, float(spark_value or 0)))
    except (TypeError, ValueError):
        progress = 0

    return html.Div(
        className="kpi-card kpi-card-pro kpi-card-production production-top-kpi",
        children=[
            html.Div(className="kpi-icon kpi-icon-production kpi-glyph-production"),
            html.Div(
                className="kpi-content production-top-kpi-content",
                children=[
                    html.P(title),
                    html.H2(str(actual)),
                    html.Span(str(subtitle), className="kpi-subtitle"),
                    html.Small(str(target)),
                    html.Div(
                        className="kpi-mini-visual",
                        children=[html.Span(className="kpi-mini-fill", style={"width": f"{progress}%"})],
                    ),
                ],
            ),
        ],
    )
