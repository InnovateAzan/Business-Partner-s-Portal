from functools import lru_cache
from datetime import datetime, date, timedelta

import pandas as pd
import psycopg2
import requests

from services.config import (
    DB_HOST,
    DB_PORT,
    DB_NAME,
    DB_USER,
    DB_PASSWORD,
    PRODUCTION_TARGET_API,
)


DB_SCHEMA = "cableflow"
EVENT_LOG_TABLE = "event_logs"


# ============================================================
# CACHE
# ============================================================

def clear_api_cache():
    """
    Clear all cached dashboard/API data.
    Call this when Refresh button is clicked.
    """
    fetch_dashboard_metadata.cache_clear()
    fetch_production_targets.cache_clear()
    fetch_machine_efficiency.cache_clear()
    fetch_production_groups.cache_clear()
    fetch_operational_kpis.cache_clear()
    _get_event_log_columns.cache_clear()
    _get_job_metadata_source.cache_clear()


# ============================================================
# PRODUCTION TARGET API
# ============================================================

@lru_cache(maxsize=1)
def fetch_production_targets():
    """
    Fetch real Production Overview values from the configured API.

    Expected response example:
        [
            {"work_center": "GCF", "target": 0, "actual": 30},
            {"work_center": "PCF", "target": 0, "actual": 55},
        ]
    """
    if not PRODUCTION_TARGET_API:
        return []

    try:
        response = requests.get(
            PRODUCTION_TARGET_API,
            timeout=10,
        )
        response.raise_for_status()

        payload = response.json()

        if not isinstance(payload, list):
            print("Production target API returned a non-list response.")
            return []

        cleaned = []

        for row in payload:
            if not isinstance(row, dict):
                continue

            work_center = str(
                row.get("work_center") or ""
            ).strip()

            try:
                target = float(
                    row.get("target", 0) or 0
                )
            except (TypeError, ValueError):
                target = 0.0

            try:
                actual = float(
                    row.get("actual", 0) or 0
                )
            except (TypeError, ValueError):
                actual = 0.0

            cleaned.append(
                {
                    "work_center": work_center or "Unknown",
                    "target": target,
                    "actual": actual,
                }
            )

        return cleaned

    except requests.RequestException as error:
        print(
            f"Production target API request error: {error}"
        )
        return []

    except ValueError as error:
        print(
            f"Production target API JSON error: {error}"
        )
        return []

    except Exception as error:
        print(
            f"Production target API error: {error}"
        )
        return []


# ============================================================
# DATABASE CONNECTION
# ============================================================

def _get_connection():
    """
    Create PostgreSQL connection.
    """
    return psycopg2.connect(
        host=DB_HOST,
        port=DB_PORT,
        dbname=DB_NAME,
        user=DB_USER,
        password=DB_PASSWORD,
        connect_timeout=10,
    )


# ============================================================
# DATE HELPERS
# ============================================================

def _normalize_start_date(value):
    """
    Convert a selected start date safely.
    """
    if value is None or value == "":
        return None

    parsed = pd.to_datetime(
        value,
        errors="coerce",
    )

    if pd.isna(parsed):
        return None

    return parsed


def _normalize_end_date(value):
    """
    Convert end date to an EXCLUSIVE upper boundary.

    Example:
        selected end date = 2026-08-21

    Query becomes:
        date < 2026-08-22

    This includes the complete selected end day.
    """
    if value is None or value == "":
        return None

    parsed = pd.to_datetime(
        value,
        errors="coerce",
    )

    if pd.isna(parsed):
        return None

    if (
        parsed.hour == 0
        and parsed.minute == 0
        and parsed.second == 0
        and parsed.microsecond == 0
    ):
        parsed = parsed + pd.Timedelta(days=1)

    return parsed


# ============================================================
# MACHINE-WISE EFFICIENCY
# ============================================================

