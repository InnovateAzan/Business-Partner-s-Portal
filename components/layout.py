from dash import html, dcc


# =========================================================
# SEARCH MACHINE
# =========================================================

def search_machine_filter():
    return html.Div(
        className="filter-box popup-filter-box search-box",
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
        className="filter-box popup-filter-box normal-filter-box department-filter-box",
        children=[
            html.Label("Department"),
            html.Div(
                className="department-filter-control",
                children=[
                    html.Span(
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
        className="filter-box popup-filter-box normal-filter-box machine-type-filter-box",
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
                                    "All Machine Types"
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
# DATE RANGE
# =========================================================

def date_range_filter():
    return html.Div(
        className="filter-box popup-filter-box date-range-compact-box",
        children=[
            html.Label("Date Range"),
            html.Div(
                className="date-filter-control",
                children=[
                    html.Span(
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
# HEADER / TOPBAR
# =========================================================

def topbar(filter_options):
    filter_options = filter_options or {}


    return html.Div(
        className="dashboard-header prototype-header",
        children=[
            html.Div(
                className="prototype-header-row",
                children=[
                    # BRAND
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
                                    html.H1("Cable Flow Dashboard"),
                                    html.P(
                                        "Real-time Overview of Machine Operations"
                                    ),
                                ],
                            ),
                        ],
                    ),

                    # GLOBAL SEARCH
                    # Uses the existing machine-search callback ID, but the
                    # backend search also matches Department and Machine Type.
                    html.Div(
                        className="header-global-search",
                        children=[
                            html.Span(
                                className="header-global-search-icon icon-search",
                            ),
                            dcc.Input(
                                id="machine-search",
                                type="text",
                                placeholder="Search Machine / Department...",
                                value="",
                                debounce=True,
                                className="header-global-search-input",
                            ),
                        ],
                    ),

                    # PLANT QUICK FILTER
                    dcc.RadioItems(
                        id="plant-filter",
                        options=[
                            {"label": "All Plants", "value": "All"},
                            {"label": "GCFA", "value": "GCFA"},
                            {"label": "PCF", "value": "PCF"},
                            {"label": "GCFB", "value": "GCFB"},
                        ],
                        value="All",
                        inline=True,
                        className="plant-segmented-control",
                        inputClassName="plant-segmented-input",
                        labelClassName="plant-segmented-label",
                    ),

                    # HEADER ACTIONS
                    html.Div(
                        className="prototype-header-controls",
                        children=[
                            html.Button(
                                [
                                    html.Span(
                                        className=(
                                            "action-button-icon "
                                            "icon-refresh-dark"
                                        ),
                                    ),
                                    html.Span("Refresh Data"),
                                ],
                                id="refresh-btn",
                                className="prototype-refresh-btn",
                                n_clicks=0,
                            ),
                            html.Button(
                                [
                                    html.Span(
                                        className=(
                                            "action-button-icon "
                                            "icon-filter-green"
                                        ),
                                    ),
                                    html.Span("Filters"),
                                ],
                                id="filters-toggle-btn",
                                className="prototype-filters-btn",
                                n_clicks=0,
                            ),
                        ],
                    ),
                ],
            ),

            # FLOATING FILTER POPUP
            html.Div(
                id="advanced-filters",
                className="advanced-filters-panel filter-popover",
                style={"display": "none"},
                children=[
                    html.Div(
                        className="filter-popover-header",
                        children=[
                            html.Div(
                                children=[
                                    html.H3(
                                        "Filters",
                                        className="filter-popover-title",
                                    ),
                                    html.P(
                                        "Refine dashboard data",
                                        className="filter-popover-subtitle",
                                    ),
                                ],
                            ),
                            html.Button(
                                "×",
                                id="filters-close-btn",
                                n_clicks=0,
                                className="filter-popover-close",
                                title="Close filters",
                            ),
                        ],
                    ),

                    # FILTER POPUP ORDER:
                    # 1. Department
                    # 2. Machine Type
                    # 3. Date Range
                    #
                    # Search Machine / Department is now in the main header.
                    html.Div(
                        className="filter-popover-body filter-popover-body-column",
                        children=[
                            date_range_filter(),
                        ],
                    ),

                    html.Div(
                        className="filter-popover-footer",
                        children=[
                            html.Button(
                                [
                                    html.Span(
                                        className=(
                                            "action-button-icon "
                                            "icon-reset"
                                        ),
                                    ),
                                    html.Span("Reset Filters"),
                                ],
                                id="date-reset-btn",
                                className="top-reset-btn popup-reset-btn",
                                n_clicks=0,
                            ),
                        ],
                    ),
                ],
            ),
        ],
    )
