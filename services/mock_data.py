import pandas as pd

from services.api_service import (
    fetch_dashboard_metadata,
    fetch_production_targets,
    clear_api_cache,
)


ALLOWED_STATUSES = ["Running", "Idle", "Halt / Stop"]

# Current machine-state mapping used by KPI cards and current-machine tables.
# Business rule: a machine with no active/running job is shown as Idle rather
# than as a separate Stopped state. Halt / Stop remains a temporary interruption
# of an active job.
STATUS_CODE_MAP = {
    0: "Idle",
    1: "Running",
    10: "Running",
    20: "Running",
    30: "Halt / Stop",
    40: "Idle",
    50: "Halt / Stop",
    60: "Halt / Stop",
    70: "Idle",
    80: "Idle",
    90: "Idle",
    100: "Idle",
}

# Keep the existing historical downtime-event population for the donut chart.
# This preserves the current reason totals while the current machine KPI view
# merges the old Stopped state into Idle.
HALT_STOP_EVENT_CODES = {0, 30, 50, 60, 70, 80, 90, 100}


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
        return STATUS_CODE_MAP.get(int(float(value)), "Idle")
    except Exception:
        return "Idle"


def _safe_numeric(series_or_value):
    if isinstance(series_or_value, pd.Series):
        return pd.to_numeric(series_or_value, errors="coerce").fillna(0)
    try:
        return float(series_or_value or 0)
    except Exception:
        return 0.0


def _format_minutes(minutes):
    """Format minutes as a compact user-friendly duration."""
    try:
        minutes = int(round(float(minutes or 0)))
    except Exception:
        minutes = 0

    minutes = max(minutes, 0)
    days, remainder = divmod(minutes, 1440)
    hours, mins = divmod(remainder, 60)

    parts = []
    if days:
        parts.append(f"{days}d")
    if hours or days:
        parts.append(f"{hours}h")
    parts.append(f"{mins}m")
    return " ".join(parts)


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
    """Use selected range end when present; otherwise use real current UTC time."""
    filters = _normalize_filters(filters)
    end_date = filters.get("end_date")

    if end_date:
        end_ts = pd.to_datetime(end_date, errors="coerce", utc=True)
        if pd.notna(end_ts):
            if end_ts.hour == 0 and end_ts.minute == 0 and end_ts.second == 0:
                end_ts = end_ts + pd.Timedelta(days=1) - pd.Timedelta(seconds=1)
            return end_ts

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
            "plannedQtySource": "Planned Qty Source",
            "productionUom": "Production UOM",
            "actualProductionTons": "Production Tons",
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
        "Planned Qty Source": "",
        "Production UOM": "",
        "Production Tons": pd.NA,
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
    df["Production Tons"] = pd.to_numeric(df["Production Tons"], errors="coerce")

    # Do not allow machine id 0/null placeholders to become real machines.
    df = df[df["Machine ID"] > 0].copy()
    if df.empty:
        return pd.DataFrame()

    # Machine Name comes from cableflow.job_machines_master through api_service.
    # Keep the numeric ID only as a defensive fallback if a master row is missing.
    machine_name = df["Machine Name"].fillna("").astype(str).str.strip()
    fallback_name = df["Machine ID"].astype(int).astype(str)
    df["Machine Name"] = machine_name.where(machine_name.ne(""), fallback_name)

    df["Process Name"] = df["Process Name"].fillna("").astype(str)
    df["Machine Type"] = df["Process Name"].apply(_pretty_process_name)
    df["Department"] = df["Department"].fillna("").astype(str).str.strip()
    df["Planned Qty Source"] = df["Planned Qty Source"].fillna("").astype(str).str.strip()
    df["Production UOM"] = df["Production UOM"].fillna("").astype(str).str.strip()
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
    starts = pd.to_datetime(df["Status From"], errors="coerce", utc=True)
    df["Duration Minutes"] = ((reference_time - starts).dt.total_seconds() / 60).clip(lower=0)
    df["Duration Minutes"] = pd.to_numeric(df["Duration Minutes"], errors="coerce").fillna(0)
    df["Duration"] = df["Duration Minutes"].apply(_format_minutes)
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

    # Historical interruption events used by the Halt / Stop downtime chart.
    # Use raw event codes so merging current Stopped machines into Idle does not
    # erase the existing downtime-reason history.
    df = df[df["Status Code"].astype(int).isin(HALT_STOP_EVENT_CODES)].copy()
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
    idle = int(status_counts.get("Idle", 0))
    halt_stop = int(status_counts.get("Halt / Stop", 0))
    stopped = 0  # kept only for backwards compatibility with older UI code

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
        "stopped_pct": 0,
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


