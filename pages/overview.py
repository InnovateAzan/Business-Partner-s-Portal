from dash import html

from components.cards import (
    machine_summary_card,
    kpi_card,
    production_kpi_card,
)

from components.charts import (
    status_trend_line,
    reason_donut,
    planned_vs_actual_chart,
    production_overview_chart,
)

from components.tables import (
    data_table,
    card_header,
    stopped_machines_card_header,
    stopped_machines_table,
)

from services.mock_data import (
    get_kpis,
    downtime_reasons,
    stopped_machine_detail,
    idle_machine_detail,
    department_summary,
    machine_status_trend,
    last_24h_production,
    production_daily_trend,
)


# =========================================================
# NUMBER FORMATTER
# =========================================================

def _format_number(value):
    try:
        value = float(value)
    except Exception:
        value = 0

    return f"{value:,.2f}"


# =========================================================
# =========================================================
# DOWNTIME REASONS LEGEND
# =========================================================

def reason_legend(reasons_df):
    if reasons_df is None or reasons_df.empty:
        return html.Div(
            "No downtime reasons available",
            className="reason-legend-empty",
        )

    colors = [
        "#f87171",
        "#f59e0b",
        "#a78bfa",
        "#22c55e",
        "#60a5fa",
        "#67e8f9",
    ]

    total = max(
        float(
            reasons_df["Minutes"]
            .fillna(0)
            .sum()
        ),
        1,
    )

    items = []

    for idx, row in enumerate(
        reasons_df.head(6).itertuples(
            index=False
        )
    ):
        pct = round(
            (
                float(row.Minutes or 0)
                / total
            )
            * 100,
            1,
        )

        items.append(
            html.Div(
                className="reason-legend-item",
                children=[
                    html.Span(
                        className="reason-legend-dot",
                        style={
                            "backgroundColor":
                                colors[
                                    idx
                                    % len(colors)
                                ]
                        },
                    ),

                    html.Span(
                        str(row.Reason),
                        className="reason-legend-label",
                    ),

                    html.Span(
                        f"{pct}%",
                        className="reason-legend-value",
                    ),
                ],
            )
        )

    return html.Div(
        items,
        className="reason-legend-list",
    )


# =========================================================
# PRODUCTION OVERVIEW CARD
# Prototype:
#
# Production Overview
# Current Performance
#
# 8.22          2.00          24.3%
# Target        Actual        Achievement
#
# Target / Actual legend
#
# Daily bars
# =========================================================

def production_overview_card(production_daily):
    target = 0
    actual = 0
    achievement_pct = 0

    if (
        production_daily is not None
        and not production_daily.empty
    ):
        target = float(
            production_daily["Target"]
            .fillna(0)
            .sum()
        )

        actual = float(
            production_daily["Actual"]
            .fillna(0)
            .sum()
        )

        if target > 0:
            achievement_pct = round(
                (actual / target) * 100,
                1,
            )

    return html.Div(
        className=(
            "card overview-chart-card "
            "production-overview-card"
        ),
        children=[

            # ==============================
            # HEADER
            # ==============================
            html.Div(
                className="production-overview-header",
                children=[
                    html.H3(
                        "Production Overview",
                        className="card-title",
                    ),

                    html.P(
                        "Current Performance",
                        className="production-overview-subtitle",
                    ),
                ],
            ),

            # ==============================
            # INLINE METRICS
            # ==============================
            html.Div(
                className="production-overview-top",
                children=[

                    # TARGET
                    html.Div(
                        className=(
                            "production-overview-metric "
                            "production-target-metric"
                        ),
                        children=[
                            html.Strong(
                                f"{target:,.2f}"
                            ),
                            html.Span(
                                "Target (Tons)"
                            ),
                        ],
                    ),

                    html.Div(
                        className="production-metric-divider"
                    ),

                    # ACTUAL
                    html.Div(
                        className=(
                            "production-overview-metric "
                            "production-actual-metric"
                        ),
                        children=[
                            html.Strong(
                                f"{actual:,.2f}"
                            ),
                            html.Span(
                                "Actual (Tons)"
                            ),
                        ],
                    ),

                    html.Div(
                        className="production-metric-divider"
                    ),

                    # ACHIEVEMENT
                    html.Div(
                        className=(
                            "production-overview-metric "
                            "production-achievement-metric"
                        ),
                        children=[
                            html.Strong(
                                f"{achievement_pct:.1f}%"
                            ),
                            html.Span(
                                "Achievement"
                            ),
                        ],
                    ),
                ],
            ),

            # Chart legend is now handled
            # inside production_overview_chart()
            html.Div(
                className="production-overview-chart-area",
                children=[
                    production_overview_chart(
                        production_daily
                    )
                ],
            ),
        ],
    )


# =========================================================
# MAIN OVERVIEW PAGE
# =========================================================

