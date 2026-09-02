import pandas as pd
from dash import html, dcc
import plotly.graph_objects as go


COLORS = {
    "green": "#22c55e",
    "green_dark": "#16a34a",
    "red": "#f87171",
    "red_dark": "#ef4444",
    "orange": "#fbbf24",
    "orange_dark": "#f59e0b",
    "purple": "#a78bfa",
    "purple_dark": "#8b5cf6",
    "blue": "#60a5fa",
    "blue_dark": "#2563eb",
    "cyan": "#67e8f9",
    "gray": "#94a3b8",
    "grid": "#e8f0eb",
}


REASON_COLORS = [
    COLORS["red"],
    COLORS["orange"],
    COLORS["purple"],
    COLORS["green"],
    COLORS["blue"],
    COLORS["cyan"],
]


STATUS_COLORS = {
    "Running": COLORS["green"],
    "Idle": COLORS["orange"],
    "Halt / Stop": COLORS["purple"],
}


STATUS_ORDER = [
    "Running",
    "Idle",
    "Halt / Stop",
]


# =========================================================
# COMMON GRAPH WRAPPER
# =========================================================

def _graph(fig):
    return dcc.Graph(
        figure=fig,
        config={
            "displayModeBar": False,
            "responsive": True,
        },
        responsive=True,
        style={
            "width": "100%",
            "height": "100%",
        },
    )


# =========================================================
# EMPTY CHART
# =========================================================

def _empty_chart(message="No data available", height=280):
    fig = go.Figure()

    fig.add_annotation(
        text=message,
        x=0.5,
        y=0.5,
        xref="paper",
        yref="paper",
        showarrow=False,
        font=dict(
            size=14,
            color="#647080",
        ),
    )

    fig.update_layout(
        height=height,
        margin=dict(
            l=20,
            r=20,
            t=20,
            b=20,
        ),
        paper_bgcolor="rgba(0,0,0,0)",
        plot_bgcolor="rgba(0,0,0,0)",
        xaxis=dict(visible=False),
        yaxis=dict(visible=False),
    )

    return _graph(fig)


# =========================================================
# MACHINE STATUS TREND
# =========================================================

def status_trend_line(df):
    if df is None or df.empty:
        return _empty_chart(height=310)

    df = df.copy()

    if "Date" not in df.columns:
        return _empty_chart(height=310)

    if len(df) > 7:
        df = df.tail(7).copy()

    for col in STATUS_ORDER:
        if col not in df.columns:
            df[col] = 0

    fig = go.Figure()

    for status in STATUS_ORDER:
        fig.add_trace(
            go.Scatter(
                x=df["Date"],
                y=df[status],
                mode="lines+markers+text",
                name=status,
                line=dict(
                    color=STATUS_COLORS.get(
                        status,
                        COLORS["gray"],
                    ),
                    width=3,
                    shape="spline",
                ),
                marker=dict(
                    size=7,
                    color=STATUS_COLORS.get(
                        status,
                        COLORS["gray"],
                    ),
                    line=dict(
                        color="#ffffff",
                        width=2,
                    ),
                ),
                text=df[status].round(1).astype(str) + "%",
                textposition="top center",
                textfont=dict(
                    size=9,
                    color="#0f172a",
                ),
                hovertemplate=(
                    "<b>%{x}</b><br>"
                    + status
                    + ": %{y:.1f}%<extra></extra>"
                ),
            )
        )

    fig.update_layout(
        height=310,
        margin=dict(
            l=40,
            r=20,
            t=54,
            b=42,
        ),
        paper_bgcolor="rgba(0,0,0,0)",
        plot_bgcolor="rgba(0,0,0,0)",
        hovermode="x unified",

        legend=dict(
            orientation="h",
            yanchor="bottom",
            y=1.08,
            xanchor="center",
            x=0.5,
            font=dict(size=10),
        ),

        xaxis=dict(
            showgrid=False,
            tickfont=dict(size=10),
            automargin=True,
        ),

        yaxis=dict(
            title=dict(
                text="Percentage",
                font=dict(size=10),
            ),
            range=[0, 100],
            ticksuffix="%",
            showgrid=True,
            gridcolor=COLORS["grid"],
            zeroline=False,
            tickfont=dict(size=10),
            automargin=True,
        ),

        font=dict(
            family="Inter, Arial",
            size=11,
            color="#172033",
        ),
    )

    return _graph(fig)


