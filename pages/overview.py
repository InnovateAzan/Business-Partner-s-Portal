from dash import html

from components.cards import (
    machine_summary_card,
    kpi_card,
    operational_kpi_card,
    production_group_card,
)

from components.charts import (
    status_trend_line,
    reason_donut,
    planned_vs_actual_chart,
    production_overview_chart,
    machine_efficiency_chart,
)

from components.tables import (
    data_table,
    card_header,
    idle_machines_card_header,
    idle_machines_table,
    halt_stop_machines_card_header,
    halt_stop_machines_table,
)

from services.mock_data import (
    get_kpis,
    downtime_reasons,
    idle_machine_detail,
    halt_stop_machine_detail,
    machine_wise_efficiency,
    machine_status_trend,
    last_24h_production,
    production_daily_trend,
    get_operational_kpis,
    production_group_summary,
)


# =========================================================
# NUMBER FORMATTER
# =========================================================

def _format_number(value):
    if value is None:
        return "N/A"
    try:
        value = float(value)
    except Exception:
        return "N/A"
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
#
# Target / Actual legend
#
# Daily bars
# =========================================================

def production_overview_card(production_daily):
    target = None
    actual = None
    achievement_pct = None

    if production_daily is not None and not production_daily.empty:
        target_sum = production_daily["Target"].sum(min_count=1)
        actual_sum = production_daily["Actual"].sum(min_count=1)
        target = None if target_sum != target_sum else float(target_sum)
        actual = None if actual_sum != actual_sum else float(actual_sum)

        if target is not None and actual is not None and target > 0:
            achievement_pct = round((actual / target) * 100, 1)

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
                        "Combined production target vs actual",
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
                                _format_number(target)
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
                                _format_number(actual)
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
                                "N/A" if achievement_pct is None else f"{achievement_pct:.1f}%"
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
    operational_kpis = get_operational_kpis(filters)

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

    idle_detail = idle_machine_detail(
        filters
    )

    halt_stop_detail = halt_stop_machine_detail(
        filters
    )

    efficiency_df = machine_wise_efficiency(
        filters
    )

    production_groups = production_group_summary(
        filters
    )

    production_group_map = {}
    if production_groups is not None and not production_groups.empty:
        production_group_map = {
            str(row["Group"]): {
                "target": row.get("Target"),
                "actual": row.get("Actual"),
                "unit": row.get("Unit") or "Tons",
            }
            for _, row in production_groups.iterrows()
        }

    return html.Div(
        className=(
            "dashboard-page "
            "overview-dashboard"
        ),
        children=[

            # =================================================
            # ROW 1 - ALL 8 KPI CARDS IN ONE ROW
            # =================================================
            html.Div(
                className="dashboard-kpi-row-eight",
                children=[

                    machine_summary_card(
                        kpis
                    ),

                    kpi_card(
                        "Running Machines",
                        kpis.get("running", 0),
                        f'{kpis.get("running_pct", 0)}% of Total',
                        "▲",
                        "green",
                        kpis.get("running_pct", 0),
                    ),

                    kpi_card(
                        "Idle Machines",
                        kpis.get("idle", 0),
                        f'{kpis.get("idle_pct", 0)}% of Total',
                        "◷",
                        "orange",
                        kpis.get("idle_pct", 0),
                    ),

                    kpi_card(
                        "Halt / Stop",
                        kpis.get("halt_stop", 0),
                        f'{kpis.get("halt_stop_pct", 0)}% of Total',
                        "Ⅱ",
                        "purple",
                        kpis.get("halt_stop_pct", 0),
                    ),

                    operational_kpi_card(
                        "OEE",
                        operational_kpis.get("oee"),
                        "Availability × Performance × Quality",
                        "oee",
                    ),

                    operational_kpi_card(
                        "Performance",
                        operational_kpis.get("performance"),
                        "Actual production vs planned production",
                        "performance",
                    ),

                    operational_kpi_card(
                        "Quality",
                        operational_kpis.get("quality"),
                        "Good production vs total production",
                        "quality",
                    ),

                    operational_kpi_card(
                        "Availability",
                        operational_kpis.get("availability"),
                        "Runtime vs Halt / Stop downtime",
                        "availability",
                    ),
                ],
            ),

            # =================================================
            # ROW 3 - 3 MAIN CHARTS
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
                                "Halt / Stop - "
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
            # ROW 4 - MACHINE DETAIL TABLES
            # =================================================
            html.Div(
                className="overview-machine-detail-row",
                children=[
                    # IDLE MACHINES
                    html.Div(
                        className="card machine-detail-card idle-detail-card",
                        children=[
                            idle_machines_card_header(
                                "Idle Machines",
                                "Machines available with no active running job",
                            ),
                            idle_machines_table(
                                idle_detail,
                                max_rows=None,
                            ),
                        ],
                    ),

                    # HALT / STOP MACHINES
                    html.Div(
                        className="card machine-detail-card halt-stop-detail-card",
                        children=[
                            halt_stop_machines_card_header(
                                "Halt / Stop Machines",
                                "Machines with temporarily interrupted active jobs",
                            ),
                            halt_stop_machines_table(
                                halt_stop_detail,
                                max_rows=None,
                            ),
                        ],
                    ),
                ],
            ),

            # =================================================
            # ROW 5 - FULL-WIDTH MACHINE-WISE EFFICIENCY
            # =================================================
            html.Div(
                className="card machine-efficiency-card",
                children=[
                    html.Div(
                        className="machine-efficiency-header",
                        children=[
                            html.H3(
                                "Machine-wise Efficiency",
                                className="card-title",
                            ),
                            html.P(
                                "Actual production vs planned quantity by machine",
                                className="machine-efficiency-subtitle",
                            ),
                        ],
                    ),
                    html.Div(
                        className="machine-efficiency-chart-area",
                        children=[
                            machine_efficiency_chart(efficiency_df)
                        ],
                    ),
                ],
            ),

            # =================================================
            # ROW 6 - PRODUCTION GROUP TARGET VS ACTUAL
            # =================================================
            html.Div(
                className="production-group-row-cards",
                children=[
                    production_group_card(
                        "Extruders",
                        production_group_map.get(
                            "Extruders",
                            {},
                        ).get("target"),
                        production_group_map.get(
                            "Extruders",
                            {},
                        ).get("actual"),
                        production_group_map.get(
                            "Extruders",
                            {},
                        ).get("unit", "Tons"),
                    ),

                    production_group_card(
                        "Bunchers & Braiders",
                        production_group_map.get(
                            "Bunchers & Braiders",
                            {},
                        ).get("target"),
                        production_group_map.get(
                            "Bunchers & Braiders",
                            {},
                        ).get("actual"),
                        production_group_map.get(
                            "Bunchers & Braiders",
                            {},
                        ).get("unit", "Tons"),
                    ),

                    production_group_card(
                        "Other Lines",
                        production_group_map.get(
                            "Other Lines",
                            {},
                        ).get("target"),
                        production_group_map.get(
                            "Other Lines",
                            {},
                        ).get("actual"),
                        production_group_map.get(
                            "Other Lines",
                            {},
                        ).get("unit", "Tons"),
                    ),
                ],
            ),

        ],
    )
