import pandas as pd

from services.api_service import fetch_dashboard_metadata, clear_api_cache


YEARLY_PRODUCTION_TARGET_TONS = 3000
YEARLY_PRODUCTION_TARGET_KM = 1200

ALLOWED_STATUSES = ["Running", "Idle", "Stopped", "Halt / Stop"]

# CableFlow event status mapping.
# 0/100 are terminal/setup-complete style events; they are treated as Stopped
# until the next machine event changes the state.
STATUS_CODE_MAP = {
    0: "Stopped",
    1: "Running",
    10: "Running",
    20: "Running",
    30: "Halt / Stop",
    40: "Idle",
    50: "Halt / Stop",
    60: "Halt / Stop",
    70: "Stopped",
    80: "Stopped",
    90: "Stopped",
    100: "Stopped",
}


def clear_data_cache():
    clear_api_cache()


def _normalize_filters(filters):
    filters = filters or {}

    def _clean(value):
        if value is None:
            return "All"
        text = str(value).strip()
        return text if text and text not in {"None", "nan"} else "All"

    return {
        "start_date": filters.get("start_date"),
        "end_date": filters.get("end_date"),
        "department": _clean(filters.get("department")),
        "machine_type": _clean(filters.get("machine_type")),
        "search_text": str(filters.get("search_text") or "").strip(),
    }


def _pretty_process_name(value):
    if value is None:
        return ""
    text = str(value).strip()
    if not text or text.lower() in {"none", "nan"}:
        return ""
    text = text.replace("_", " ").replace("-", " ")
    return " ".join(text.split()).title()


def _normalize_status_code(value):
    try:
        return STATUS_CODE_MAP.get(int(float(value)), "Stopped")
    except Exception:
        return "Stopped"


def _safe_numeric(series_or_value):
    if isinstance(series_or_value, pd.Series):
        return pd.to_numeric(series_or_value, errors="coerce").fillna(0)
    try:
        return float(series_or_value or 0)
    except Exception:
        return 0.0


def _format_minutes(minutes):
    try:
        minutes = int(round(float(minutes or 0)))
    except Exception:
        minutes = 0
    minutes = max(minutes, 0)
    hours, mins = divmod(minutes, 60)
    if hours and mins:
        return f"{hours}h {mins}m"
    if hours:
        return f"{hours}h"
    return f"{mins}m"


def _make_utc_compatible(value):
    ts = pd.to_datetime(value, errors="coerce", utc=True)
    return ts


def _format_duration_hours(start_time, reference_time=None):
    start_time = _make_utc_compatible(start_time)
    if pd.isna(start_time):
        return ""

    if reference_time is None:
        reference_time = pd.Timestamp.now(tz="UTC")
    else:
        reference_time = _make_utc_compatible(reference_time)
        if pd.isna(reference_time):
            reference_time = pd.Timestamp.now(tz="UTC")

    minutes = (reference_time - start_time).total_seconds() / 60
    return _format_minutes(minutes)


def _get_reference_time(df, filters=None):
    filters = _normalize_filters(filters)
    end_date = filters.get("end_date")

    if end_date:
        end_ts = pd.to_datetime(end_date, errors="coerce", utc=True)
        if pd.notna(end_ts):
            # Include the full selected day.
            if end_ts.hour == 0 and end_ts.minute == 0 and end_ts.second == 0:
                end_ts = end_ts + pd.Timedelta(days=1) - pd.Timedelta(seconds=1)
            return end_ts

    if df is not None and not df.empty and "Status From" in df.columns:
        dates = pd.to_datetime(df["Status From"], errors="coerce", utc=True).dropna()
        if not dates.empty:
            return dates.max()

    return pd.Timestamp.now(tz="UTC")


def _api_records(filters=None, apply_dates=True):
    filters = _normalize_filters(filters)
    records = fetch_dashboard_metadata(
        dept=filters["department"],
        created_from=filters["start_date"] if apply_dates else None,
        created_to=filters["end_date"] if apply_dates else None,
    )
    return records or []