# =========================================================
# DOWNTIME REASONS DONUT
# =========================================================

def reason_donut(df):
    if df is None or df.empty:
        return _empty_chart(height=310)

    df = df.copy()

    if "Reason" not in df.columns:
        return _empty_chart(height=310)

    if "Minutes" not in df.columns:
        if "Duration Minutes" in df.columns:
            df["Minutes"] = df["Duration Minutes"]

        elif "Count" in df.columns:
            df["Minutes"] = df["Count"]

        else:
            df["Minutes"] = 0

    total_minutes = int(
        df["Minutes"].fillna(0).sum()
    )

    if total_minutes == 0:
        return _empty_chart(height=310)

    days, remainder = divmod(total_minutes, 1440)
    hours, mins = divmod(remainder, 60)
    center_parts = []
    if days:
        center_parts.append(f"{days}d")
    if hours or days:
        center_parts.append(f"{hours}h")
    center_parts.append(f"{mins}m")
    center_label = " ".join(center_parts)

    fig = go.Figure(
        data=[
            go.Pie(
                labels=df["Reason"],
                values=df["Minutes"],
                hole=0.58,
                sort=False,
                direction="clockwise",
                textinfo="none",

                marker=dict(
                    colors=REASON_COLORS,
                    line=dict(
                        color="#ffffff",
                        width=2,
                    ),
                ),

                hovertemplate=(
                    "<b>%{label}</b><br>"
                    "Minutes: %{value}"
                    "<extra></extra>"
                ),
            )
        ]
    )

    fig.add_annotation(
        text=(
            f"<b>{center_label}</b>"
            "<br>"
            "<span style='font-size:8px'>"
            "Total Downtime"
            "</span>"
        ),
        x=0.5,
        y=0.5,
        showarrow=False,

        font=dict(
            size=12,
            color="#0f172a",
        ),
    )

    fig.update_layout(
        height=208,

        margin=dict(
            l=10,
            r=10,
            t=8,
            b=8,
        ),

        paper_bgcolor="rgba(0,0,0,0)",
        showlegend=False,

        uniformtext=dict(
            mode="hide",
            minsize=9,
        ),

        font=dict(
            family="Inter, Arial",
            size=11,
            color="#172033",
        ),
    )

    return _graph(fig)


# =========================================================
# PRODUCTION OVERVIEW
# Prototype:
# - Light blue target bars
# - Green actual bars
# - Date-wise bars
# - Compact legend
# =========================================================