@lru_cache(maxsize=64)
def fetch_machine_efficiency(
    created_from=None,
    created_to=None,
):
    """
    Return machine-wise planned quantity, actual executed quantity
    and efficiency.

    Business formula:
        Efficiency % =
            Machine Actual Production
            / Machine Planned Production
            * 100

    Data sources:
    - Planned Qty:
        cableflow.job_workbench_job.total_qty
    - Actual Qty:
        MAX(cableflow.event_logs.actual_executed_qty)
        per job

    Important:
    event_logs contains multiple progress snapshots for the same job,
    therefore actual production is MAX(actual_executed_qty) per job,
    not SUM(event rows).
    """

    conn = None

    try:
        from_dt = _normalize_start_date(
            created_from
        )

        to_dt = _normalize_end_date(
            created_to
        )

        where_parts = [
            "j.machine_code IS NOT NULL",
            "BTRIM(j.machine_code) <> ''",
        ]

        params = []

        if (
            from_dt is not None
            and not pd.isna(from_dt)
        ):
            where_parts.append(
                "j.planning_date >= %s"
            )
            params.append(
                from_dt.to_pydatetime()
            )

        if (
            to_dt is not None
            and not pd.isna(to_dt)
        ):
            where_parts.append(
                "j.planning_date < %s"
            )
            params.append(
                to_dt.to_pydatetime()
            )

        where_sql = " AND ".join(
            where_parts
        )

        query = f"""
            WITH actual_per_job AS (
                SELECT
                    el.job_id,
                    MAX(el.machine_id) AS machine_id,
                    MAX(
                        COALESCE(
                            el.actual_executed_qty,
                            0
                        )
                    ) AS actual_qty
                FROM {DB_SCHEMA}.event_logs el
                WHERE el.job_id IS NOT NULL
                GROUP BY el.job_id
            )

            SELECT
                MAX(a.machine_id) AS machine_id,

                COALESCE(
                    NULLIF(
                        BTRIM(j.machine_code),
                        ''
                    ),
                    MAX(mm.machine_name)
                ) AS machine_name,

                SUM(
                    COALESCE(
                        j.total_qty,
                        0
                    )
                )::numeric AS planned_qty,

                SUM(
                    COALESCE(
                        a.actual_qty,
                        0
                    )
                )::numeric AS actual_qty,

                CASE
                    WHEN SUM(
                        COALESCE(
                            j.total_qty,
                            0
                        )
                    ) > 0

                    THEN ROUND(
                        (
                            SUM(
                                COALESCE(
                                    a.actual_qty,
                                    0
                                )
                            )
                            /
                            SUM(
                                COALESCE(
                                    j.total_qty,
                                    0
                                )
                            )
                        ) * 100,
                        2
                    )

                    ELSE NULL
                END AS efficiency_pct

            FROM {DB_SCHEMA}.job_workbench_job j

            LEFT JOIN actual_per_job a
                ON a.job_id = j.id

            LEFT JOIN {DB_SCHEMA}.job_machines_master mm
                ON mm.id = a.machine_id

            WHERE {where_sql}

            GROUP BY
                NULLIF(
                    BTRIM(j.machine_code),
                    ''
                )

            HAVING
                SUM(
                    COALESCE(
                        j.total_qty,
                        0
                    )
                ) > 0

            ORDER BY
                efficiency_pct DESC NULLS LAST,
                machine_name
        """

        conn = _get_connection()

        df = pd.read_sql_query(
            query,
            conn,
            params=params,
        )

        # Useful lightweight diagnostics.
        print(
            f"Machine efficiency rows fetched: {len(df)}"
        )

        if not df.empty:
            print(
                df[
                    [
                        "machine_name",
                        "planned_qty",
                        "actual_qty",
                        "efficiency_pct",
                    ]
                ]
                .head(10)
                .to_string(index=False)
            )

        if df.empty:
            return []

        numeric_columns = [
            "machine_id",
            "planned_qty",
            "actual_qty",
            "efficiency_pct",
        ]

        for column in numeric_columns:
            if column in df.columns:
                df[column] = pd.to_numeric(
                    df[column],
                    errors="coerce",
                )

        if "machine_name" in df.columns:
            df["machine_name"] = (
                df["machine_name"]
                .fillna("")
                .astype(str)
                .str.strip()
            )

        df = df[
            df["machine_name"] != ""
        ].copy()

        return df.to_dict(
            orient="records"
        )

    except Exception as error:
        import traceback

        print("=" * 60)
        print("MACHINE EFFICIENCY QUERY ERROR")
        print(repr(error))
        print("=" * 60)

        traceback.print_exc()

        return []

    finally:
        if conn:
            conn.close()



# ============================================================
# VERIFIED OPERATIONAL KPI SUMMARY
# ============================================================