def _api_dataframe(filters=None, apply_dates=True):
    filters = _normalize_filters(filters)
    records = _api_records(filters, apply_dates=apply_dates)

    if not records:
        return pd.DataFrame()

    df = pd.DataFrame(records)
    if df.empty:
        return pd.DataFrame()

    df = df.rename(
        columns={
            "jobId": "Job ID",
            "processName": "Process Name",
            "machineCode": "Machine Name",
            "dept": "Department",
            "createdAt": "Status From",
            "updatedAt": "Updated At",
            "status": "Status Code",
            "eventReason": "Reason",
            "interfaceLog": "Interface Log",
            "machineId": "Machine ID",
            "actualExecutedQty": "Actual Qty",
            "length": "Length",
            "plannedQty": "Planned Qty",
        }
    )

    # Guarantee all columns used by the dashboard exist.
    defaults = {
        "Job ID": 0,
        "Machine ID": 0,
        "Machine Name": "",
        "Process Name": "",
        "Department": "",
        "Status From": pd.NaT,
        "Updated At": pd.NaT,
        "Status Code": 0,
        "Reason": "Unspecified",
        "Interface Log": "",
        "Actual Qty": 0,
        "Length": 0,
        "Planned Qty": 0,
    }
    for col, default in defaults.items():
        if col not in df.columns:
            df[col] = default

    # Convert dates with UTC handling to avoid aware/naive comparison errors.
    df["Status From"] = pd.to_datetime(df["Status From"], errors="coerce", utc=True)
    df["Updated At"] = pd.to_datetime(df["Updated At"], errors="coerce", utc=True)
    df["Updated At"] = df["Updated At"].fillna(df["Status From"])

    for col in ["Actual Qty", "Length", "Planned Qty", "Machine ID", "Job ID", "Status Code"]:
        df[col] = pd.to_numeric(df[col], errors="coerce").fillna(0)

    # Do not allow machine id 0/null placeholders to become real machines.
    df = df[df["Machine ID"] > 0].copy()
    if df.empty:
        return pd.DataFrame()

    # If API does not provide a machine code yet, use a readable fallback.
    machine_name = df["Machine Name"].fillna("").astype(str).str.strip()
    fallback_name = "Machine " + df["Machine ID"].astype(int).astype(str)
    df["Machine Name"] = machine_name.where(machine_name.ne(""), fallback_name)

    df["Process Name"] = df["Process Name"].fillna("").astype(str)
    df["Machine Type"] = df["Process Name"].apply(_pretty_process_name)
    df["Department"] = df["Department"].fillna("").astype(str).str.strip()
    df["Status"] = df["Status Code"].apply(_normalize_status_code)
    df["Reason"] = df["Reason"].fillna("Unspecified").astype(str).replace("", "Unspecified")
    df["Interface Log"] = df["Interface Log"].fillna("").astype(str)

    df = _apply_local_filters(df, filters, apply_dates=apply_dates)

    return df


def _apply_local_filters(df, filters=None, apply_dates=True):
    if df is None or df.empty:
        return pd.DataFrame()

    filters = _normalize_filters(filters)
    df = df.copy()

    department = str(filters["department"] or "All").strip()
    machine_type = str(filters["machine_type"] or "All").strip()
    search_text = str(filters["search_text"] or "").strip().lower()

    if not apply_dates:
        # Current machine state should not disappear just because the selected
        # historical range excludes the latest machine event.
        filters = dict(filters)
        filters["start_date"] = None
        filters["end_date"] = None

    # Only filter Department/Machine Type when metadata exists in the dataset.
    if department != "All" and "Department" in df.columns:
        if df["Department"].astype(str).str.strip().ne("").any():
            df = df[df["Department"].astype(str).str.strip().eq(department)]

    if machine_type != "All" and "Machine Type" in df.columns:
        if df["Machine Type"].astype(str).str.strip().ne("").any():
            df = df[df["Machine Type"].astype(str).str.strip().eq(machine_type)]

    if search_text:
        search_cols = [
            "Machine Name",
            "Machine ID",
            "Department",
            "Machine Type",
            "Status",
            "Reason",
            "Interface Log",
            "Process Name",
        ]
        available = [c for c in search_cols if c in df.columns]
        if available:
            mask = pd.Series(False, index=df.index)
            for col in available:
                mask |= df[col].astype(str).str.lower().str.contains(
                    search_text, na=False, regex=False
                )
            df = df[mask]

    return df


