from functools import lru_cache
import time

import pandas as pd

from services.api_service import (
    DB_SCHEMA,
    fetch_dashboard_metadata,
    fetch_production_targets,
    fetch_machine_efficiency,
    fetch_production_groups,
    fetch_operational_kpis,
    clear_api_cache,
    _get_connection,
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
    _machine_master_dataframe_cached.cache_clear()
    _api_dataframe_cached.cache_clear()
    _latest_event_rows_cached.cache_clear()
    _latest_machine_rows_cached.cache_clear()
    _downtime_events_cached.cache_clear()
    _idle_events_cached.cache_clear()


def _dashboard_timing(label, start_time):
    elapsed = time.perf_counter() - start_time
    print(f"Dashboard Timing -> {label}: {elapsed:.2f}s")


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
        "plant": _clean(filters.get("plant")),
        "department": _clean(filters.get("department")),
        "machine_type": _clean(filters.get("machine_type")),
        "search_text": str(filters.get("search_text") or "").strip(),
    }


def _filters_cache_key(filters=None, apply_dates=True):
    normalized = _normalize_filters(filters)
    return (
        normalized.get("start_date"),
        normalized.get("end_date"),
        normalized.get("plant"),
        normalized.get("department"),
        normalized.get("machine_type"),
        normalized.get("search_text"),
        bool(apply_dates),
    )