@lru_cache(maxsize=64)
def fetch_production_groups(created_from=None, created_to=None, plant="All"):
    """Return target/actual meters grouped by authoritative machine type."""
    conn = None
    try:
        selected_plant = str(plant or "All").strip().upper()
        filter_by_plant = selected_plant not in {"ALL", "ALL PLANTS", "ALLPLANTS", ""}
        where_parts = [
            "j.id IS NOT NULL",
            "NULLIF(TRIM(mm.machine_type), '') IS NOT NULL",
        ]
        params = []
        start_date = _normalize_start_date(created_from)
        end_date = _normalize_end_date(created_to)
        if start_date is not None and not pd.isna(start_date):
            where_parts.append("j.planning_date >= %s")
            params.append(start_date.to_pydatetime())
        if end_date is not None and not pd.isna(end_date):
            where_parts.append("j.planning_date < %s")
            params.append(end_date.to_pydatetime())
        if filter_by_plant:
            where_parts.append("TRIM(UPPER(mm.department)) = %s")
            params.append(selected_plant)
        query = f"""
            WITH actual_per_job AS (
                SELECT el.job_id, el.machine_id, MAX(el.actual_executed_qty) AS actual_qty
                FROM {DB_SCHEMA}.event_logs el
                WHERE el.job_id IS NOT NULL AND el.machine_id IS NOT NULL
                GROUP BY el.job_id, el.machine_id
            )
            SELECT CASE
                WHEN TRIM(UPPER(mm.machine_type)) LIKE '%%EXTRUD%%'
                    THEN 'Extruders'
                WHEN TRIM(UPPER(mm.machine_type)) LIKE '%%BUNCH%%'
                  OR TRIM(UPPER(mm.machine_type)) LIKE '%%BRAID%%'
                  OR TRIM(UPPER(mm.machine_type)) LIKE '%%STRAND%%'
                    THEN 'Bunchers & Braiders'
                ELSE 'Other Lines' END AS group_name,
                SUM(j.total_qty)::numeric AS target,
                SUM(COALESCE(a.actual_qty, 0))::numeric AS actual
            FROM {DB_SCHEMA}.job_workbench_job j
            JOIN {DB_SCHEMA}.job_machines_master mm ON mm.id = j.machine_id
            LEFT JOIN actual_per_job a
              ON a.job_id = j.id AND a.machine_id = mm.id
            WHERE {' AND '.join(where_parts)}
            GROUP BY 1
        """
        conn = _get_connection()
        df = pd.read_sql_query(query, conn, params=params)
        result = {name: (None, None) for name in ("Extruders", "Bunchers & Braiders", "Other Lines")}
        for _, row in df.iterrows():
            target = pd.to_numeric(row.get("target"), errors="coerce")
            actual = pd.to_numeric(row.get("actual"), errors="coerce")
            result[row["group_name"]] = (None if pd.isna(target) else round(float(target), 2), None if pd.isna(actual) else round(float(actual), 2))
        return result
    except Exception:
        import traceback
        traceback.print_exc()
        return {}
    finally:
        if conn:
            conn.close()