def machine_status_trend(filters=None, days=7):
    """
    Build a true daily status snapshot. For each day we take the latest event
    at or before that day's end for every machine, then calculate the status
    distribution. This avoids duplicate-event distortion and keeps daily
    percentages aligned to the machine population.
    """
    filters = _normalize_filters(filters)
    df = _api_dataframe(filters, apply_dates=False)
    empty = pd.DataFrame(columns=["Date", *ALLOWED_STATUSES])

    if df.empty or "Status From" not in df.columns:
        return empty

    df = df.dropna(subset=["Status From"]).copy()
    if df.empty:
        return empty

    end_day = pd.Timestamp.now(tz="UTC").floor("D")
    if filters.get("end_date"):
        selected_end = pd.to_datetime(filters["end_date"], errors="coerce", utc=True)
        if pd.notna(selected_end):
            end_day = selected_end.floor("D")

    start_day = end_day - pd.Timedelta(days=max(int(days) - 1, 0))
    if filters.get("start_date"):
        selected_start = pd.to_datetime(filters["start_date"], errors="coerce", utc=True)
        if pd.notna(selected_start):
            start_day = max(start_day, selected_start.floor("D"))

    rows = []
    for day in pd.date_range(start=start_day, end=end_day, freq="D"):
        day_end = day + pd.Timedelta(days=1) - pd.Timedelta(microseconds=1)
        snapshot = df[df["Status From"] <= day_end].copy()
        if snapshot.empty:
            continue

        snapshot = snapshot.sort_values(
            ["Machine ID", "Status From", "Updated At", "Job ID"],
            ascending=[True, False, False, False],
            na_position="last",
        ).drop_duplicates(subset=["Machine ID"], keep="first")

        total = int(snapshot["Machine ID"].nunique())
        counts = snapshot["Status"].value_counts()
        row = {"Date": day.strftime("%d %b")}
        for status in ALLOWED_STATUSES:
            row[status] = round(float(counts.get(status, 0)) / total * 100, 1) if total else 0
        rows.append(row)

    return pd.DataFrame(rows, columns=["Date", *ALLOWED_STATUSES])


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
    # Longest-idle machine first. Keep the numeric helper only for sorting.
    df = df.sort_values("Duration Minutes", ascending=False, na_position="last")
    df = df.rename(columns={"Status From": "Since From"})
    return df[[c for c in ["Machine Name", "Since From", "Duration"] if c in df.columns]]


def department_summary(filters=None):
    df = machines_data(filters)
    columns = ["Department", "Total", "Running", "Idle", "Halt / Stop", "OEE %"]
    if df.empty:
        return pd.DataFrame(columns=columns)

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
                "Halt / Stop": int(counts.get("Halt / Stop", 0)),
                "OEE %": oee,
            }
        )
    return pd.DataFrame(rows, columns=columns)

def _is_ton_uom(value):
    text = str(value or "").strip().upper().replace(".", "")
    return text in {"TON", "TONS", "MT", "M/T", "METRIC TON", "METRIC TONS", "TONNE", "TONNES"}


def _explicit_ton_target_source(source_name):
    text = str(source_name or "").lower()
    return any(token in text for token in ["ton", "tonnage"])


