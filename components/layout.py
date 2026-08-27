from dash import html, dcc


# =========================================================
# DATE RANGE
# =========================================================

def date_range_filter():
    return html.Div(
        className="filter-box date-range-compact-box",
        children=[
            html.Label("Date Range"),

            html.Div(
                className="date-filter-control",
                children=[

                    # Separate calendar icon area
                    html.Div(
                        className="date-filter-icon",
                    ),

                    dcc.DatePickerRange(
                        id="dashboard-date-range",
                        start_date=None,
                        end_date=None,
                        display_format="DD MMM YYYY",
                        start_date_placeholder_text="Start Date",
                        end_date_placeholder_text="End Date",
                        minimum_nights=0,
                        clearable=False,
                        reopen_calendar_on_clear=True,
                        className="custom-date-range-picker",
                    ),
                ],
            ),
        ],
    )


# =========================================================
# SEARCH MACHINE
# =========================================================

def search_machine_filter():
    return html.Div(
        className="filter-box search-box",
        children=[
            html.Label("Search Machine"),

            html.Div(
                className="search-input-wrapper",
                children=[
                    html.Span(
                        className="search-leading-icon icon-search",
                    ),

                    dcc.Input(
                        id="machine-search",
                        type="text",
                        placeholder="Machine ID / Position / Process...",
                        className="search-input",
                        debounce=True,
                        value="",
                    ),
                ],
            ),
        ],
    )


# =========================================================
# DEPARTMENT
# =========================================================

def department_filter(departments):
    departments = departments or ["All"]

    return html.Div(
        className="filter-box normal-filter-box department-filter-box",
        children=[
            html.Label("Department"),

            html.Div(
                className="department-filter-control",
                children=[

                    # Separate department/building icon area
                    html.Div(
                        className="department-filter-icon",
                    ),

                    dcc.Dropdown(
                        id="department-filter",
                        options=[
                            {
                                "label": (
                                    "All Departments"
                                    if department == "All"
                                    else str(department)
                                ),
                                "value": department,
                            }
                            for department in departments
                        ],
                        value="All",
                        clearable=False,
                        searchable=False,
                        className="dash-dropdown department-dropdown",
                        maxHeight=180,
                        optionHeight=32,
                    ),
                ],
            ),
        ],
    )


# =========================================================
# MACHINE TYPE
# =========================================================

def machine_type_filter(machine_types):
    machine_types = machine_types or ["All"]

    return html.Div(
        className="filter-box normal-filter-box machine-type-filter-box",
        children=[
            html.Label("Machine Type"),

            html.Div(
                className="dropdown-with-icon",
                children=[
                    html.Span(
                        className="filter-leading-icon icon-filter",
                    ),

                    dcc.Dropdown(
                        id="machine-type-filter",
                        options=[
                            {
                                "label": (
                                    "All Machines"
                                    if machine_type == "All"
                                    else str(machine_type)
                                ),
                                "value": machine_type,
                            }
                            for machine_type in machine_types
                        ],
                        value="All",
                        clearable=False,
                        searchable=False,
                        className="dash-dropdown",
                        maxHeight=180,
                        optionHeight=32,
                    ),
                ],
            ),
        ],
    )


# =========================================================
# HEADER
# =========================================================

def topbar(filter_options):
    filter_options = filter_options or {}

    departments = filter_options.get(
        "departments",
        ["All"],
    )

    machine_types = filter_options.get(
        "machine_types",
        ["All"],
    )

    return html.Div(
        className="dashboard-header prototype-header",
        children=[

            # =================================================
            # MAIN HEADER ROW
            # =================================================
            html.Div(
                className="prototype-header-row",
                children=[

                    # -----------------------------------------
                    # BRAND
                    # -----------------------------------------
                    html.Div(
                        className="header-brand prototype-brand",
                        children=[
                            html.Img(
                                src="/assets/logo/pakistan-cables.png",
                                className="header-logo",
                            ),

                            html.Div(
                                className="header-title-text",
                                children=[
                                    html.H1(
                                        "Cable Flow Dashboard"
                                    ),

                                    html.P(
                                        "Real-time Overview of Machine Operations"
                                    ),
                                ],
                            ),
                        ],
                    ),

                    # -----------------------------------------
                    # HEADER CONTROLS
                    # -----------------------------------------
                    html.Div(
                        className="prototype-header-controls",
                        children=[

                            # REFRESH
                            html.Button(
                                [
                                    html.Span(
                                        className=(
                                            "action-button-icon "
                                            "icon-refresh-dark"
                                        )
                                    ),

                                    html.Span(
                                        "Refresh Data"
                                    ),
                                ],
                                id="refresh-btn",
                                className="prototype-refresh-btn",
                                n_clicks=0,
                            ),

                            # DATE RANGE
                            html.Div(
                                className="prototype-date-control",
                                children=[
                                    date_range_filter()
                                ],
                            ),

                            # DEPARTMENT
                            html.Div(
                                className="prototype-dept-control",
                                children=[
                                    department_filter(
                                        departments
                                    )
                                ],
                            ),

                            # FILTERS BUTTON
                            html.Button(
                                [
                                    html.Span(
                                        className=(
                                            "action-button-icon "
                                            "icon-filter-white"
                                        )
                                    ),

                                    html.Span(
                                        "Filters"
                                    ),
                                ],
                                id="filters-toggle-btn",
                                className="prototype-filters-btn",
                                n_clicks=0,
                            ),
                        ],
                    ),
                ],
            ),

            # =================================================
            # ADVANCED FILTERS
            # =================================================
            html.Div(
                id="advanced-filters",
                className="advanced-filters-panel",
                style={
                    "display": "none"
                },
                children=[

                    # MACHINE TYPE
                    machine_type_filter(
                        machine_types
                    ),

                    # SEARCH
                    search_machine_filter(),

                    # RESET
                    html.Button(
                        [
                            html.Span(
                                className=(
                                    "action-button-icon "
                                    "icon-reset"
                                )
                            ),

                            html.Span(
                                "Reset"
                            ),
                        ],
                        id="date-reset-btn",
                        className="top-reset-btn",
                        n_clicks=0,
                    ),
                ],
            ),
        ],
    )