@lru_cache(maxsize=64)
def fetch_operational_kpis(
    created_from=None,
    created_to=None,
    plant="All",
):
    """
    Return verified Performance and Availability KPI values.

    PERFORMANCE
    -----------
    Planned:
        SUM(cableflow.job_workbench_job.total_qty)

    Actual:
        MAX(cableflow.event_logs.actual_executed_qty) per job,
        then SUM across jobs.

    Formula:
        Performance % = Total Actual / Total Planned * 100

    AVAILABILITY
    ------------
    Events are ordered by job + machine + created_at.
    Each event owns the duration until the next event.

    Running:
        event_reason = START JOB

    Downtime:
        event_reason NOT IN (START JOB, JOB COMPLETE, COMPLETE)

    Formula:
        Availability % =
            Running Minutes
            / (Running Minutes + Downtime Minutes)
            * 100

    QUALITY / OEE
    -------------
    Quality is intentionally not calculated until a reliable
    Good / Reject / Scrap quantity source is mapped.
    OEE therefore remains unavailable until Quality is real.

    Current filter scope:
    - Date range is supported.
    - Plant / department / machine-type filtering is intentionally
      NOT guessed here until their authoritative DB mapping is confirmed.
    """

    conn = None

    try:
        selected_plant = str(plant or "All").strip().upper()
        filter_by_plant = selected_plant not in {"ALL", "ALL PLANTS", "ALLPLANTS", ""}
        start_date = _normalize_start_date(created_from)
        end_date = _normalize_end_date(created_to)

        # ----------------------------------------------------
        # PERFORMANCE
        # ----------------------------------------------------
        performance_where = [
            "COALESCE(j.total_qty, 0) > 0",
        ]
        performance_params = []

        if start_date is not None and not pd.isna(start_date):
            performance_where.append(
                "j.planning_date >= %s"
            )
            performance_params.append(
                start_date.to_pydatetime()
            )

        if end_date is not None and not pd.isna(end_date):
            performance_where.append(
                "j.planning_date < %s"
            )
            performance_params.append(
                end_date.to_pydatetime()
            )

        performance_query = f"""
            WITH actual_per_job AS (
                SELECT
                    el.job_id,
                    MAX(el.machine_id) AS machine_id,
                    MAX(
                        COALESCE(
                            el.actual_executed_qty,
                            0
                        )
                    ) AS actual_qty
                FROM {DB_SCHEMA}.event_logs el
                WHERE el.job_id IS NOT NULL
                GROUP BY el.job_id
            )
            SELECT
                COALESCE(
                    SUM(
                        COALESCE(
                            j.total_qty,
                            0
                        )
                    ),
                    0
                )::numeric AS total_planned_qty,

                COALESCE(
                    SUM(
                        COALESCE(
                            a.actual_qty,
                            0
                        )
                    ),
                    0
                )::numeric AS total_actual_qty,

                CASE
                    WHEN COALESCE(
                        SUM(
                            COALESCE(
                                j.total_qty,
                                0
                            )
                        ),
                        0
                    ) > 0
                    THEN ROUND(
                        (
                            COALESCE(
                                SUM(
                                    COALESCE(
                                        a.actual_qty,
                                        0
                                    )
                                ),
                                0
                            )
                            /
                            SUM(
                                COALESCE(
                                    j.total_qty,
                                    0
                                )
                            )
                        ) * 100,
                        2
                    )
                    ELSE NULL
                END AS performance_pct

            FROM {DB_SCHEMA}.job_workbench_job j

            LEFT JOIN actual_per_job a
                ON a.job_id = j.id

            LEFT JOIN {DB_SCHEMA}.job_machines_master mm
                ON mm.id = a.machine_id

            WHERE {" AND ".join(performance_where)}
            {"AND TRIM(UPPER(mm.department)) = %s" if filter_by_plant else ""}
        """

        performance_params = list(performance_params)
        if filter_by_plant:
            performance_params.append(selected_plant)

        conn = _get_connection()

        performance_df = pd.read_sql_query(
            performance_query,
            conn,
            params=performance_params,
        )

        total_planned_qty = 0.0
        total_actual_qty = 0.0
        performance_pct = None

        if not performance_df.empty:
            row = performance_df.iloc[0]

            planned_raw = row.get("total_planned_qty")
            actual_raw = row.get("total_actual_qty")
            performance_raw = row.get("performance_pct")

            if pd.notna(planned_raw):
                total_planned_qty = float(planned_raw)

            if pd.notna(actual_raw):
                total_actual_qty = float(actual_raw)

            if pd.notna(performance_raw):
                performance_pct = float(performance_raw)

        # ----------------------------------------------------
        # AVAILABILITY
        # ----------------------------------------------------
        availability_where = [
            "el.created_at IS NOT NULL",
        ]
        availability_params = []

        if start_date is not None and not pd.isna(start_date):
            availability_where.append(
                "el.created_at >= %s"
            )
            availability_params.append(
                start_date.to_pydatetime()
            )

        if end_date is not None and not pd.isna(end_date):
            availability_where.append(
                "el.created_at < %s"
            )
            availability_params.append(
                end_date.to_pydatetime()
            )

        availability_query = f"""
            WITH event_sequence AS (
                SELECT
                    el.job_id,
                    el.machine_id,
                    el.status,
                    el.event_reason,
                    el.created_at,
                    mm.department AS department,

                    LEAD(
                        el.created_at
                    ) OVER (
                        PARTITION BY
                            el.job_id,
                            el.machine_id
                        ORDER BY
                            el.created_at
                    ) AS next_event_time

                FROM {DB_SCHEMA}.event_logs el
                LEFT JOIN {DB_SCHEMA}.job_machines_master mm
                    ON mm.id = el.machine_id

                WHERE {" AND ".join(availability_where)}
                {"AND TRIM(UPPER(mm.department)) = %s" if filter_by_plant else ""}
            ),

            durations AS (
                SELECT
                    job_id,
                    machine_id,
                    status,
                    event_reason,
                    created_at,
                    next_event_time,

                    EXTRACT(
                        EPOCH FROM (
                            next_event_time - created_at
                        )
                    ) / 60.0 AS duration_minutes

                FROM event_sequence

                WHERE next_event_time IS NOT NULL
            ),

            totals AS (
                SELECT
                    SUM(
                        CASE
                            WHEN UPPER(
                                BTRIM(
                                    COALESCE(
                                        event_reason,
                                        ''
                                    )
                                )
                            ) = 'START JOB'
                            THEN GREATEST(
                                duration_minutes,
                                0
                            )
                            ELSE 0
                        END
                    ) AS running_minutes,

                    SUM(
                        CASE
                            WHEN UPPER(
                                BTRIM(
                                    COALESCE(
                                        event_reason,
                                        ''
                                    )
                                )
                            ) NOT IN (
                                'START JOB',
                                'JOB COMPLETE',
                                'COMPLETE'
                            )
                            THEN GREATEST(
                                duration_minutes,
                                0
                            )
                            ELSE 0
                        END
                    ) AS downtime_minutes

                FROM durations
            )

            SELECT
                ROUND(
                    COALESCE(
                        running_minutes,
                        0
                    )::numeric,
                    2
                ) AS running_minutes,

                ROUND(
                    COALESCE(
                        downtime_minutes,
                        0
                    )::numeric,
                    2
                ) AS downtime_minutes,

                CASE
                    WHEN (
                        COALESCE(
                            running_minutes,
                            0
                        )
                        +
                        COALESCE(
                            downtime_minutes,
                            0
                        )
                    ) > 0
                    THEN ROUND(
                        (
                            COALESCE(
                                running_minutes,
                                0
                            )
                            /
                            (
                                COALESCE(
                                    running_minutes,
                                    0
                                )
                                +
                                COALESCE(
                                    downtime_minutes,
                                    0
                                )
                            )
                        ) * 100,
                        2
                    )
                    ELSE NULL
                END AS availability_pct

            FROM totals
        """

        availability_df = pd.read_sql_query(
            availability_query,
            conn,
            params=availability_params + ([selected_plant] if filter_by_plant else []),
        )

        running_minutes = 0.0
        downtime_minutes = 0.0
        availability_pct = None

        if not availability_df.empty:
            row = availability_df.iloc[0]

            running_raw = row.get("running_minutes")
            downtime_raw = row.get("downtime_minutes")
            availability_raw = row.get("availability_pct")

            if pd.notna(running_raw):
                running_minutes = float(running_raw)

            if pd.notna(downtime_raw):
                downtime_minutes = float(downtime_raw)

            if pd.notna(availability_raw):
                availability_pct = float(
                    availability_raw
                )

        print(
            "Operational KPI -> "
            f"Planned: {total_planned_qty:.2f}, "
            f"Actual: {total_actual_qty:.2f}, "
            f"Performance: {performance_pct}, "
            f"Run Min: {running_minutes:.2f}, "
            f"Downtime Min: {downtime_minutes:.2f}, "
            f"Availability: {availability_pct}"
        )

        # ----------------------------------------------------
        # QUALITY
        # ----------------------------------------------------
        # Temporary business rule confirmed for the dashboard:
        # until a real reject source is connected, Reject Qty = 0.
        #
        # Good Qty = Total Produced - Reject Qty
        # Quality % = Good Qty / Total Produced * 100
        total_produced_qty = float(total_actual_qty or 0.0)
        reject_qty = 0.0
        good_qty = max(
            total_produced_qty - reject_qty,
            0.0,
        )

        if total_produced_qty > 0:
            quality_pct = round(
                (
                    good_qty
                    / total_produced_qty
                ) * 100,
                2,
            )
        else:
            quality_pct = 0.0

        # ----------------------------------------------------
        # OEE
        # ----------------------------------------------------
        # OEE % =
        #   (Availability / 100)
        #   * (Performance / 100)
        #   * (Quality / 100)
        #   * 100
        if (
            availability_pct is not None
            and performance_pct is not None
            and quality_pct is not None
        ):
            oee_pct = round(
                (
                    float(availability_pct) / 100.0
                )
                * (
                    float(performance_pct) / 100.0
                )
                * (
                    float(quality_pct) / 100.0
                )
                * 100.0,
                2,
            )
        else:
            oee_pct = None

        print(
            "Quality/OEE -> "
            f"Produced: {total_produced_qty:.2f}, "
            f"Reject: {reject_qty:.2f}, "
            f"Good: {good_qty:.2f}, "
            f"Quality: {quality_pct}, "
            f"OEE: {oee_pct}"
        )
        print("Operational KPI Scope ->")
        print(f"Plant: {selected_plant if filter_by_plant else 'All'}")
        print(f"Performance: {performance_pct}")
        print(f"Availability: {availability_pct}")
        print(f"Quality: {quality_pct}")
        print(f"OEE: {oee_pct}")

        return {
            "total_planned_qty": total_planned_qty,
            "total_actual_qty": total_actual_qty,
            "performance_pct": performance_pct,
            "running_minutes": running_minutes,
            "downtime_minutes": downtime_minutes,
            "availability_pct": availability_pct,
            "reject_qty": reject_qty,
            "good_qty": good_qty,
            "quality_pct": quality_pct,
            "oee_pct": oee_pct,
        }

    except Exception as error:
        import traceback

        print("=" * 60)
        print("OPERATIONAL KPI QUERY ERROR")
        print(repr(error))
        print("=" * 60)
        traceback.print_exc()

        return {
            "total_planned_qty": 0.0,
            "total_actual_qty": 0.0,
            "performance_pct": None,
            "running_minutes": 0.0,
            "downtime_minutes": 0.0,
            "availability_pct": None,
            "reject_qty": 0.0,
            "good_qty": 0.0,
            "quality_pct": None,
            "oee_pct": None,
        }

    finally:
        if conn:
            conn.close()


