from dash import html
import math


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
    """
    Machine Summary card.

    Only this KPI uses a MULTI-COLOR gradient bar:
        Green -> Orange -> Purple

    The footer still shows the real live counts for:
        Running / Idle / Halt
    """
    total = int(kpis.get("total", 0) or 0)
    running = int(kpis.get("running", 0) or 0)
    idle = int(kpis.get("idle", 0) or 0)
    halt_stop = int(kpis.get("halt_stop", 0) or 0)

    return html.Div(
        className="machine-summary-prototype",
        children=[
            html.Div(
                className="machine-summary-header",
                children=[
                    html.Div(
                        className="machine-summary-icon icon-machine-summary"
                    ),
                    html.Div(
                        "Machine Summary",
                        className="machine-summary-title",
                    ),
                ],
            ),

            html.Div(
                className="machine-summary-main",
                children=[
                    html.Div(
                        str(total),
                        className="machine-summary-total",
                    ),
                    html.Div(
                        "Total Machines",
                        className="machine-summary-total-label",
                    ),
                ],
            ),

            # Multi-colour gradient is intentionally used ONLY here.
            html.Div(
                className="machine-summary-gradient-track",
                children=[
                    html.Div(
                        className="machine-summary-gradient-fill"
                    )
                ],
            ),

            html.Div(
                className="machine-summary-footer",
                children=[
                    html.Span(
                        [
                            html.Span(
                                className=(
                                    "machine-summary-footer-dot "
                                    "machine-summary-dot-running"
                                )
                            ),
                            html.Strong(str(running)),
                            " Running",
                        ],
                        className="machine-summary-running",
                    ),

                    html.Span(
                        [
                            html.Span(
                                className=(
                                    "machine-summary-footer-dot "
                                    "machine-summary-dot-idle"
                                )
                            ),
                            html.Strong(str(idle)),
                            " Idle",
                        ],
                        className="machine-summary-idle",
                    ),

                    html.Span(
                        [
                            html.Span(
                                className=(
                                    "machine-summary-footer-dot "
                                    "machine-summary-dot-halt"
                                )
                            ),
                            html.Strong(str(halt_stop)),
                            " Halt",
                        ],
                        className="machine-summary-halt",
                    ),
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


def operational_kpi_card(title, value, subtitle, color="green"):
    """
    KPI card for OEE / Performance / Quality / Availability.

    No demo/static percentage is created here. Pass None when the real source
    is unavailable and the card will display N/A with an empty progress bar.
    """
    is_available = value is not None

    if is_available:
        try:
            numeric_value = float(value)
            display_value = f"{numeric_value:.1f}%"
            progress = max(0.0, min(100.0, numeric_value))
        except (TypeError, ValueError):
            display_value = "N/A"
            progress = 0.0
            is_available = False
    else:
        display_value = "N/A"
        progress = 0.0

    return html.Div(
        className=(
            f"operational-kpi-card operational-kpi-{color}"
            + ("" if is_available else " operational-kpi-na")
        ),
        children=[
            html.Div(
                className="operational-kpi-header",
                children=[
                    html.Span(title, className="operational-kpi-title"),
                ],
            ),
            html.Div(display_value, className="operational-kpi-value"),
            html.Div(str(subtitle), className="operational-kpi-subtitle"),
            html.Div(
                className="operational-kpi-progress",
                children=[
                    html.Div(
                        className="operational-kpi-progress-fill",
                        style={"width": f"{progress}%"},
                    )
                ],
            ),
        ],
    )

# =========================================================
# PRODUCTION GROUP CARD
# =========================================================

def production_group_card(title, target, actual, unit="Tons"):
    """
    Target vs Actual production group card.

    Important:
    - None / NaN / infinity are displayed as N/A.
    - No fake zero is substituted for missing source data.
    - Bar width is drawn only when a valid numeric value exists.
    """

    def _valid_number(value):
        if value is None:
            return None

        try:
            number = float(value)
        except (TypeError, ValueError):
            return None

        if not math.isfinite(number):
            return None

        return max(number, 0.0)

    def _format_number(value):
        number = _valid_number(value)

        if number is None:
            return "N/A"

        if abs(number) >= 1000:
            return f"{number:,.0f}"

        if number.is_integer():
            return f"{number:,.0f}"

        return f"{number:,.1f}"

    target_num = _valid_number(target)
    actual_num = _valid_number(actual)

    scale_candidates = [
        value
        for value in (
            target_num,
            actual_num,
        )
        if value is not None and value > 0
    ]

    scale = (
        max(scale_candidates)
        if scale_candidates
        else 0.0
    )

    if target_num is not None and scale > 0:
        target_width = max(
            2.0,
            min(
                100.0,
                target_num / scale * 100,
            ),
        )
    else:
        target_width = 0.0

    if actual_num is not None and scale > 0:
        actual_width = max(
            2.0,
            min(
                100.0,
                actual_num / scale * 100,
            ),
        )
    else:
        actual_width = 0.0

    return html.Div(
        className="production-group-card",
        children=[
            html.Div(
                className="production-group-header",
                children=[
                    html.H3(title),
                    html.P(
                        f"Target vs actual, {unit}"
                    ),
                ],
            ),

            html.Div(
                className="production-group-bars",
                children=[
                    html.Div(
                        className="production-group-row",
                        children=[
                            html.Div(
                                "Target",
                                className="production-group-label",
                            ),

                            html.Div(
                                className="production-group-track",
                                children=[
                                    html.Div(
                                        className=(
                                            "production-group-fill "
                                            "production-group-target-fill"
                                        ),
                                        style={
                                            "width":
                                                f"{target_width:.2f}%"
                                        },
                                    ),
                                ],
                            ),

                            html.Div(
                                _format_number(target),
                                className="production-group-value",
                            ),
                        ],
                    ),

                    html.Div(
                        className="production-group-row",
                        children=[
                            html.Div(
                                "Actual",
                                className="production-group-label",
                            ),

                            html.Div(
                                className="production-group-track",
                                children=[
                                    html.Div(
                                        className=(
                                            "production-group-fill "
                                            "production-group-actual-fill"
                                        ),
                                        style={
                                            "width":
                                                f"{actual_width:.2f}%"
                                        },
                                    ),
                                ],
                            ),

                            html.Div(
                                _format_number(actual),
                                className="production-group-value",
                            ),
                        ],
                    ),
                ],
            ),
        ],
    )