def production_overview_chart(df):
    if df is None or df.empty:
        return _empty_chart("Production tonnage unavailable", height=150)

    required = {"Date", "Target", "Actual"}
    if not required.issubset(set(df.columns)):
        return _empty_chart("Production tonnage unavailable", height=150)

    df = df.copy()
    if len(df) > 7:
        df = df.tail(7).copy()

    # Keep missing DB values as missing. Do not convert an unavailable target
    # into a fake zero bar.
    import pandas as pd
    df["Target"] = pd.to_numeric(df["Target"], errors="coerce")
    df["Actual"] = pd.to_numeric(df["Actual"], errors="coerce")

    has_target = df["Target"].notna().any()
    has_actual = df["Actual"].notna().any()
    if not has_target and not has_actual:
        return _empty_chart("Production tonnage unavailable", height=150)

    fig = go.Figure()

    if has_target:
        fig.add_trace(
            go.Bar(
                x=df["Date"],
                y=df["Target"],
                name="Target",
                marker=dict(
                    color="#d9eaff",
                    line=dict(color="#c9def8", width=0.5),
                ),
                width=0.42,
                hovertemplate=(
                    "<b>%{x}</b><br>"
                    "Target: %{y:.2f} Tons"
                    "<extra></extra>"
                ),
            )
        )

    if has_actual:
        fig.add_trace(
            go.Bar(
                x=df["Date"],
                y=df["Actual"],
                name="Actual",
                marker=dict(
                    color="#16a34a",
                    line=dict(color="#0f8f3d", width=0.5),
                ),
                width=0.26,
                hovertemplate=(
                    "<b>%{x}</b><br>"
                    "Actual: %{y:.2f} Tons"
                    "<extra></extra>"
                ),
            )
        )

    numeric_values = []
    if has_target:
        numeric_values.extend(df["Target"].dropna().tolist())
    if has_actual:
        numeric_values.extend(df["Actual"].dropna().tolist())
    max_value = max(numeric_values) if numeric_values else 0

    fig.update_layout(
        height=150,
        margin=dict(l=34, r=8, t=28, b=28),
        barmode="overlay",
        bargap=0.34,
        paper_bgcolor="rgba(0,0,0,0)",
        plot_bgcolor="rgba(0,0,0,0)",
        legend=dict(
            orientation="h",
            yanchor="bottom",
            y=1.02,
            xanchor="center",
            x=0.5,
            font=dict(size=8),
        ),
        xaxis=dict(showgrid=False, tickfont=dict(size=8), automargin=True),
        yaxis=dict(
            title=None,
            rangemode="tozero",
            range=[0, max_value * 1.2] if max_value > 0 else None,
            showgrid=True,
            gridcolor=COLORS["grid"],
            zeroline=False,
            tickfont=dict(size=8),
            automargin=True,
        ),
        font=dict(family="Inter, Arial", size=9, color="#172033"),
    )

    return _graph(fig)


def trend_line(df, chart_type="downtime"):
    if df is None or df.empty:
        return _empty_chart(height=280)

    df = df.copy()

    if (
        "Date" not in df.columns
        or "Hours" not in df.columns
    ):
        return _empty_chart(height=280)

    if chart_type == "downtime":
        color = COLORS["red_dark"]
        fill = "rgba(248, 113, 113, 0.18)"

    else:
        color = COLORS["orange_dark"]
        fill = "rgba(251, 191, 36, 0.20)"

    max_y = df["Hours"].fillna(0).max()
    max_y = max(
        1,
        max_y * 1.35,
    )

    fig = go.Figure()

    fig.add_trace(
        go.Scatter(
            x=df["Date"],
            y=df["Hours"],

            mode="lines+markers+text",

            line=dict(
                color=color,
                width=3,
                shape="spline",
            ),

            marker=dict(
                size=7,
                color=color,
                line=dict(
                    color="#ffffff",
                    width=2,
                ),
            ),

            text=(
                df["Hours"]
                .round(1)
                .astype(str)
                + "h"
            ),

            textposition="top center",

            textfont=dict(
                size=9,
                color="#0f172a",
            ),

            fill="tozeroy",
            fillcolor=fill,

            hovertemplate=(
                "<b>%{x}</b><br>"
                "Hours: %{y:.1f}h"
                "<extra></extra>"
            ),

            name="Hours",
        )
    )

    fig.update_layout(
        height=280,

        margin=dict(
            l=42,
            r=18,
            t=18,
            b=36,
        ),

        paper_bgcolor="rgba(0,0,0,0)",
        plot_bgcolor="rgba(0,0,0,0)",

        showlegend=False,

        xaxis=dict(
            showgrid=False,
            tickfont=dict(size=9),
        ),

        yaxis=dict(
            title=dict(
                text="Hours",
                font=dict(size=10),
            ),

            range=[
                0,
                max_y,
            ],

            showgrid=True,
            gridcolor=COLORS["grid"],

            zeroline=False,

            tickfont=dict(size=9),
        ),

        font=dict(
            family="Inter, Arial",
            size=11,
            color="#172033",
        ),
    )

    return _graph(fig)