def _latest_machine_rows(filters=None):
    df = _api_dataframe(filters, apply_dates=False)
    if df.empty:
        return pd.DataFrame()

    # Latest state per machine is what KPI cards must use.
    df = df.sort_values(
        ["Machine ID", "Status From", "Updated At", "Job ID"],
        ascending=[True, False, False, False],
        na_position="last",
    )
    latest = df.drop_duplicates(subset=["Machine ID"], keep="first").copy()

    return latest


def _add_status_duration(df, filters=None):
    if df is None or df.empty:
        return df
    df = df.copy()
    reference_time = _get_reference_time(df, filters)
    df["Duration"] = df["Status From"].apply(
        lambda value: _format_duration_hours(value, reference_time)
    )
    return df


def _calculate_event_durations(df, filters=None):
    if df is None or df.empty:
        return pd.DataFrame()

    df = df.copy()
    df = df.dropna(subset=["Status From"])
    if df.empty:
        return df

    df = df.sort_values(["Machine ID", "Status From"])
    df["Next Time"] = df.groupby("Machine ID")["Status From"].shift(-1)

    # For each machine's last event, use the selected/reference end time instead of
    # an arbitrary hard-coded 60 minutes.
    reference_time = _get_reference_time(df, filters)
    df["Next Time"] = df["Next Time"].fillna(reference_time)

    df["Duration Minutes"] = (
        (df["Next Time"] - df["Status From"]).dt.total_seconds() / 60
    )
    df["Duration Minutes"] = pd.to_numeric(
        df["Duration Minutes"], errors="coerce"
    ).fillna(0)
    df.loc[df["Duration Minutes"] < 0, "Duration Minutes"] = 0
    # Cap a single event to one day for dashboard readability.
    df.loc[df["Duration Minutes"] > 1440, "Duration Minutes"] = 1440
    return df


def machines_data(filters=None):
    return _latest_machine_rows(filters)


def downtime_events(filters=None):
    df = _api_dataframe(filters, apply_dates=True)
    if df.empty:
        return pd.DataFrame()
    df = df[df["Status"].isin(["Stopped", "Halt / Stop"])].copy()
    return _calculate_event_durations(df, filters)


def idle_events(filters=None):
    df = _api_dataframe(filters, apply_dates=True)
    if df.empty:
        return pd.DataFrame()
    df = df[df["Status"] == "Idle"].copy()
    return _calculate_event_durations(df, filters)


def get_filter_options():
    df = _api_dataframe(
        {
            "start_date": None,
            "end_date": None,
            "department": "All",
            "machine_type": "All",
            "search_text": "",
        }
    )

    if df.empty:
        return {
            "departments": ["All"],
            "machine_types": ["All"],
            "min_date": None,
            "max_date": None,
        }

    departments = ["All"]
    if "Department" in df.columns:
        vals = sorted(
            x for x in df["Department"].dropna().astype(str).str.strip().unique().tolist()
            if x
        )
        departments += vals

    machine_types = ["All"]
    if "Machine Type" in df.columns:
        vals = sorted(
            x for x in df["Machine Type"].dropna().astype(str).str.strip().unique().tolist()
            if x
        )
        machine_types += vals

    dates = pd.to_datetime(df["Status From"], errors="coerce", utc=True).dropna()
    min_date = dates.min().strftime("%Y-%m-%d") if not dates.empty else None
    max_date = dates.max().strftime("%Y-%m-%d") if not dates.empty else None

    return {
        "departments": departments,
        "machine_types": machine_types,
        "min_date": min_date,
        "max_date": max_date,
    }


def get_machine_types_by_department(department="All"):
    df = _api_dataframe(
        {
            "start_date": None,
            "end_date": None,
            "department": department or "All",
            "machine_type": "All",
            "search_text": "",
        }
    )
    if df.empty or "Machine Type" not in df.columns:
        return ["All"]

    values = sorted(
        x for x in df["Machine Type"].dropna().astype(str).str.strip().unique().tolist()
        if x
    )
    return ["All"] + values