def _production_event_dataframe(filters=None, apply_dates=True):
    """
    Return production events without inventing a tonnage conversion.

    Actual Tons is accepted only when the database exposes an explicit tonnage
    column, or when the job UOM says the generic actual quantity is already tons.
    Planned/Target Tons follows the same rule. If neither condition is met, the
    dashboard reports the ton values as unavailable instead of using a hardcoded
    target or silently treating metres/units as tons.
    """
    df = _api_dataframe(filters, apply_dates=apply_dates)
    if df.empty:
        return pd.DataFrame()

    required = ["Job ID", "Status From", "Actual Qty", "Planned Qty"]
    if any(c not in df.columns for c in required):
        return pd.DataFrame()

    df = df.dropna(subset=["Status From"]).copy()
    if df.empty:
        return df

    uom_is_tons = df.get("Production UOM", pd.Series("", index=df.index)).apply(_is_ton_uom)
    explicit_actual = pd.to_numeric(df.get("Production Tons", 0), errors="coerce")
    generic_actual = pd.to_numeric(df["Actual Qty"], errors="coerce").fillna(0)

    df["Actual Tons Source"] = explicit_actual.where(explicit_actual.notna())
    df.loc[df["Actual Tons Source"].isna() & uom_is_tons, "Actual Tons Source"] = generic_actual

    planned = pd.to_numeric(df["Planned Qty"], errors="coerce").fillna(0)
    source_is_tons = df.get("Planned Qty Source", pd.Series("", index=df.index)).apply(_explicit_ton_target_source)
    df["Target Tons Source"] = pd.NA
    df.loc[source_is_tons | uom_is_tons, "Target Tons Source"] = planned
    df["Target Tons Source"] = pd.to_numeric(df["Target Tons Source"], errors="coerce")

    # Delta actual cumulative production per job, when a valid tonnage source exists.
    df = df.sort_values(["Job ID", "Status From"])
    df["Previous Actual Tons"] = df.groupby("Job ID")["Actual Tons Source"].shift(1)
    df["Production Tons Delta"] = df["Actual Tons Source"] - df["Previous Actual Tons"]
    first_actual = df["Previous Actual Tons"].isna() & df["Actual Tons Source"].notna()
    df.loc[first_actual, "Production Tons Delta"] = df.loc[first_actual, "Actual Tons Source"]
    negative = df["Production Tons Delta"] < 0
    df.loc[negative, "Production Tons Delta"] = df.loc[negative, "Actual Tons Source"]

    # Length remains available as a separate real metric, never relabelled as tons.
    length = pd.to_numeric(df.get("Length", 0), errors="coerce").fillna(0)
    df["Previous Length"] = df.groupby("Job ID")["Length"].shift(1)
    df["Production Length"] = length - pd.to_numeric(df["Previous Length"], errors="coerce")
    df["Production Length"] = df["Production Length"].fillna(length)
    df.loc[df["Production Length"] < 0, "Production Length"] = length
    df["KM"] = df["Production Length"] / 1000
    return df


def _target_for_window(window):
    """Sum one DB target per job, avoiding repetition on every event row."""
    if window is None or window.empty or "Target Tons Source" not in window.columns:
        return None

    valid = window.dropna(subset=["Target Tons Source"]).copy()
    valid = valid[valid["Target Tons Source"] > 0]
    if valid.empty:
        return None

    per_job = valid.sort_values("Status From").drop_duplicates(subset=["Job ID"], keep="last")
    return round(float(per_job["Target Tons Source"].sum()), 2)


def _actual_for_window(window):
    if window is None or window.empty or "Production Tons Delta" not in window.columns:
        return None
    values = pd.to_numeric(window["Production Tons Delta"], errors="coerce").dropna()
    if values.empty:
        return None
    return round(float(values.sum()), 2)