# =========================================================
# PLANNED VS ACTUAL
# =========================================================

def planned_vs_actual_chart(df):
    if df is None or df.empty:
        return _empty_chart(height=280)

    required_cols = [
        "Month",
        "Target",
        "Actual",
    ]

    for col in required_cols:
        if col not in df.columns:
            return _empty_chart(height=280)

    df = df.copy()

    if "Achievement %" not in df.columns:
        df["Achievement %"] = df.apply(
            lambda row: (
                round(
                    (
                        row["Actual"]
                        / row["Target"]
                    )
                    * 100,
                    1,
                )
                if row["Target"] > 0
                else 0
            ),
            axis=1,
        )

    fig = go.Figure()

    # TARGET
    fig.add_trace(
        go.Bar(
            x=df["Month"],
            y=df["Target"],

            name="Target",

            marker_color="#cbd5e1",

            text=(
                df["Target"]
                .round(0)
                .astype(int)
                .astype(str)
            ),

            textposition="outside",

            textfont=dict(
                size=9,
                color="#334155",
            ),

            hovertemplate=(
                "<b>%{x}</b><br>"
                "Target: %{y:.1f} Tons"
                "<extra></extra>"
            ),
        )
    )

    # ACTUAL
    fig.add_trace(
        go.Bar(
            x=df["Month"],
            y=df["Actual"],

            name="Actual",

            marker_color=COLORS["green"],

            text=(
                df["Actual"]
                .round(0)
                .astype(int)
                .astype(str)
            ),

            textposition="outside",

            textfont=dict(
                size=9,
                color="#008a22",
            ),

            hovertemplate=(
                "<b>%{x}</b><br>"
                "Actual: %{y:.1f} Tons"
                "<extra></extra>"
            ),
        )
    )

    # ACHIEVEMENT
    fig.add_trace(
        go.Scatter(
            x=df["Month"],
            y=df["Achievement %"],

            name="Achievement %",

            mode="lines+markers+text",

            yaxis="y2",

            line=dict(
                color=COLORS["orange_dark"],
                width=3,
                shape="spline",
            ),

            marker=dict(
                size=7,
                color=COLORS["orange_dark"],

                line=dict(
                    color="#ffffff",
                    width=2,
                ),
            ),

            text=(
                df["Achievement %"]
                .round(0)
                .astype(int)
                .astype(str)
                + "%"
            ),

            textposition="top center",

            textfont=dict(
                size=9,
                color="#0f172a",
            ),

            hovertemplate=(
                "<b>%{x}</b><br>"
                "Achievement: %{y:.1f}%"
                "<extra></extra>"
            ),
        )
    )

    max_y = max(
        (
            float(df["Target"].max())
            if not df["Target"].empty
            else 0
        ),
        (
            float(df["Actual"].max())
            if not df["Actual"].empty
            else 0
        ),
    )

    max_achievement = (
        float(df["Achievement %"].max())
        if not df["Achievement %"].empty
        else 100
    )

    min_achievement = (
        float(df["Achievement %"].min())
        if not df["Achievement %"].empty
        else 0
    )

    y2_min = max(
        0,
        min_achievement - 10,
    )

    y2_max = max(
        120,
        max_achievement + 10,
    )

    fig.update_layout(
        height=280,

        barmode="group",

        margin=dict(
            l=45,
            r=45,
            t=42,
            b=36,
        ),

        paper_bgcolor="rgba(0,0,0,0)",
        plot_bgcolor="rgba(0,0,0,0)",

        legend=dict(
            orientation="h",

            yanchor="bottom",
            y=1.06,

            xanchor="center",
            x=0.5,

            font=dict(size=10),
        ),

        xaxis=dict(
            showgrid=False,
            tickfont=dict(size=9),
        ),

        yaxis=dict(
            title=dict(
                text="Tons",
                font=dict(size=10),
            ),

            range=[
                0,
                (
                    max_y * 1.25
                    if max_y > 0
                    else 300
                ),
            ],

            showgrid=True,
            gridcolor=COLORS["grid"],

            zeroline=False,

            tickfont=dict(size=9),
        ),

        yaxis2=dict(
            title=dict(
                text="",
                font=dict(size=10),
            ),

            overlaying="y",
            side="right",

            range=[
                y2_min,
                y2_max,
            ],

            ticksuffix="%",

            showgrid=False,
            zeroline=False,

            tickfont=dict(size=9),
        ),

        font=dict(
            family="Inter, Arial",
            size=11,
            color="#172033",
        ),

        uniformtext=dict(
            mode="hide",
            minsize=8,
        ),
    )

    return _graph(fig)