def get_kpis(filters=None):
    machine_df = machines_data(filters)

    total = int(machine_df["Machine ID"].nunique()) if not machine_df.empty else 0

    if total == 0:
        return {
            "total": 0,
            "total_machines": 0,
            "running": 0,
            "stopped": 0,
            "idle": 0,
            "halt_stop": 0,
            "running_pct": 0,
            "stopped_pct": 0,
            "idle_pct": 0,
            "halt_stop_pct": 0,
            "oee": 0,
            "overall_oee": 0,
            "downtime": "0m",
            "total_downtime": "0m",
            "downtime_today": "0m",
            "idle_duration": "0m",
        }

    status_counts = machine_df["Status"].value_counts()
    running = int(status_counts.get("Running", 0))
    stopped = int(status_counts.get("Stopped", 0))
    idle = int(status_counts.get("Idle", 0))
    halt_stop = int(status_counts.get("Halt / Stop", 0))

    # OEE remains 0 until planned quantity is populated by the API query.
    actual_sum = _safe_numeric(machine_df["Actual Qty"]).sum()
    planned_sum = _safe_numeric(machine_df["Planned Qty"]).sum()
    oee = round((actual_sum / planned_sum) * 100, 1) if planned_sum > 0 else 0

    down_df = downtime_events(filters)
    idle_df = idle_events(filters)

    down_minutes = (
        _safe_numeric(down_df["Duration Minutes"]).sum()
        if not down_df.empty and "Duration Minutes" in down_df.columns
        else 0
    )
    idle_minutes = (
        _safe_numeric(idle_df["Duration Minutes"]).sum()
        if not idle_df.empty and "Duration Minutes" in idle_df.columns
        else 0
    )

    result = {
        "total": total,
        "total_machines": total,
        "running": running,
        "stopped": stopped,
        "idle": idle,
        "halt_stop": halt_stop,
        "running_pct": round(running / total * 100, 1),
        "stopped_pct": round(stopped / total * 100, 1),
        "idle_pct": round(idle / total * 100, 1),
        "halt_stop_pct": round(halt_stop / total * 100, 1),
        "oee": oee,
        "overall_oee": oee,
        "downtime": _format_minutes(down_minutes),
        "total_downtime": _format_minutes(down_minutes),
        "downtime_today": _format_minutes(down_minutes),
        "idle_duration": _format_minutes(idle_minutes),
    }
    return result


def machine_status_trend(filters=None):
    df = _api_dataframe(filters, apply_dates=True)
    empty = pd.DataFrame(columns=["Date", *ALLOWED_STATUSES])
    if df.empty or "Status From" not in df.columns:
        return empty

    df = df.dropna(subset=["Status From"]).copy()
    if df.empty:
        return empty

    # For each day, take each machine's final state on that day.
    df["DateOnly"] = df["Status From"].dt.floor("D")
    df = df.sort_values(["DateOnly", "Machine ID", "Status From"])
    df = df.drop_duplicates(subset=["DateOnly", "Machine ID"], keep="last")

    trend = (
        df.groupby(["DateOnly", "Status"])
        .size()
        .reset_index(name="Count")
        .pivot(index="DateOnly", columns="Status", values="Count")
        .fillna(0)
        .reset_index()
    )

    for status in ALLOWED_STATUSES:
        if status not in trend.columns:
            trend[status] = 0

    trend["Total"] = trend[ALLOWED_STATUSES].sum(axis=1)
    for status in ALLOWED_STATUSES:
        trend[status] = trend.apply(
            lambda r: round(r[status] / r["Total"] * 100, 1) if r["Total"] else 0,
            axis=1,
        )

    trend = trend.sort_values("DateOnly")
    trend["Date"] = trend["DateOnly"].dt.strftime("%d %b")
    return trend[["Date", *ALLOWED_STATUSES]]


def downtime_reasons(filters=None):
    df = downtime_events(filters)
    if df.empty:
        return pd.DataFrame(columns=["Reason", "Minutes"])

    df["Reason"] = df["Reason"].fillna("Unspecified").astype(str).replace("", "Unspecified")
    return (
        df.groupby("Reason", as_index=False)["Duration Minutes"]
        .sum()
        .rename(columns={"Duration Minutes": "Minutes"})
        .sort_values("Minutes", ascending=False)
    )


def _trend_from_events(df):
    if df is None or df.empty or "Status From" not in df.columns:
        return pd.DataFrame(columns=["Date", "Hours"])

    df = df.dropna(subset=["Status From"]).copy()
    if df.empty:
        return pd.DataFrame(columns=["Date", "Hours"])

    df["DateOnly"] = df["Status From"].dt.floor("D")
    result = df.groupby("DateOnly", as_index=False)["Duration Minutes"].sum()
    result["Hours"] = (result["Duration Minutes"] / 60).round(1)
    result = result.sort_values("DateOnly")
    result["Date"] = result["DateOnly"].dt.strftime("%d %b")
    return result[["Date", "Hours"]]