def last_24h_production(filters=None):
    # Fetch complete production history so the 24-hour window is anchored to
    # real current time rather than to the last event timestamp.
    df = _production_event_dataframe(filters, apply_dates=False)

    to_time = pd.Timestamp.now(tz="UTC")
    from_time = to_time - pd.Timedelta(hours=24)

    empty_result = {
        "actual": None,
        "target": None,
        "achievement_pct": None,
        "gap": None,
        "gap_label": "Target Unavailable",
        "uom": "Tons",
        "records": 0,
        "from_time": from_time.strftime("%d %b %Y %I:%M %p"),
        "to_time": to_time.strftime("%d %b %Y %I:%M %p"),
        "data_available": False,
        "target_available": False,
    }

    if df.empty:
        return empty_result

    window = df[(df["Status From"] >= from_time) & (df["Status From"] <= to_time)].copy()
    actual = _actual_for_window(window)
    target = _target_for_window(window)

    achievement = None
    gap = None
    gap_label = "Target Unavailable"
    if actual is not None and target is not None and target > 0:
        achievement = round(actual / target * 100, 1)
        gap = round(actual - target, 2)
        gap_label = "Above Target" if gap >= 0 else "Below Target"

    return {
        "actual": actual,
        "target": target,
        "achievement_pct": achievement,
        "gap": gap,
        "gap_label": gap_label,
        "uom": "Tons",
        "records": len(window),
        "from_time": from_time.strftime("%d %b %Y %I:%M %p"),
        "to_time": to_time.strftime("%d %b %Y %I:%M %p"),
        "data_available": actual is not None,
        "target_available": target is not None,
    }


def planned_vs_actual_monthly(filters=None):
    df = _production_event_dataframe(filters, apply_dates=True)
    if df.empty:
        return pd.DataFrame(columns=["Month", "Target", "Actual", "Achievement %"])

    filters = _normalize_filters(filters)
    selected_year = pd.Timestamp.now(tz="UTC").year
    if filters.get("end_date"):
        end_date = pd.to_datetime(filters["end_date"], errors="coerce", utc=True)
        if pd.notna(end_date):
            selected_year = end_date.year

    year_df = df[df["Status From"].dt.year == selected_year].copy()
    if year_df.empty:
        return pd.DataFrame(columns=["Month", "Target", "Actual", "Achievement %"])

    year_df["MonthNo"] = year_df["Status From"].dt.month
    rows = []
    for month_no, group in year_df.groupby("MonthNo"):
        actual = _actual_for_window(group)
        target = _target_for_window(group)
        rows.append(
            {
                "MonthNo": int(month_no),
                "Month": pd.Timestamp(selected_year, int(month_no), 1).strftime("%b"),
                "Target": target,
                "Actual": actual,
                "Achievement %": round(actual / target * 100, 1)
                if actual is not None and target is not None and target > 0
                else None,
            }
        )

    return pd.DataFrame(rows).sort_values("MonthNo")[["Month", "Target", "Actual", "Achievement %"]]


def production_daily_trend(filters=None, days=7):
    """
    Production Overview is sourced from the production targets API.

    The API returns one row per work center.  The dashboard deliberately sums
    all work centers and displays one combined Target/Actual result, as
    requested.  Example:

        GCF: target=0, actual=30
        PCF: target=0, actual=55

    becomes:

        Target = 0
        Actual = 85

    Achievement is calculated by the UI only when Target > 0.
    """
    rows = fetch_production_targets()

    if not rows:
        return pd.DataFrame(columns=["Date", "Target", "Actual"])

    target_total = 0.0
    actual_total = 0.0

    for row in rows:
        try:
            target_total += float(row.get("target", 0) or 0)
        except (TypeError, ValueError):
            pass

        try:
            actual_total += float(row.get("actual", 0) or 0)
        except (TypeError, ValueError):
            pass

    return pd.DataFrame(
        [
            {
                "Date": "Total",
                "Target": round(target_total, 2),
                "Actual": round(actual_total, 2),
            }
        ]
    )