# =========================================================
# MACHINE-WISE EFFICIENCY
# =========================================================

def machine_efficiency_chart(df):
    """
    Machine-wise Efficiency chart.

    DB/calculation logic stays unchanged upstream:
        SUM(MAX(actual_executed_qty) per job)
        / SUM(total_qty)
        * 100

    UI:
        >= 80%       Green
        60-79.99%    Blue
        40-59.99%    Yellow/Orange
        < 40%         Red

    Exactly 10 machine slots are visible at one time.
    Remaining machines are available through horizontal scroll.
    """

    if df is None or df.empty:
        return _empty_chart(
            message="Machine efficiency unavailable",
            height=285,
        )

    required = {
        "Machine Name",
        "Efficiency %",
    }

    if not required.issubset(df.columns):
        return _empty_chart(
            message="Machine efficiency unavailable",
            height=285,
        )

    chart_df = df.copy()

    chart_df["Machine Name"] = (
        chart_df["Machine Name"]
        .fillna("")
        .astype(str)
        .str.strip()
    )

    chart_df["Efficiency %"] = pd.to_numeric(
        chart_df["Efficiency %"],
        errors="coerce",
    )

    chart_df = (
        chart_df[
            chart_df["Machine Name"].ne("")
        ]
        .dropna(subset=["Efficiency %"])
        .copy()
    )

    if chart_df.empty:
        return _empty_chart(
            message="Machine efficiency unavailable",
            height=285,
        )

    def wrap_machine_name(name):
        """
        Wrap long machine names into at most two horizontal lines.

        The split favors natural separators so labels stay readable while
        remaining compact in the fixed machine slot.
        """

        name = str(name).strip()

        if not name:
            return name

        if len(name) <= 12:
            return name

        tokens = [
            token for token in name.replace("/", " ").split()
            if token
        ]

        if len(tokens) >= 2:
            head = " ".join(tokens[:-1]).strip()
            tail = tokens[-1].strip()

            if head and tail:
                return f"{head}<br>{tail}"

        hyphen_parts = [
            part for part in name.split("-")
            if part
        ]

        if len(hyphen_parts) >= 2:
            pivot = max(1, len(hyphen_parts) // 2)
            first_line = "-".join(hyphen_parts[:pivot]).strip("-")
            second_line = "-".join(hyphen_parts[pivot:]).strip("-")

            if first_line and second_line:
                return f"{first_line}<br>{second_line}"

        pivot = max(4, min(len(name) - 4, len(name) // 2))

        return f"{name[:pivot].rstrip()}<br>{name[pivot:].lstrip()}"

    # Highest efficiency first.
    chart_df = (
        chart_df
        .sort_values(
            by=[
                "Efficiency %",
                "Machine Name",
            ],
            ascending=[
                False,
                True,
            ],
        )
        .reset_index(drop=True)
    )

    chart_df["Machine Label"] = (
        chart_df["Machine Name"]
        .map(wrap_machine_name)
    )

    def get_bar_color(value):
        value = float(value)

        if value >= 80:
            return "#22A06B"

        if value >= 60:
            return "#3F84C5"

        if value >= 40:
            return "#F3A936"

        return "#EF5350"

    bar_colors = [
        get_bar_color(value)
        for value in chart_df["Efficiency %"]
    ]

    max_efficiency = float(
        chart_df["Efficiency %"].max()
    )

    y_max = max(
        110.0,
        max_efficiency + 18.0,
    )

    text_values = [
        f"{float(value):.1f}%"
        for value in chart_df["Efficiency %"]
    ]

    fig = go.Figure()

    fig.add_trace(
        go.Bar(
            x=chart_df["Machine Name"],
            y=chart_df["Efficiency %"],

            width=0.46,

            marker=dict(
                color=bar_colors,
                line=dict(width=0),
            ),

            cliponaxis=False,

            customdata=chart_df["Machine Name"],

            hovertemplate=(
                "<b>%{customdata}</b><br>"
                "Efficiency: %{y:.2f}%"
                "<extra></extra>"
            ),
        )
    )

    fig.add_trace(
        go.Scatter(
            x=chart_df["Machine Name"],
            y=chart_df["Efficiency %"] + 3.5,
            mode="text",
            text=text_values,
            textposition="middle center",
            textfont=dict(
                size=10,
                color="#0F172A",
            ),
            hoverinfo="skip",
            showlegend=False,
            cliponaxis=False,
        )
    )

    fig.update_layout(
        height=285,
        autosize=True,

        margin=dict(
            l=34,
            r=18,
            t=30,
            b=76,
        ),

        paper_bgcolor="rgba(0,0,0,0)",
        plot_bgcolor="rgba(0,0,0,0)",

        showlegend=False,

        bargap=0.28,
        barcornerradius=7,

        font=dict(
            family="Inter, Arial, sans-serif",
            color="#172033",
        ),

        xaxis=dict(
            title=None,
            showgrid=False,
            zeroline=False,
            type="category",
            tickmode="array",

            tickangle=0,

            tickfont=dict(
                size=9,
                color="#6F7F78",
            ),

            automargin=True,
            fixedrange=True,

            categoryorder="array",
            categoryarray=(
                chart_df["Machine Name"]
                .tolist()
            ),
            tickvals=chart_df["Machine Name"].tolist(),
            ticktext=chart_df["Machine Label"].tolist(),
        ),

        yaxis=dict(
            title=None,

            range=[
                -5,
                y_max,
            ],

            showgrid=True,
            gridcolor="#E7EEEA",
            gridwidth=1,

            zeroline=False,

            showticklabels=True,
            tickvals=[0, 25, 50, 75, 100],
            ticktext=[
                "0%",
                "25%",
                "50%",
                "75%",
                "100%",
            ],

            tickfont=dict(
                size=9,
                color="#66756E",
            ),

            fixedrange=True,
        ),
    )

    machine_count = len(chart_df)

    visible_count = 12
    graph_width_pct = 100 if machine_count <= visible_count else round(
        (machine_count / visible_count) * 100,
        2,
    )

    graph = dcc.Graph(
        figure=fig,

        config={
            "displayModeBar": False,
            "responsive": True,
        },

        responsive=True,

        style={
            "width": "100%",
            "minWidth": "100%",
            "maxWidth": "100%",
            "height": "285px",
            "flexShrink": "0",
        },
    )

    return html.Div(
        className=(
            "machine-efficiency-scroll"
            + (
                " machine-efficiency-scroll-active"
                if machine_count > visible_count
                else ""
            )
        ),
        style={
            "--machine-efficiency-width": f"{graph_width_pct}%",
        },
        children=[graph],
    )