def downtime_trend(filters=None):
    return _trend_from_events(downtime_events(filters))


def idle_trend(filters=None):
    return _trend_from_events(idle_events(filters))


def stopped_machine_detail(filters=None):
    df = machines_data(filters)
    if df.empty:
        return pd.DataFrame()
    df = df[df["Status"] == "Stopped"].copy()
    if df.empty:
        return pd.DataFrame()
    df = _add_status_duration(df, filters)
    df = df.rename(columns={"Status From": "Since From"})
    return df[[c for c in ["Machine Name", "Since From", "Duration"] if c in df.columns]]


def idle_machine_detail(filters=None):
    df = machines_data(filters)
    if df.empty:
        return pd.DataFrame()
    df = df[df["Status"] == "Idle"].copy()
    if df.empty:
        return pd.DataFrame()
    df = _add_status_duration(df, filters)
    df = df.rename(columns={"Status From": "Since From"})
    return df[[c for c in ["Machine Name", "Since From", "Duration"] if c in df.columns]]


def department_summary(filters=None):
    df = machines_data(filters)
    columns = ["Department", "Total", "Running", "Idle", "Stopped", "Halt / Stop", "OEE %"]
    if df.empty:
        return pd.DataFrame(columns=columns)

    # API currently returns blank department; use a dashboard-safe fallback bucket.
    dept_series = df["Department"].fillna("").astype(str).str.strip()
    df = df.copy()
    df["Department"] = dept_series.where(dept_series.ne(""), "All Machines")

    rows = []
    for department, group in df.groupby("Department"):
        counts = group["Status"].value_counts()
        actual = _safe_numeric(group["Actual Qty"]).sum()
        planned = _safe_numeric(group["Planned Qty"]).sum()
        oee = round(actual / planned * 100, 1) if planned > 0 else 0
        rows.append(
            {
                "Department": department,
                "Total": int(group["Machine ID"].nunique()),
                "Running": int(counts.get("Running", 0)),
                "Idle": int(counts.get("Idle", 0)),
                "Stopped": int(counts.get("Stopped", 0)),
                "Halt / Stop": int(counts.get("Halt / Stop", 0)),
                "OEE %": oee,
            }
        )
    return pd.DataFrame(rows, columns=columns)


def _production_event_dataframe(filters=None, apply_dates=True):
    df = _api_dataframe(filters, apply_dates=apply_dates)
    if df.empty:
        return pd.DataFrame()

    required = ["Job ID", "Status From", "Actual Qty", "Length"]
    if any(c not in df.columns for c in required):
        return pd.DataFrame()

    df = df.dropna(subset=["Status From"]).copy()
    if df.empty:
        return df

    # Calculate deltas in chronological order. Do NOT drop machines here; production
    # requires all events, unlike current-state KPI cards.
    df = df.sort_values(["Job ID", "Status From"])

    df["Previous Actual Qty"] = df.groupby("Job ID")["Actual Qty"].shift(1)
    df["Production Qty"] = df["Actual Qty"] - df["Previous Actual Qty"]
    df["Production Qty"] = df["Production Qty"].fillna(df["Actual Qty"])
    df.loc[df["Production Qty"] < 0, "Production Qty"] = df.loc[
        df["Production Qty"] < 0, "Actual Qty"
    ]

    df["Previous Length"] = df.groupby("Job ID")["Length"].shift(1)
    df["Production Length"] = df["Length"] - df["Previous Length"]
    df["Production Length"] = df["Production Length"].fillna(df["Length"])
    df.loc[df["Production Length"] < 0, "Production Length"] = df.loc[
        df["Production Length"] < 0, "Length"
    ]
    df["KM"] = df["Production Length"] / 1000
    return df