def _filters_from_cache_key(key):
    return {
        "start_date": key[0],
        "end_date": key[1],
        "plant": key[2],
        "department": key[3],
        "machine_type": key[4],
        "search_text": key[5],
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


def _normalize_plant_value(value):
    text = str(value or "").strip().upper()
    compact = "".join(ch for ch in text if ch.isalnum())

    if compact in {"ALL", "ALLPLANTS", "NONE", "NAN", ""}:
        return "All"
    # GCFA/legacy NCFA must be checked before the broader GCFB/GCF rule.
    if "GCFA" in compact or "NCFA" in compact or compact.startswith("NCF"):
        return "GCFA"
    if "GCFB" in compact or compact == "GCF":
        return "GCFB"
    if "PCF" in compact:
        return "PCF"
    return text


def _normalize_department_value(value):
    text = str(value or "").strip().upper()
    if text in {"ALL", "ALL PLANTS", "ALLPLANTS", "NONE", "NAN", ""}:
        return "All"
    return text


def _should_filter_department(plant):
    selected_plant = str(plant or "All").strip().upper()
    return selected_plant not in {"ALL", "ALL PLANTS", "ALLPLANTS", ""}


def _machine_master_dataframe(plant="All"):
    normalized_plant = _normalize_plant_value(plant)
    return _machine_master_dataframe_cached(normalized_plant).copy()


@lru_cache(maxsize=16)
def _machine_master_dataframe_cached(normalized_plant="All"):
    start_time = time.perf_counter()
    conn = None
    try:
        conn = _get_connection()
        query = f"""
            SELECT
                mm.id AS "Machine ID",
                COALESCE(NULLIF(BTRIM(mm.machine_name), ''), CAST(mm.id AS TEXT)) AS "Machine Name",
                TRIM(UPPER(mm.department)) AS "Department",
                TRIM(COALESCE(mm.machine_type, '')) AS "Machine Type"
            FROM {DB_SCHEMA}.job_machines_master mm
            WHERE mm.id IS NOT NULL
        """
        params = []
        if _should_filter_department(normalized_plant):
            query += " AND TRIM(UPPER(mm.department)) = %s"
            params.append(normalized_plant)

        df = pd.read_sql_query(query, conn, params=params)
        columns = ["Machine ID", "Machine Name", "Department", "Machine Type"]
        if df.empty:
            return pd.DataFrame(columns=columns)

        df["Machine ID"] = pd.to_numeric(df["Machine ID"], errors="coerce").fillna(0).astype(int)
        df["Machine Name"] = df["Machine Name"].fillna("").astype(str).str.strip()
        df["Department"] = df["Department"].fillna("").astype(str).str.strip().str.upper()
        df["Machine Type"] = df["Machine Type"].fillna("").astype(str).str.strip()
        print("Machine Scope ->")
        print(f"Plant: {normalized_plant}")
        print(f"Machine Count: {int(df['Machine ID'].nunique())}")
        _dashboard_timing("Machine Scope", start_time)
        return df[df["Machine ID"] > 0].reset_index(drop=True)
    except Exception as error:
        print(f"Error reading job_machines_master: {error}")
        _dashboard_timing("Machine Scope", start_time)
        return pd.DataFrame(columns=["Machine ID", "Machine Name", "Department", "Machine Type"])
    finally:
        if conn:
            conn.close()


def _latest_event_rows(filters=None):
    key = _filters_cache_key(filters, apply_dates=False)
    return _latest_event_rows_cached(key).copy()


@lru_cache(maxsize=64)
def _latest_event_rows_cached(key):
    start_time = time.perf_counter()
    filters = _filters_from_cache_key(key)
    event_filters = dict(_normalize_filters(filters))
    event_filters["plant"] = "All"
    event_filters["department"] = "All"
    event_filters["machine_type"] = "All"
    event_filters["search_text"] = ""
    df = _api_dataframe(event_filters, apply_dates=False)
    if df.empty:
        _dashboard_timing("Latest Machine Status", start_time)
        return pd.DataFrame()
    df = df.sort_values(
        ["Machine ID", "Status From", "Updated At", "Job ID"],
        ascending=[True, False, False, False],
        na_position="last",
    )
    result = df.drop_duplicates(subset=["Machine ID"], keep="first").copy()
    _dashboard_timing("Latest Machine Status", start_time)
    return result


def _infer_plant_from_row(row):
    # Keep plant resolution aligned to the authoritative department field.
    department = row.get("Department", "")
    plant = row.get("Plant", "")

    normalized_department = _normalize_plant_value(department)
    if normalized_department != "All":
        return normalized_department

    return _normalize_plant_value(plant)


def _plant_matches_work_center(plant, work_center):
    plant = _normalize_plant_value(plant)
    wc = str(work_center or "").strip().upper()
    production_work_centers = {
        "PCF": {"PCF"},
        "GCFA": {"GCF"},
        "GCFB": {"GCF"},
        "All": {"GCF", "PCF"},
    }
    return wc in production_work_centers.get(plant, {plant})


def _api_records(filters=None, apply_dates=True):
    filters = _normalize_filters(filters)
    records = fetch_dashboard_metadata(
        dept=filters["department"],
        created_from=filters["start_date"] if apply_dates else None,
        created_to=filters["end_date"] if apply_dates else None,
    )
    return records or []


def _api_dataframe(filters=None, apply_dates=True):
    key = _filters_cache_key(filters, apply_dates=apply_dates)
    return _api_dataframe_cached(key).copy()


@lru_cache(maxsize=64)
def _api_dataframe_cached(key):
    start_time = time.perf_counter()
    filters = _filters_from_cache_key(key)
    apply_dates = key[6]
    records = _api_records(filters, apply_dates=apply_dates)

    if not records:
        _dashboard_timing("Event DataFrame", start_time)
        return pd.DataFrame()

    df = pd.DataFrame(records)
    if df.empty:
        _dashboard_timing("Event DataFrame", start_time)
        return pd.DataFrame()

    df = df.rename(
        columns={
            "jobId": "Job ID",
            "processName": "Process Name",
            "machineCode": "Machine Name",
            "dept": "Department",
            "plant": "Plant",
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
        "Plant": "",
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
        _dashboard_timing("Event DataFrame", start_time)
        return pd.DataFrame()

    # Machine Name comes from cableflow.job_machines_master through api_service.
    # Keep the numeric ID only as a defensive fallback if a master row is missing.
    machine_name = df["Machine Name"].fillna("").astype(str).str.strip()
    fallback_name = df["Machine ID"].astype(int).astype(str)
    df["Machine Name"] = machine_name.where(machine_name.ne(""), fallback_name)

    df["Process Name"] = df["Process Name"].fillna("").astype(str)
    df["Machine Type"] = df["Process Name"].apply(_pretty_process_name)
    df["Department"] = df["Department"].fillna("").astype(str).str.strip()
    df["Plant"] = df["Plant"].fillna("").astype(str).str.strip()
    inferred_plant = df.apply(_infer_plant_from_row, axis=1)
    explicit_plant = df["Plant"].apply(_normalize_plant_value)
    df["Plant"] = explicit_plant.where(explicit_plant.ne("All"), inferred_plant)
    df["Planned Qty Source"] = df["Planned Qty Source"].fillna("").astype(str).str.strip()
    df["Production UOM"] = df["Production UOM"].fillna("").astype(str).str.strip()
    df["Status"] = df["Status Code"].apply(_normalize_status_code)
    df["Reason"] = df["Reason"].fillna("Unspecified").astype(str).replace("", "Unspecified")
    df["Interface Log"] = df["Interface Log"].fillna("").astype(str)

    df = _apply_local_filters(df, filters, apply_dates=apply_dates)

    _dashboard_timing("Event DataFrame", start_time)
    return df


def _apply_local_filters(df, filters=None, apply_dates=True):
    if df is None or df.empty:
        return pd.DataFrame()

    filters = _normalize_filters(filters)
    df = df.copy()

    plant = _normalize_plant_value(filters.get("plant", "All"))
    department = str(filters["department"] or "All").strip()
    machine_type = str(filters["machine_type"] or "All").strip()
    search_text = str(filters["search_text"] or "").strip().lower()

    if not apply_dates:
        # Current machine state should not disappear just because the selected
        # historical range excludes the latest machine event.
        filters = dict(filters)
        filters["start_date"] = None
        filters["end_date"] = None

    if plant != "All":
        if "Department" in df.columns and df["Department"].astype(str).str.strip().ne("").any():
            df = df[df["Department"].astype(str).apply(_normalize_plant_value).eq(plant)]
        elif "Plant" in df.columns:
            df = df[df["Plant"].astype(str).apply(_normalize_plant_value).eq(plant)]

    # Only filter Department/Machine Type when metadata exists in the dataset.
    if department != "All" and "Department" in df.columns:
        if df["Department"].astype(str).str.strip().ne("").any():
            normalized_department = _normalize_department_value(department)
            df = df[
                df["Department"]
                .astype(str)
                .apply(_normalize_department_value)
                .eq(normalized_department)
            ]

    if machine_type != "All" and "Machine Type" in df.columns:
        if df["Machine Type"].astype(str).str.strip().ne("").any():
            df = df[df["Machine Type"].astype(str).str.strip().eq(machine_type)]

    if search_text:
        search_cols = [
            "Machine Name",
            "Machine ID",
            "Plant",
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
    key = _filters_cache_key(filters, apply_dates=False)
    return _latest_machine_rows_cached(key).copy()


@lru_cache(maxsize=64)
def _latest_machine_rows_cached(key):
    start_time = time.perf_counter()
    filters = _filters_from_cache_key(key)
    filters = _normalize_filters(filters)
    master = _machine_master_dataframe(filters.get("plant", "All"))
    if master.empty:
        _dashboard_timing("Machine Status Snapshot", start_time)
        return pd.DataFrame()

    latest = _latest_event_rows(filters)
    if latest.empty:
        master["Status"] = "Idle"
        master["Status Code"] = 0
        master["Reason"] = ""
        master["Process Name"] = ""
        if "Machine Type" not in master.columns:
            master["Machine Type"] = ""
        master["Status From"] = pd.NaT
        master["Updated At"] = pd.NaT
        master["Job ID"] = 0
        master["Actual Qty"] = 0
        master["Planned Qty"] = 0
        master["Interface Log"] = ""
        master["Plant"] = master["Department"]
        _dashboard_timing("Machine Status Snapshot", start_time)
        return master

    merged = master.merge(
        latest,
        on=["Machine ID"],
        how="left",
        suffixes=("", "_event"),
    )

    for col in ["Status", "Reason", "Process Name", "Interface Log", "Plant"]:
        if col in merged.columns:
            merged[col] = merged[col].fillna("")

    merged["Status"] = merged.get("Status", pd.Series(index=merged.index, dtype=object)).replace("", "Idle").fillna("Idle")
    merged["Status Code"] = pd.to_numeric(merged.get("Status Code", 0), errors="coerce").fillna(0)
    merged["Status From"] = pd.to_datetime(merged.get("Status From"), errors="coerce", utc=True)
    merged["Updated At"] = pd.to_datetime(merged.get("Updated At"), errors="coerce", utc=True)
    merged["Actual Qty"] = pd.to_numeric(merged.get("Actual Qty", 0), errors="coerce").fillna(0)
    merged["Planned Qty"] = pd.to_numeric(merged.get("Planned Qty", 0), errors="coerce").fillna(0)
    merged["Process Name"] = merged.get("Process Name", "").fillna("")
    event_machine_type = merged["Process Name"].apply(_pretty_process_name)
    merged["Machine Type"] = merged["Machine Type"].fillna("").astype(str).str.strip()
    merged["Machine Type"] = merged["Machine Type"].where(merged["Machine Type"].ne(""), event_machine_type)
    merged["Plant"] = merged["Department"]
    result = _apply_local_filters(merged, filters, apply_dates=False)
    _dashboard_timing("Machine Status Snapshot", start_time)
    return result


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
    key = _filters_cache_key(filters, apply_dates=True)
    return _downtime_events_cached(key).copy()


@lru_cache(maxsize=64)
def _downtime_events_cached(key):
    start_time = time.perf_counter()
    filters = _filters_from_cache_key(key)
    event_filters = dict(_normalize_filters(filters))
    event_filters["plant"] = "All"
    event_filters["department"] = "All"
    event_filters["machine_type"] = "All"
    df = _api_dataframe(event_filters, apply_dates=True)
    if df.empty:
        _dashboard_timing("Downtime Events", start_time)
        return pd.DataFrame()
    plant = _normalize_filters(filters).get("plant", "All")
    allowed_ids = set(_machine_master_dataframe(plant)["Machine ID"].tolist())
    if allowed_ids:
        df = df[df["Machine ID"].isin(allowed_ids)].copy()

    # Historical interruption events used by the Halt / Stop downtime chart.
    # Use raw event codes so merging current Stopped machines into Idle does not
    # erase the existing downtime-reason history.
    df = df[df["Status Code"].astype(int).isin(HALT_STOP_EVENT_CODES)].copy()
    result = _calculate_event_durations(df, filters)
    _dashboard_timing("Downtime Events", start_time)
    return result


def idle_events(filters=None):
    key = _filters_cache_key(filters, apply_dates=True)
    return _idle_events_cached(key).copy()


@lru_cache(maxsize=64)
def _idle_events_cached(key):
    start_time = time.perf_counter()
    filters = _filters_from_cache_key(key)
    event_filters = dict(_normalize_filters(filters))
    event_filters["plant"] = "All"
    event_filters["department"] = "All"
    event_filters["machine_type"] = "All"
    df = _api_dataframe(event_filters, apply_dates=True)
    if df.empty:
        _dashboard_timing("Idle Events", start_time)
        return pd.DataFrame()
    plant = _normalize_filters(filters).get("plant", "All")
    allowed_ids = set(_machine_master_dataframe(plant)["Machine ID"].tolist())
    if allowed_ids:
        df = df[df["Machine ID"].isin(allowed_ids)].copy()
    df = df[df["Status"] == "Idle"].copy()
    result = _calculate_event_durations(df, filters)
    _dashboard_timing("Idle Events", start_time)
    return result


def get_filter_options(plant="All"):
    df = _machine_master_dataframe(plant or "All")

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
            x for x in df["Department"].dropna().astype(str).str.strip().str.upper().unique().tolist()
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

    if "Status From" in df.columns:
        dates = pd.to_datetime(df["Status From"], errors="coerce", utc=True).dropna()
    else:
        dates = pd.Series(dtype="datetime64[ns, UTC]")
    min_date = dates.min().strftime("%Y-%m-%d") if not dates.empty else None
    max_date = dates.max().strftime("%Y-%m-%d") if not dates.empty else None

    return {
        "departments": departments,
        "machine_types": machine_types,
        "min_date": min_date,
        "max_date": max_date,
    }


def get_machine_types_by_department(department="All", plant="All"):
    df = _machine_master_dataframe(plant or "All")
    if df.empty or "Machine Type" not in df.columns:
        return ["All"]
    normalized_department = _normalize_department_value(department)
    if normalized_department != "All" and "Department" in df.columns:
        df = df[df["Department"].apply(_normalize_department_value).eq(normalized_department)]

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




def _operational_time_summary(filters=None):
    """
    Calculate real runtime and Halt/Stop downtime from event-log timestamps.

    Business rule currently confirmed for Availability:
        Availability % = Run Time / (Run Time + Downtime) * 100

    Idle time is deliberately excluded from this formula. It can be added later
    only if the business explicitly changes the Availability definition.
    """
    df = _api_dataframe(filters, apply_dates=True)
    master = _machine_master_dataframe(_normalize_filters(filters).get("plant", "All"))

    if df is None or df.empty or master.empty:
        return {
            "run_minutes": 0.0,
            "downtime_minutes": 0.0,
            "availability": None,
        }

    allowed_ids = set(master["Machine ID"].tolist())
    if allowed_ids:
        df = df[df["Machine ID"].isin(allowed_ids)].copy()

    df = df.dropna(subset=["Status From"]).copy()
    if df.empty:
        return {
            "run_minutes": 0.0,
            "downtime_minutes": 0.0,
            "availability": None,
        }

    # Keep event order machine-by-machine. The duration of each event state is
    # the time until the next event for that same machine. The current/latest
    # event runs until the selected range end (or current UTC time).
    df = df.sort_values(
        ["Machine ID", "Status From", "Updated At", "Job ID"],
        ascending=[True, True, True, True],
        na_position="last",
    )

    df["Next Time"] = df.groupby("Machine ID")["Status From"].shift(-1)
    reference_time = _get_reference_time(df, filters)
    df["Next Time"] = df["Next Time"].fillna(reference_time)

    df["Operational Duration Minutes"] = (
        (df["Next Time"] - df["Status From"]).dt.total_seconds() / 60.0
    )
    df["Operational Duration Minutes"] = pd.to_numeric(
        df["Operational Duration Minutes"], errors="coerce"
    ).fillna(0.0).clip(lower=0.0)

    run_minutes = float(
        df.loc[df["Status"].eq("Running"), "Operational Duration Minutes"].sum()
    )
    downtime_minutes = float(
        df.loc[df["Status"].eq("Halt / Stop"), "Operational Duration Minutes"].sum()
    )

    denominator = run_minutes + downtime_minutes
    availability = (
        round((run_minutes / denominator) * 100.0, 1)
        if denominator > 0
        else None
    )

    return {
        "run_minutes": run_minutes,
        "downtime_minutes": downtime_minutes,
        "availability": availability,
    }


def get_operational_kpis(filters=None):
    """
    Return the real operational KPI values used by the top cards.

    Performance:
        Total Actual / Total Planned * 100

    Availability:
        Run Time / (Run Time + Halt/Stop Downtime) * 100

    Quality:
        Temporary approved rule:
        Reject Qty = 0
        Good Qty = Total Produced - Reject Qty
        Quality % = Good Qty / Total Produced * 100

    OEE:
        Availability * Performance * Quality

    No demo/static KPI percentage is hardcoded here.
    """
    normalized = _normalize_filters(filters)

    data = fetch_operational_kpis(
        normalized.get("start_date"),
        normalized.get("end_date"),
        normalized.get("plant"),
    )

    return {
        "performance": data.get(
            "performance_pct"
        ),
        "availability": data.get(
            "availability_pct"
        ),
        "quality": data.get(
            "quality_pct"
        ),
        "oee": data.get(
            "oee_pct"
        ),

        "planned_qty": float(
            data.get(
                "total_planned_qty",
                0.0,
            )
            or 0.0
        ),
        "actual_qty": float(
            data.get(
                "total_actual_qty",
                0.0,
            )
            or 0.0
        ),
        "reject_qty": float(
            data.get(
                "reject_qty",
                0.0,
            )
            or 0.0
        ),
        "good_qty": float(
            data.get(
                "good_qty",
                0.0,
            )
            or 0.0
        ),
        "run_minutes": float(
            data.get(
                "running_minutes",
                0.0,
            )
            or 0.0
        ),
        "downtime_minutes": float(
            data.get(
                "downtime_minutes",
                0.0,
            )
            or 0.0
        ),
    }

def machine_status_trend(filters=None, days=7):
    """
    Build a true daily status snapshot. For each day we take the latest event
    at or before that day's end for every machine, then calculate the status
    distribution. This avoids duplicate-event distortion and keeps daily
    percentages aligned to the machine population.
    """
    filters = _normalize_filters(filters)
    master = _machine_master_dataframe(filters.get("plant", "All"))
    event_filters = dict(filters)
    event_filters["plant"] = "All"
    event_filters["department"] = "All"
    event_filters["machine_type"] = "All"
    df = _api_dataframe(event_filters, apply_dates=False)
    empty = pd.DataFrame(columns=["Date", *ALLOWED_STATUSES])

    if master.empty:
        return empty

    if df.empty or "Status From" not in df.columns:
        return pd.DataFrame(
            [{"Date": pd.Timestamp.now(tz="UTC").strftime("%d %b"), "Running": 0, "Idle": 100.0, "Halt / Stop": 0.0}],
            columns=["Date", *ALLOWED_STATUSES],
        )

    df = df.dropna(subset=["Status From"]).copy()
    if df.empty:
        return empty
    df = df[df["Machine ID"].isin(master["Machine ID"])].copy()
    if df.empty:
        return pd.DataFrame(
            [{"Date": pd.Timestamp.now(tz="UTC").strftime("%d %b"), "Running": 0, "Idle": 100.0, "Halt / Stop": 0.0}],
            columns=["Date", *ALLOWED_STATUSES],
        )

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

        total = int(master["Machine ID"].nunique())
        counts = snapshot["Status"].value_counts()
        row = {"Date": day.strftime("%d %b")}
        for status in ALLOWED_STATUSES:
            value = float(counts.get(status, 0))
            if status == "Idle":
                value += max(total - int(snapshot["Machine ID"].nunique()), 0)
            row[status] = round(value / total * 100, 1) if total else 0
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


def halt_stop_machine_detail(filters=None):
    """Return currently Halt / Stop machines, longest interruption first."""
    df = machines_data(filters)
    if df.empty:
        return pd.DataFrame(
            columns=["Machine Name", "Since From", "Duration", "Reason"]
        )

    df = df[df["Status"] == "Halt / Stop"].copy()
    if df.empty:
        return pd.DataFrame(
            columns=["Machine Name", "Since From", "Duration", "Reason"]
        )

    df = _add_status_duration(df, filters)
    df = df.sort_values("Duration Minutes", ascending=False, na_position="last")
    df = df.rename(columns={"Status From": "Since From"})

    if "Reason" not in df.columns:
        df["Reason"] = ""
    df["Reason"] = df["Reason"].fillna("").astype(str).str.strip()
    df.loc[df["Reason"].eq(""), "Reason"] = "Not specified"

    return df[[c for c in ["Machine Name", "Since From", "Duration", "Reason"] if c in df.columns]]


def machine_wise_efficiency(filters=None):
    """
    Machine-wise efficiency from real job/workbench + event-log data.

    Formula:
        Efficiency % = Machine Actual Production / Machine Planned Production * 100

    Data sources:
        Planned Qty = cableflow.job_workbench_job.total_qty
        Actual Qty  = MAX(cableflow.event_logs.actual_executed_qty) per job

    The current dashboard filters are preserved. Date range is applied in the
    database query; plant/department/machine-type/global-search are applied by
    matching the resulting machines against the already-filtered dashboard data.
    """
    normalized = _normalize_filters(filters)

    records = fetch_machine_efficiency(
        normalized.get("start_date"),
        normalized.get("end_date"),
    )

    columns = [
        "Machine ID",
        "Machine Name",
        "Planned Qty",
        "Actual Qty",
        "Efficiency %",
    ]

    if not records:
        return pd.DataFrame(columns=columns)

    result = pd.DataFrame(records)
    if result.empty:
        return pd.DataFrame(columns=columns)

    rename_map = {
        "machine_id": "Machine ID",
        "machine_name": "Machine Name",
        "planned_qty": "Planned Qty",
        "actual_qty": "Actual Qty",
        "efficiency_pct": "Efficiency %",
    }
    result = result.rename(columns=rename_map)

    for col in ["Machine ID", "Planned Qty", "Actual Qty", "Efficiency %"]:
        if col not in result.columns:
            result[col] = 0
        result[col] = pd.to_numeric(result[col], errors="coerce")

    if "Machine Name" not in result.columns:
        result["Machine Name"] = ""

    result["Machine Name"] = result["Machine Name"].fillna("").astype(str).str.strip()
    result = result[result["Machine Name"].ne("")].copy()
    result = result[result["Planned Qty"].fillna(0) > 0].copy()
    result = result.dropna(subset=["Efficiency %"])

    # Apply Plant / Department / Machine Type / global search by using the same
    # filtered machine population that powers KPI cards and machine tables.
    machine_scope = machines_data(filters)
    if machine_scope is not None and not machine_scope.empty:
        allowed_names = set(
            machine_scope["Machine Name"]
            .fillna("")
            .astype(str)
            .str.strip()
            .str.casefold()
            .loc[lambda x: x.ne("")]
            .tolist()
        )

        if allowed_names:
            result = result[
                result["Machine Name"].str.casefold().isin(allowed_names)
            ].copy()

    # Global search can also directly match an efficiency machine name even when
    # current machine metadata is incomplete.
    search_text = str(normalized.get("search_text") or "").strip()
    if search_text:
        result = result[
            result["Machine Name"].str.contains(
                search_text,
                case=False,
                na=False,
                regex=False,
            )
        ].copy()

    if result.empty:
        return pd.DataFrame(columns=columns)

    result["Efficiency %"] = result["Efficiency %"].round(2)
    result = result.sort_values(
        ["Efficiency %", "Machine Name"],
        ascending=[False, True],
        kind="stable",
    )

    return result[columns].reset_index(drop=True)


def department_summary(filters=None):
    df = machines_data(filters)
    columns = ["Department", "Total", "Running", "Idle", "Halt / Stop"]
    if df.empty:
        return pd.DataFrame(columns=columns)

    dept_series = df["Department"].fillna("").astype(str).str.strip()
    df = df.copy()
    df["Department"] = dept_series.where(dept_series.ne(""), "All Machines")

    rows = []
    for department, group in df.groupby("Department"):
        counts = group["Status"].value_counts()
        rows.append(
            {
                "Department": department,
                "Total": int(group["Machine ID"].nunique()),
                "Running": int(counts.get("Running", 0)),
                "Idle": int(counts.get("Idle", 0)),
                "Halt / Stop": int(counts.get("Halt / Stop", 0)),
            }
        )

    result = pd.DataFrame(rows, columns=columns)
    if not result.empty:
        result = result.sort_values(["Department"], kind="stable").reset_index(drop=True)
    return result

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
    normalized = _normalize_filters(filters)
    master = _machine_master_dataframe(normalized.get("plant", "All"))
    event_filters = dict(normalized)
    event_filters["plant"] = "All"
    event_filters["department"] = "All"
    event_filters["machine_type"] = "All"
    df = _api_dataframe(event_filters, apply_dates=apply_dates)
    if df.empty:
        return pd.DataFrame()

    if not master.empty:
        df = df[df["Machine ID"].isin(master["Machine ID"])].copy()
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



# =========================================================
# PRODUCTION GROUP SUMMARY
# =========================================================

def production_group_summary(filters=None):
    """
    Return real Target/Actual production grouped into:
      1. Extruders
      2. Bunchers & Braiders
      3. Other Lines

    The function deliberately uses only quantities that are proven to be Tons
    by _production_event_dataframe(). It does not relabel metres/units as Tons.

    Group classification uses the real Process Name + Machine Name metadata:
      Extruders:
        extrud*, monosil, ccv
      Bunchers & Braiders:
        bunch*, braid*, braider*, stranding/strander
      Other Lines:
        everything else

    Target is counted once per Job ID.
    Actual is the real production tonnage delta already calculated per event.
    """

    columns = [
        "Group",
        "Target",
        "Actual",
        "Unit",
    ]

    df = _production_event_dataframe(
        filters,
        apply_dates=True,
    )

    group_order = [
        "Extruders",
        "Bunchers & Braiders",
        "Other Lines",
    ]

    if df is None or df.empty:
        return pd.DataFrame(
            [
                {
                    "Group": group,
                    "Target": None,
                    "Actual": None,
                    "Unit": "Tons",
                }
                for group in group_order
            ],
            columns=columns,
        )

    def classify_group(row):
        text = " ".join(
            [
                str(row.get("Process Name") or ""),
                str(row.get("Machine Name") or ""),
                str(row.get("Machine Type") or ""),
            ]
        ).casefold()

        extruder_terms = (
            "extrud",
            "monosil",
            "ccv",
        )

        bunch_braid_terms = (
            "bunch",
            "braid",
            "braider",
            "stranding",
            "strander",
        )

        if any(term in text for term in extruder_terms):
            return "Extruders"

        if any(term in text for term in bunch_braid_terms):
            return "Bunchers & Braiders"

        return "Other Lines"

    df = df.copy()
    df["Production Group"] = df.apply(
        classify_group,
        axis=1,
    )

    rows = []

    for group_name in group_order:
        group = df[
            df["Production Group"].eq(group_name)
        ].copy()

        target = None
        actual = None

        if not group.empty:
            # One target per job so repeated event rows do not inflate plan.
            valid_target = group.dropna(
                subset=["Target Tons Source"]
            ).copy()
            valid_target = valid_target[
                pd.to_numeric(
                    valid_target["Target Tons Source"],
                    errors="coerce",
                ).fillna(0) > 0
            ]

            if not valid_target.empty:
                per_job_target = (
                    valid_target
                    .sort_values("Status From")
                    .drop_duplicates(
                        subset=["Job ID"],
                        keep="last",
                    )
                )

                target_values = pd.to_numeric(
                    per_job_target["Target Tons Source"],
                    errors="coerce",
                ).dropna()

                if not target_values.empty:
                    target = round(
                        float(target_values.sum()),
                        2,
                    )

            actual_values = pd.to_numeric(
                group.get(
                    "Production Tons Delta",
                    pd.Series(dtype=float),
                ),
                errors="coerce",
            ).dropna()

            if not actual_values.empty:
                actual = round(
                    float(
                        actual_values.clip(lower=0).sum()
                    ),
                    2,
                )

        rows.append(
            {
                "Group": group_name,
                "Target": target,
                "Actual": actual,
                "Unit": "Tons",
            }
        )

    return pd.DataFrame(
        rows,
        columns=columns,
    )


def production_group_summary(filters=None):
    """Return authoritative production groups in meters for the selected plant."""
    filters = filters or {}
    data = fetch_production_groups(
        filters.get("start_date"),
        filters.get("end_date"),
        filters.get("plant") or "All",
    )
    rows = []
    for group in ("Extruders", "Bunchers & Braiders", "Other Lines"):
        target, actual = data.get(group, (None, None))
        target = None if pd.isna(target) else target
        actual = None if pd.isna(actual) else actual
        rows.append({"Group": group, "Target": target, "Actual": actual, "Unit": "Meters"})
    return pd.DataFrame(rows, columns=["Group", "Target", "Actual", "Unit"])


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

    filters = _normalize_filters(filters)
    selected_plant = filters.get("plant", "All")
    rows = [
        row for row in rows
        if _plant_matches_work_center(selected_plant, row.get("work_center", ""))
    ]

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