# ============================================================
# DATABASE COLUMN CHECK
# ============================================================

@lru_cache(maxsize=1)
def _get_event_log_columns():
    """
    Read actual event_logs columns from PostgreSQL.

    This prevents the complete dashboard query from failing if
    an optional column does not exist.
    """

    conn = None

    try:
        conn = _get_connection()

        with conn.cursor() as cursor:
            cursor.execute(
                """
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = %s
                  AND table_name = %s
                ORDER BY ordinal_position
                """,
                (
                    DB_SCHEMA,
                    EVENT_LOG_TABLE,
                ),
            )

            rows = cursor.fetchall()

        return {
            row[0]
            for row in rows
        }

    except Exception as error:
        print(
            f"Error reading event_logs columns: {error}"
        )
        return set()

    finally:
        if conn:
            conn.close()


@lru_cache(maxsize=1)
def _get_job_metadata_source():
    """
    Discover an optional job metadata table that can provide
    department/process/plant metadata for event_logs.job_id.
    """

    conn = None

    try:
        conn = _get_connection()

        with conn.cursor() as cursor:
            cursor.execute(
                """
                SELECT
                    table_name,
                    column_name
                FROM information_schema.columns
                WHERE table_schema = %s
                ORDER BY
                    table_name,
                    ordinal_position
                """,
                (DB_SCHEMA,),
            )

            rows = cursor.fetchall()

        table_columns = {}

        for table_name, column_name in rows:
            table_columns.setdefault(
                table_name,
                set(),
            ).add(
                column_name
            )

        department_candidates = [
            "department",
            "dept",
            "department_name",
        ]

        process_candidates = [
            "process",
            "process_name",
            "operation",
            "operation_name",
        ]

        plant_candidates = [
            "plant",
            "plant_name",
            "work_center",
            "workcentre",
            "work_center_name",
            "value_stream_group",
            "value_stream",
            "site",
            "site_name",
        ]

        target_candidates = [
            "target_tons",
            "planned_tons",
            "plan_tons",
            "planned_tonnage",
            "target_tonnage",
            "planned_qty",
            "plan_qty",
            "planned_quantity",
            "target_qty",
            "target_quantity",
            "total_qty",
        ]

        uom_candidates = [
            "uom",
            "uom_code",
            "unit_of_measure",
            "primary_uom_code",
        ]

        join_candidates = [
            "id",
            "job_id",
        ]

        ordered_tables = sorted(
            table_columns,
            key=lambda name: (
                0
                if name == "job_workbench_job"
                else 1
                if "job_workbench" in name
                else 2
                if "job" in name
                else 3,
                name,
            ),
        )

        for table_name in ordered_tables:
            if "job" not in table_name:
                continue

            columns = table_columns[
                table_name
            ]

            department_column = next(
                (
                    column
                    for column
                    in department_candidates
                    if column in columns
                ),
                None,
            )

            join_column = next(
                (
                    column
                    for column
                    in join_candidates
                    if column in columns
                ),
                None,
            )

            if (
                not department_column
                or not join_column
            ):
                continue

            process_column = next(
                (
                    column
                    for column
                    in process_candidates
                    if column in columns
                ),
                None,
            )

            plant_column = next(
                (
                    column
                    for column
                    in plant_candidates
                    if column in columns
                ),
                None,
            )

            target_column = next(
                (
                    column
                    for column
                    in target_candidates
                    if column in columns
                ),
                None,
            )

            uom_column = next(
                (
                    column
                    for column
                    in uom_candidates
                    if column in columns
                ),
                None,
            )

            return {
                "table":
                    table_name,

                "join_column":
                    join_column,

                "department_column":
                    department_column,

                "process_column":
                    process_column,

                "plant_column":
                    plant_column,

                "target_column":
                    target_column,

                "uom_column":
                    uom_column,
            }

        return None

    except Exception as error:
        print(
            "Could not discover job metadata "
            f"source: {error}"
        )
        return None

    finally:
        if conn:
            conn.close()


