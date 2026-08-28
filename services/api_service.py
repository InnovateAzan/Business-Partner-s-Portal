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
    _get_event_log_columns.cache_clear()
    _get_job_metadata_source.cache_clear()


# ============================================================
# PRODUCTION TARGET API
# ============================================================

@lru_cache(maxsize=1)
def fetch_production_targets():
    """
    Fetch real Production Overview values from the configured API.

    Expected response example::

        [
            {"work_center": "GCF", "target": 0, "actual": 30},
            {"work_center": "PCF", "target": 0, "actual": 55},
        ]

    The API is configured through PRODUCTION_TARGET_API in .env.
    No target or actual value is hardcoded in the dashboard.
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

            work_center = str(row.get("work_center") or "").strip()

            try:
                target = float(row.get("target", 0) or 0)
            except (TypeError, ValueError):
                target = 0.0

            try:
                actual = float(row.get("actual", 0) or 0)
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
        print(f"Production target API request error: {error}")
        return []
    except ValueError as error:
        print(f"Production target API JSON error: {error}")
        return []
    except Exception as error:
        print(f"Production target API error: {error}")
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
                (DB_SCHEMA, EVENT_LOG_TABLE),
            )

            rows = cursor.fetchall()

        columns = {row[0] for row in rows}

        return columns

    except Exception as error:
        print(f"Error reading event_logs columns: {error}")
        return set()

    finally:
        if conn:
            conn.close()


@lru_cache(maxsize=1)
def _get_job_metadata_source():
    """
    Discover an optional job metadata table that can provide real
    department/process names for event_logs.job_id.

    event_logs itself does not contain department/process metadata, so
    the dashboard previously returned blank values and the Department
    dropdown only contained ``All``.  This helper safely looks for a
    related job table in the same schema and uses it when available.
    """
    conn = None

    try:
        conn = _get_connection()
        with conn.cursor() as cursor:
            cursor.execute(
                """
                SELECT table_name, column_name
                FROM information_schema.columns
                WHERE table_schema = %s
                ORDER BY table_name, ordinal_position
                """,
                (DB_SCHEMA,),
            )
            rows = cursor.fetchall()

        table_columns = {}
        for table_name, column_name in rows:
            table_columns.setdefault(table_name, set()).add(column_name)

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
        ]
        uom_candidates = [
            "uom",
            "uom_code",
            "unit_of_measure",
            "primary_uom_code",
        ]
        join_candidates = ["id", "job_id"]

        # Prefer the CableFlow job-workbench table when it exists, then
        # fall back to other job-like tables with a department column.
        ordered_tables = sorted(
            table_columns,
            key=lambda name: (
                0 if name == "job_workbench_job" else
                1 if "job_workbench" in name else
                2 if "job" in name else
                3,
                name,
            ),
        )

        for table_name in ordered_tables:
            if "job" not in table_name:
                continue

            columns = table_columns[table_name]
            department_column = next(
                (c for c in department_candidates if c in columns),
                None,
            )
            join_column = next(
                (c for c in join_candidates if c in columns),
                None,
            )

            if not department_column or not join_column:
                continue

            process_column = next(
                (c for c in process_candidates if c in columns),
                None,
            )
            target_column = next(
                (c for c in target_candidates if c in columns),
                None,
            )
            uom_column = next(
                (c for c in uom_candidates if c in columns),
                None,
            )

            return {
                "table": table_name,
                "join_column": join_column,
                "department_column": department_column,
                "process_column": process_column,
                "target_column": target_column,
                "uom_column": uom_column,
            }

        return None

    except Exception as error:
        print(f"Could not discover job metadata source: {error}")
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
        return f'el."{column_name}" AS "{alias}"'

    return f'{default_sql} AS "{alias}"'


# ============================================================
# DATE HELPERS
# ============================================================

def _normalize_start_date(value):
    """
    Convert start date safely.
    """
    if value is None or value == "":
        return None

    return pd.to_datetime(value, errors="coerce")


def _normalize_end_date(value):
    """
    Convert end date to an EXCLUSIVE upper boundary.

    Example:
        selected end date = 2026-08-21

    Query becomes:
        created_at < 2026-08-22

    This ensures the complete selected end day is included.
    """

    if value is None or value == "":
        return None

    parsed = pd.to_datetime(value, errors="coerce")

    if pd.isna(parsed):
        return None

    # If only a date/date-at-midnight was supplied,
    # include the entire selected date.
    if (
        parsed.hour == 0
        and parsed.minute == 0
        and parsed.second == 0
        and parsed.microsecond == 0
    ):
        parsed = parsed + pd.Timedelta(days=1)

    return parsed


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
    - Database/query errors remain visible in terminal.
    - Complete end-date is included.
    - Errors are printed with traceback.
    """

    conn = None

    try:
        available_columns = _get_event_log_columns()

        if not available_columns:
            print(
                "ERROR: Could not find columns for "
                "cableflow.event_logs"
            )
            return []

        # ----------------------------------------------------
        # Required fields
        # ----------------------------------------------------

        required_columns = {
            "job_id",
            "machine_id",
            "created_at",
            "status",
        }

        missing_required = (
            required_columns - available_columns
        )

        if missing_required:
            print(
                "ERROR: Required event_logs columns are missing:"
            )

            for column in sorted(missing_required):
                print(f" - {column}")

            return []

        # ----------------------------------------------------
        # Build query dynamically
        # ----------------------------------------------------

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

            # Prefer an explicit tonnage field when event_logs exposes one.
            next(
                (
                    _column_expression(available_columns, name, "actualProductionTons", "NULL::numeric")
                    for name in [
                        "actual_production_tons",
                        "production_tons",
                        "actual_tons",
                        "actual_tonnage",
                        "tonnage",
                    ]
                    if name in available_columns
                ),
                'NULL::numeric AS "actualProductionTons"',
            ),

            # Job-level metadata is added below when a related job table
            # is available.
            'COALESCE(mm."machine_name"::text, CAST(el."machine_id" AS TEXT)) AS "machineCode"',
        ]

        metadata_source = _get_job_metadata_source()
        join_sql = ""

        if metadata_source:
            table_name = metadata_source["table"]
            join_column = metadata_source["join_column"]
            department_column = metadata_source["department_column"]
            process_column = metadata_source.get("process_column")
            target_column = metadata_source.get("target_column")
            uom_column = metadata_source.get("uom_column")

            select_columns.append(
                f"COALESCE(jm.\"{department_column}\"::text, '') AS \"dept\""
            )

            if process_column:
                select_columns.append(
                    f"COALESCE(jm.\"{process_column}\"::text, '') AS \"processName\""
                )
            else:
                select_columns.append("''::text AS \"processName\"")

            if target_column:
                select_columns.append(
                    f"COALESCE(jm.\"{target_column}\"::numeric, 0::numeric) AS \"plannedQty\""
                )
                select_columns.append(
                    f"'{target_column}'::text AS \"plannedQtySource\""
                )
            else:
                select_columns.append('0::numeric AS "plannedQty"')
                select_columns.append("''::text AS \"plannedQtySource\"")

            if uom_column:
                select_columns.append(
                    f"COALESCE(jm.\"{uom_column}\"::text, '') AS \"productionUom\""
                )
            else:
                select_columns.append("''::text AS \"productionUom\"")

            join_sql = (
                f'LEFT JOIN {DB_SCHEMA}."{table_name}" jm '
                f'ON jm."{join_column}" = el."job_id"'
            )
        else:
            select_columns.extend([
                "''::text AS \"dept\"",
                "''::text AS \"processName\"",
                '0::numeric AS "plannedQty"',
                "''::text AS \"plannedQtySource\"",
                "''::text AS \"productionUom\"",
            ])

        query = f"""
            SELECT
                {", ".join(select_columns)}

            FROM {DB_SCHEMA}.{EVENT_LOG_TABLE} el
            LEFT JOIN {DB_SCHEMA}.job_machines_master mm
                ON mm.id = el.machine_id
            {join_sql}

            WHERE 1 = 1
        """

        params = []

        # ----------------------------------------------------
        # Date filtering
        # ----------------------------------------------------

        start_date = _normalize_start_date(created_from)
        end_date = _normalize_end_date(created_to)

        if start_date is not None and not pd.isna(start_date):
            query += """
                AND el.created_at >= %s
            """
            params.append(start_date.to_pydatetime())

        if end_date is not None and not pd.isna(end_date):
            query += """
                AND el.created_at < %s
            """
            params.append(end_date.to_pydatetime())

        query += """
            ORDER BY el.created_at DESC
        """

        # ----------------------------------------------------
        # Execute query
        # ----------------------------------------------------

        conn = _get_connection()



        df = pd.read_sql_query(
            query,
            conn,
            params=params,
        )


        # ----------------------------------------------------
        # No rows
        # ----------------------------------------------------

        if df.empty:
            return []

        # ----------------------------------------------------
        # Clean numeric values
        # ----------------------------------------------------

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
            if column in df.columns:
                numeric = pd.to_numeric(df[column], errors="coerce")
                # NULL means the database has no explicit tonnage source; keep
                # that distinction so the UI can show N/A instead of fake 0 Tons.
                df[column] = numeric if column == "actualProductionTons" else numeric.fillna(0)

        # ----------------------------------------------------
        # Clean text values
        # ----------------------------------------------------

        text_columns = [
            "eventReason",
            "interfaceLog",
            "processName",
            "machineCode",
            "dept",
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

        # ----------------------------------------------------
        # Convert dataframe to records
        # ----------------------------------------------------

        return df.to_dict("records")

    except psycopg2.OperationalError as error:
        print("=" * 60)
        print("POSTGRESQL CONNECTION ERROR")
        print(error)
        print("=" * 60)

        return pd.DataFrame().to_dict("records")

    except psycopg2.Error as error:
        print("=" * 60)
        print("POSTGRESQL QUERY ERROR")

        if error.pgerror:
            print(error.pgerror)
        else:
            print(error)

        print("=" * 60)

        return pd.DataFrame().to_dict("records")

    except Exception as error:
        import traceback

        print("=" * 60)
        print("DASHBOARD DATABASE ERROR")
        print(repr(error))
        print("=" * 60)

        traceback.print_exc()

        return pd.DataFrame().to_dict("records")

    finally:
        if conn:
            conn.close()