def overview_page(filters):
    kpis = get_kpis(filters)

    production = last_24h_production(
        filters
    )

    production_daily = production_daily_trend(
        filters
    )

    reasons = downtime_reasons(
        filters
    )

    status_trend = machine_status_trend(
        filters
    )

    stopped_detail = stopped_machine_detail(
        filters
    )

    idle_detail = idle_machine_detail(
        filters
    )

    dept_summary = department_summary(
        filters
    )

    return html.Div(
        className=(
            "dashboard-page "
            "overview-dashboard"
        ),
        children=[

            # =================================================
            # ROW 1 - 7 KPI CARDS
            # =================================================
            html.Div(
                className=(
                    "kpi-row "
                    "kpi-row-seven"
                ),
                children=[

                    # MACHINE SUMMARY
                    machine_summary_card(
                        kpis
                    ),

                    # RUNNING
                    kpi_card(
                        "Running Machines",
                        kpis.get(
                            "running",
                            0,
                        ),
                        (
                            f'{kpis.get("running_pct", 0)}%'
                            " of Total"
                        ),
                        "▲",
                        "green",
                        kpis.get(
                            "running_pct",
                            0,
                        ),
                    ),

                    # IDLE
                    kpi_card(
                        "Idle Machines",
                        kpis.get(
                            "idle",
                            0,
                        ),
                        (
                            f'{kpis.get("idle_pct", 0)}%'
                            " of Total"
                        ),
                        "◷",
                        "orange",
                        kpis.get(
                            "idle_pct",
                            0,
                        ),
                    ),

                    # HALT / STOP
                    kpi_card(
                        "Halt / Stop",
                        kpis.get(
                            "halt_stop",
                            0,
                        ),
                        (
                            f'{kpis.get("halt_stop_pct", 0)}%'
                            " of Total"
                        ),
                        "Ⅱ",
                        "purple",
                        kpis.get(
                            "halt_stop_pct",
                            0,
                        ),
                    ),

                    # STOPPED
                    kpi_card(
                        "Stopped Machines",
                        kpis.get(
                            "stopped",
                            0,
                        ),
                        (
                            f'{kpis.get("stopped_pct", 0)}%'
                            " of Total"
                        ),
                        "■",
                        "red",
                        kpis.get(
                            "stopped_pct",
                            0,
                        ),
                    ),

                    # OEE
                    kpi_card(
                        "Overall OEE",
                        f'{kpis.get("oee", 0)}%',
                        "↑ 2.4% vs Last 7 Days",
                        "◔",
                        "blue",
                        kpis.get(
                            "oee",
                            0,
                        ),
                    ),

                    # PRODUCTION
                    production_kpi_card(
                        "Last 24 Hours Production",
                        f"{_format_number(production.get('actual', 0))} Tons",
                        f"Target: {_format_number(production.get('target', 0))} Tons",
                        (
                            f"{production.get('achievement_pct', 0)}% "
                            f"({production.get('gap_label', 'Below Target')})"
                        ),
                        production.get("achievement_pct", 0),
                    ),
                ],
            ),

            # =================================================
            # ROW 2 - 3 MAIN CHARTS
            # =================================================
            html.Div(
                className=(
                    "overview-main-chart-row "
                    "overview-main-chart-row-three"
                ),
                children=[

                    # MACHINE STATUS TREND
                    html.Div(
                        className=(
                            "card "
                            "overview-chart-card "
                            "wide"
                        ),
                        children=[
                            card_header(
                                "Machine Status Trend (%)"
                            ),

                            status_trend_line(
                                status_trend
                            ),
                        ],
                    ),

                    # DOWNTIME REASONS
                    html.Div(
                        className=(
                            "card "
                            "overview-chart-card "
                            "reason-card-wrapper"
                        ),
                        children=[
                            card_header(
                                "Stopped Machines - "
                                "Downtime Reasons"
                            ),

                            html.Div(
                                className="reason-donut-layout",
                                children=[

                                    html.Div(
                                        className="reason-donut-plot",
                                        children=[
                                            reason_donut(
                                                reasons
                                            ),
                                        ],
                                    ),

                                    reason_legend(
                                        reasons
                                    ),
                                ],
                            ),
                        ],
                    ),

                    # PRODUCTION OVERVIEW
                    production_overview_card(
                        production_daily
                    ),
                ],
            ),

            # =================================================
            # ROW 3 - LOWER DASHBOARD
            # =================================================
            html.Div(
                className=(
                    "overview-trend-row "
                    "overview-trend-row-three"
                ),
                children=[

                    # STOPPED MACHINES
                    html.Div(
                        className=(
                            "card "
                            "overview-trend-card"
                        ),
                        children=[
                            stopped_machines_card_header(
                                "Stopped Machines",
                                "Real-time stopped machine detail",
                                "View All",
                                "#",
                            ),

                            stopped_machines_table(
                                stopped_detail,
                                max_rows=5,
                            ),
                        ],
                    ),

                    # IDLE MACHINES
                    html.Div(
                        className=(
                            "card "
                            "overview-trend-card"
                        ),
                        children=[
                            card_header(
                                "Idle Machines"
                            ),

                            data_table(
                                idle_detail,
                                max_rows=5,
                            ),
                        ],
                    ),

                    # DEPARTMENT SUMMARY
                    html.Div(
                        className=(
                            "card "
                            "overview-trend-card"
                        ),
                        children=[
                            card_header(
                                "Department Summary"
                            ),

                            data_table(
                                dept_summary,
                                max_rows=6,
                            ),
                        ],
                    ),
                ],
            ),

        ],
    )