def _column_expression(
    available_columns,
    column_name,
    alias,
    default_sql="NULL",
):
    """
    Return real column when available,
    otherwise return a safe default.
    """

    if column_name in available_columns:
        return (
            f'el."{column_name}" '
            f'AS "{alias}"'
        )

    return (
        f'{default_sql} '
        f'AS "{alias}"'
    )


# ============================================================
# MAIN DASHBOARD DATA
# ============================================================

@lru_cache(maxsize=64)
def fetch_dashboard_metadata(
    dept="All",
    created_from=None,
    created_to=None,
):
    """
    Fetch Cable Flow dashboard data directly from PostgreSQL.

    Important:
    - Missing optional DB columns will NOT break the query.
    - Complete end-date is included.
    - Machine name is read from job_machines_master.
    """

    conn = None

    try:
        available_columns = (
            _get_event_log_columns()
        )

        if not available_columns:
            print(
                "ERROR: Could not find columns for "
                "cableflow.event_logs"
            )
            return []

        required_columns = {
            "job_id",
            "machine_id",
            "created_at",
            "status",
        }

        missing_required = (
            required_columns
            - available_columns
        )

        if missing_required:
            print(
                "ERROR: Required event_logs "
                "columns are missing:"
            )

            for column in sorted(
                missing_required
            ):
                print(
                    f" - {column}"
                )

            return []

        select_columns = [
            _column_expression(
                available_columns,
                "job_id",
                "jobId",
                "0",
            ),

            _column_expression(
                available_columns,
                "machine_id",
                "machineId",
                "0",
            ),

            _column_expression(
                available_columns,
                "created_at",
                "createdAt",
                "NULL",
            ),

            _column_expression(
                available_columns,
                "updated_at",
                "updatedAt",
                "NULL",
            ),

            _column_expression(
                available_columns,
                "status",
                "status",
                "0",
            ),

            _column_expression(
                available_columns,
                "event_reason",
                "eventReason",
                "''::text",
            ),

            _column_expression(
                available_columns,
                "interface_log",
                "interfaceLog",
                "''::text",
            ),

            _column_expression(
                available_columns,
                "actual_executed_qty",
                "actualExecutedQty",
                "0::numeric",
            ),

            _column_expression(
                available_columns,
                "length",
                "length",
                "0::numeric",
            ),

            next(
                (
                    _column_expression(
                        available_columns,
                        name,
                        "actualProductionTons",
                        "NULL::numeric",
                    )
                    for name in [
                        "actual_production_tons",
                        "production_tons",
                        "actual_tons",
                        "actual_tonnage",
                        "tonnage",
                    ]
                    if name
                    in available_columns
                ),
                (
                    'NULL::numeric '
                    'AS "actualProductionTons"'
                ),
            ),

            (
                'COALESCE('
                'mm."machine_name"::text, '
                'CAST(el."machine_id" AS TEXT)'
                ') AS "machineCode"'
            ),
        ]

        metadata_source = (
            _get_job_metadata_source()
        )

        join_sql = ""

        if metadata_source:
            table_name = (
                metadata_source["table"]
            )

            join_column = (
                metadata_source[
                    "join_column"
                ]
            )

            department_column = (
                metadata_source[
                    "department_column"
                ]
            )

            process_column = (
                metadata_source.get(
                    "process_column"
                )
            )

            plant_column = (
                metadata_source.get(
                    "plant_column"
                )
            )

            target_column = (
                metadata_source.get(
                    "target_column"
                )
            )

            uom_column = (
                metadata_source.get(
                    "uom_column"
                )
            )

            select_columns.append(
                (
                    f'COALESCE('
                    f'jm."{department_column}"::text, '
                    f"''"
                    f') AS "dept"'
                )
            )

            if process_column:
                select_columns.append(
                    (
                        f'COALESCE('
                        f'jm."{process_column}"::text, '
                        f"''"
                        f') AS "processName"'
                    )
                )
            else:
                select_columns.append(
                    "''::text AS \"processName\""
                )

            if plant_column:
                select_columns.append(
                    (
                        f'COALESCE('
                        f'jm."{plant_column}"::text, '
                        f"''"
                        f') AS "plant"'
                    )
                )
            else:
                select_columns.append(
                    "''::text AS \"plant\""
                )

            if target_column:
                select_columns.append(
                    (
                        f'COALESCE('
                        f'jm."{target_column}"::numeric, '
                        f'0::numeric'
                        f') AS "plannedQty"'
                    )
                )

                select_columns.append(
                    (
                        f"'{target_column}'::text "
                        f'AS "plannedQtySource"'
                    )
                )
            else:
                select_columns.append(
                    (
                        '0::numeric '
                        'AS "plannedQty"'
                    )
                )

                select_columns.append(
                    (
                        "''::text "
                        'AS "plannedQtySource"'
                    )
                )

            if uom_column:
                select_columns.append(
                    (
                        f'COALESCE('
                        f'jm."{uom_column}"::text, '
                        f"''"
                        f') AS "productionUom"'
                    )
                )
            else:
                select_columns.append(
                    (
                        "''::text "
                        'AS "productionUom"'
                    )
                )

            join_sql = (
                f'LEFT JOIN '
                f'{DB_SCHEMA}."{table_name}" jm '
                f'ON jm."{join_column}" '
                f'= el."job_id"'
            )

        else:
            select_columns.extend(
                [
                    "''::text AS \"dept\"",
                    "''::text AS \"processName\"",
                    "''::text AS \"plant\"",
                    '0::numeric AS "plannedQty"',
                    "''::text AS \"plannedQtySource\"",
                    "''::text AS \"productionUom\"",
                ]
            )

        query = f"""
            SELECT
                {", ".join(select_columns)}

            FROM {DB_SCHEMA}.{EVENT_LOG_TABLE} el

            LEFT JOIN
                {DB_SCHEMA}.job_machines_master mm
                ON mm.id = el.machine_id

            {join_sql}

            WHERE 1 = 1
        """

        params = []

        start_date = _normalize_start_date(
            created_from
        )

        end_date = _normalize_end_date(
            created_to
        )

        if (
            start_date is not None
            and not pd.isna(start_date)
        ):
            query += """
                AND el.created_at >= %s
            """

            params.append(
                start_date.to_pydatetime()
            )

        if (
            end_date is not None
            and not pd.isna(end_date)
        ):
            query += """
                AND el.created_at < %s
            """

            params.append(
                end_date.to_pydatetime()
            )

        query += """
            ORDER BY el.created_at DESC
        """

        conn = _get_connection()

        df = pd.read_sql_query(
            query,
            conn,
            params=params,
        )

        if df.empty:
            return []

        numeric_columns = [
            "jobId",
            "machineId",
            "status",
            "actualExecutedQty",
            "length",
            "plannedQty",
            "actualProductionTons",
        ]

        for column in numeric_columns:
            if column not in df.columns:
                continue

            numeric = pd.to_numeric(
                df[column],
                errors="coerce",
            )

            if (
                column
                == "actualProductionTons"
            ):
                df[column] = numeric
            else:
                df[column] = (
                    numeric.fillna(0)
                )

        text_columns = [
            "eventReason",
            "interfaceLog",
            "processName",
            "machineCode",
            "dept",
            "plant",
            "plannedQtySource",
            "productionUom",
        ]

        for column in text_columns:
            if column in df.columns:
                df[column] = (
                    df[column]
                    .fillna("")
                    .astype(str)
                )

        return df.to_dict(
            "records"
        )

    except psycopg2.OperationalError as error:
        print("=" * 60)
        print(
            "POSTGRESQL CONNECTION ERROR"
        )
        print(error)
        print("=" * 60)

        return []

    except psycopg2.Error as error:
        print("=" * 60)
        print(
            "POSTGRESQL QUERY ERROR"
        )

        if error.pgerror:
            print(
                error.pgerror
            )
        else:
            print(error)

        print("=" * 60)

        return []

    except Exception as error:
        import traceback

        print("=" * 60)
        print(
            "DASHBOARD DATABASE ERROR"
        )
        print(
            repr(error)
        )
        print("=" * 60)

        traceback.print_exc()

        return []

    finally:
        if conn:
            conn.close()
