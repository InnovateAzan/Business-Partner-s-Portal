from dash import (
    Dash,
    html,
    Input,
    Output,
    State,
    ctx,
    no_update,
)

import dash_bootstrap_components as dbc
from wsgiref.simple_server import make_server

from components.layout import topbar
from pages.overview import overview_page

from services.mock_data import (
    get_filter_options,
    get_machine_types_by_department,
    clear_data_cache,
)


# =========================================================
# APP INITIALIZATION
# =========================================================

app = Dash(
    __name__,
    external_stylesheets=[
        dbc.themes.BOOTSTRAP,
    ],
    suppress_callback_exceptions=True,
)

server = app.server
app.title = "Cable Flow Dashboard"


# =========================================================
# INITIAL FILTER OPTIONS
# =========================================================

filter_options = get_filter_options()


# =========================================================
# DEFAULT FILTER STATE
# =========================================================

DEFAULT_FILTERS = {
    "start_date": None,
    "end_date": None,
    "department": "All",
    "machine_type": "All",
    "search_text": "",
}


# =========================================================
# APP LAYOUT
# =========================================================

app.layout = html.Div(
    className="app-shell no-sidebar-shell",
    children=[
        html.Div(
            className="main-content no-sidebar-main",
            children=[

                # Header / filters
                topbar(
                    filter_options
                ),

                # Dashboard page
                html.Div(
                    id="page-content",
                    children=overview_page(
                        DEFAULT_FILTERS
                    ),
                ),
            ],
        ),
    ],
)


# =========================================================
# SHOW / HIDE ADVANCED FILTERS
# =========================================================

@app.callback(
    Output(
        "advanced-filters",
        "style",
    ),
    Input(
        "filters-toggle-btn",
        "n_clicks",
    ),
    State(
        "advanced-filters",
        "style",
    ),
    prevent_initial_call=True,
)
def toggle_advanced_filters(
    n_clicks,
    current_style,
):
    current_style = (
        current_style
        or {
            "display": "none"
        }
    )

    is_open = (
        current_style.get(
            "display"
        )
        != "none"
    )

    if is_open:
        return {
            "display": "none"
        }

    return {
        "display": "flex"
    }


# =========================================================
# RESET FILTERS / REFRESH
# =========================================================

@app.callback(
    Output(
        "dashboard-date-range",
        "start_date",
    ),
    Output(
        "dashboard-date-range",
        "end_date",
    ),
    Output(
        "department-filter",
        "value",
    ),
    Output(
        "machine-search",
        "value",
    ),

    Input(
        "date-reset-btn",
        "n_clicks",
    ),
    Input(
        "refresh-btn",
        "n_clicks",
    ),

    prevent_initial_call=True,
)
def reset_filters(
    date_reset_clicks,
    refresh_clicks,
):
    triggered_id = ctx.triggered_id

    if triggered_id in [
        "date-reset-btn",
        "refresh-btn",
    ]:
        clear_data_cache()

        return (
            None,
            None,
            "All",
            "",
        )

    return (
        no_update,
        no_update,
        no_update,
        no_update,
    )


# =========================================================
# REFRESH DEPARTMENT OPTIONS
# =========================================================

@app.callback(
    Output(
        "department-filter",
        "options",
    ),
    Input(
        "refresh-btn",
        "n_clicks",
    ),
)
def refresh_department_filter_options(
    refresh_clicks,
):
    if refresh_clicks:
        clear_data_cache()

    options = get_filter_options()

    departments = options.get(
        "departments",
        ["All"],
    )

    if not departments:
        departments = [
            "All"
        ]

    return [
        {
            "label": (
                "All Departments"
                if department == "All"
                else department
            ),
            "value": department,
        }
        for department
        in departments
    ]


# =========================================================
# MACHINE TYPES BY DEPARTMENT
# =========================================================

@app.callback(
    Output(
        "machine-type-filter",
        "options",
    ),
    Output(
        "machine-type-filter",
        "value",
    ),

    Input(
        "department-filter",
        "value",
    ),
    Input(
        "machine-type-filter",
        "value",
    ),
    Input(
        "date-reset-btn",
        "n_clicks",
    ),
    Input(
        "refresh-btn",
        "n_clicks",
    ),
)
def update_machine_type_filter_by_department(
    department,
    current_machine_type,
    date_reset_clicks,
    refresh_clicks,
):
    triggered_id = ctx.triggered_id

    department = (
        department
        or "All"
    )

    machine_types = (
        get_machine_types_by_department(
            department
        )
    )

    if not machine_types:
        machine_types = [
            "All"
        ]

    options = [
        {
            "label": (
                "All Machine Types"
                if machine_type == "All"
                else machine_type
            ),
            "value": machine_type,
        }
        for machine_type
        in machine_types
    ]

    if triggered_id in [
        "date-reset-btn",
        "refresh-btn",
    ]:
        selected_value = "All"

    elif (
        current_machine_type
        in machine_types
    ):
        selected_value = (
            current_machine_type
        )

    else:
        selected_value = "All"

    return (
        options,
        selected_value,
    )


# =========================================================
# MAIN DASHBOARD RENDER CALLBACK
# =========================================================

@app.callback(
    Output(
        "page-content",
        "children",
    ),

    Input(
        "dashboard-date-range",
        "start_date",
    ),
    Input(
        "dashboard-date-range",
        "end_date",
    ),
    Input(
        "department-filter",
        "value",
    ),
    Input(
        "machine-type-filter",
        "value",
    ),
    Input(
        "machine-search",
        "value",
    ),
    Input(
        "refresh-btn",
        "n_clicks",
    ),
)
def render_page(
    start_date,
    end_date,
    department,
    machine_type,
    search_text,
    refresh_clicks,
):
    if (
        ctx.triggered_id
        == "refresh-btn"
    ):
        clear_data_cache()

    filters = {
        "start_date":
            start_date,

        "end_date":
            end_date,

        "department":
            department
            or "All",

        "machine_type":
            machine_type
            or "All",

        "search_text":
            search_text
            or "",
    }

    return overview_page(
        filters
    )


# =========================================================
# RUN APPLICATION
# =========================================================

if __name__ == "__main__":
    host = "127.0.0.1"
    port = 8050

    print(
        f"Serving on "
        f"http://{host}:{port}"
    )

    with make_server(
        host,
        port,
        server,
    ) as httpd:
        httpd.serve_forever()