def last_24h_production(filters=None):
    df = _production_event_dataframe(filters)
    daily_target_tons = round(YEARLY_PRODUCTION_TARGET_TONS / 365, 2)
    daily_target_km = round(YEARLY_PRODUCTION_TARGET_KM / 365, 2)

    empty_result = {
        "actual": 0,
        "actual_km": 0,
        "target": daily_target_tons,
        "target_km": daily_target_km,
        "achievement_pct": 0,
        "gap": -daily_target_tons,
        "gap_km": -daily_target_km,
        "gap_label": "Below Target",
        "uom": "Tons",
        "km_uom": "KM",
        "records": 0,
        "from_time": "-",
        "to_time": "-",
    }

    if df.empty:
        return empty_result

    to_time = pd.Timestamp.now(tz="UTC")
    if pd.isna(to_time):
        return empty_result
    from_time = to_time - pd.Timedelta(hours=24)

    window = df[(df["Status From"] >= from_time) & (df["Status From"] <= to_time)].copy()
    actual = round(float(_safe_numeric(window["Production Qty"]).sum()), 2)
    actual_km = round(float(_safe_numeric(window["KM"]).sum()), 2)
    target = daily_target_tons
    target_km = daily_target_km
    achievement = round(actual / target * 100, 1) if target > 0 else 0
    gap = round(actual - target, 2)
    gap_km = round(actual_km - target_km, 2)

    return {
        "actual": actual,
        "actual_km": actual_km,
        "target": target,
        "target_km": target_km,
        "achievement_pct": achievement,
        "gap": gap,
        "gap_km": gap_km,
        "gap_label": "Above Target" if gap >= 0 else "Below Target",
        "uom": "Tons",
        "km_uom": "KM",
        "records": len(window),
        "from_time": from_time.strftime("%d %b %Y %I:%M %p"),
        "to_time": to_time.strftime("%d %b %Y %I:%M %p"),
    }


def planned_vs_actual_monthly(filters=None):
    df = _production_event_dataframe(filters, apply_dates=True)
    filters = _normalize_filters(filters)

    selected_year = 2026
    if filters.get("end_date"):
        end_date = pd.to_datetime(filters["end_date"], errors="coerce")
        if pd.notna(end_date):
            selected_year = end_date.year
    elif not df.empty:
        selected_year = int(df["Status From"].max().year)

    months = pd.date_range(
        start=f"{selected_year}-01-01",
        end=f"{selected_year}-12-01",
        freq="MS",
    )
    monthly_target = round(YEARLY_PRODUCTION_TARGET_TONS / 12, 2)
    base = pd.DataFrame(
        {
            "MonthNo": range(1, 13),
            "Month": [m.strftime("%b") for m in months],
            "Target": monthly_target,
            "Actual": 0.0,
        }
    )

    if not df.empty:
        year_df = df[df["Status From"].dt.year == selected_year].copy()
        if not year_df.empty:
            year_df["MonthNo"] = year_df["Status From"].dt.month
            actual = year_df.groupby("MonthNo", as_index=False)["Production Qty"].sum()
            actual = actual.rename(columns={"Production Qty": "ActualNew"})
            base = base.merge(actual, on="MonthNo", how="left")
            base["Actual"] = base["ActualNew"].fillna(base["Actual"])
            base = base.drop(columns=["ActualNew"])

    base["Achievement %"] = base.apply(
        lambda r: round(r["Actual"] / r["Target"] * 100, 1) if r["Target"] else 0,
        axis=1,
    )
    return base[["Month", "Target", "Actual", "Achievement %"]]


def production_daily_trend(filters=None, days=7):
    df = _production_event_dataframe(filters, apply_dates=True)
    if df.empty:
        return pd.DataFrame(columns=["Date", "Target", "Actual"])

    df = df.dropna(subset=["Status From"]).copy()
    if df.empty:
        return pd.DataFrame(columns=["Date", "Target", "Actual"])

    end_date = pd.Timestamp.now(tz="UTC").floor("D")
    start_date = end_date - pd.Timedelta(days=max(int(days) - 1, 0))
    window = df[
        (df["Status From"].dt.floor("D") >= start_date)
        & (df["Status From"].dt.floor("D") <= end_date)
    ].copy()

    if window.empty:
        return pd.DataFrame(columns=["Date", "Target", "Actual"])

    window["DateOnly"] = window["Status From"].dt.floor("D")
    daily_target = round(YEARLY_PRODUCTION_TARGET_TONS / 365, 2)
    result = window.groupby("DateOnly", as_index=False)["Production Qty"].sum()
    result = result.rename(columns={"Production Qty": "Actual"})
    result["Date"] = result["DateOnly"].dt.strftime("%d %b")
    result["Target"] = daily_target
    return result[["Date", "Target", "Actual"]]
