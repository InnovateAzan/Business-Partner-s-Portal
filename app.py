from dash import Dash, html, dcc, Input, Output, State, ctx, no_update
import dash_bootstrap_components as dbc
from wsgiref.simple_server import make_server

from components.layout import topbar
from pages.overview import overview_page

from services.mock_data import (
    get_filter_options,
    clear_data_cache,
)


app = Dash(
    __name__,
    external_stylesheets=[dbc.themes.BOOTSTRAP],
    suppress_callback_exceptions=True,
)

server = app.server
app.title = "Cable Flow Dashboard"

filter_options = get_filter_options()

DEFAULT_FILTERS = {
    "start_date": None,
    "end_date": None,
    "plant": "All",
    "department": "All",
    "machine_type": "All",
    "search_text": "",
}

app.layout = html.Div(
    className="app-shell no-sidebar-shell",
    children=[
        # Refresh the dashboard every 60 seconds without resetting user filters.
        dcc.Interval(
            id="auto-refresh-interval",
            interval=60 * 1000,
            n_intervals=0,
        ),
        html.Div(
            className="main-content no-sidebar-main",
            children=[
                topbar(filter_options),
                html.Div(
                    id="page-content",
                    children=overview_page(DEFAULT_FILTERS),
                ),
            ],
        ),
    ],
)


@app.callback(
    Output("advanced-filters", "style"),
    Input("filters-toggle-btn", "n_clicks"),
    Input("filters-close-btn", "n_clicks"),
    State("advanced-filters", "style"),
    prevent_initial_call=True,
)
def toggle_advanced_filters(toggle_clicks, close_clicks, current_style):
    if ctx.triggered_id == "filters-close-btn":
        return {"display": "none"}

    is_open = (current_style or {}).get("display") != "none"
    return {"display": "none" if is_open else "block"}


@app.callback(
    Output("dashboard-date-range", "start_date"),
    Output("dashboard-date-range", "end_date"),
    Output("machine-search", "value"),
    Output("plant-filter", "value"),
    Input("date-reset-btn", "n_clicks"),
    Input("refresh-btn", "n_clicks"),
    prevent_initial_call=True,
)
def reset_filters(date_reset_clicks, refresh_clicks):
    triggered_id = ctx.triggered_id

    if triggered_id == "date-reset-btn":
        clear_data_cache()
        return None, None, "", "All"

    if triggered_id == "refresh-btn":
        # Manual refresh reloads data but preserves the user's plant selection.
        clear_data_cache()
        return None, None, "", no_update

    return no_update, no_update, no_update, no_update, no_update


@app.callback(
    Output("page-content", "children"),
    Input("dashboard-date-range", "start_date"),
    Input("dashboard-date-range", "end_date"),
    Input("plant-filter", "value"),
    Input("machine-search", "value"),
    Input("refresh-btn", "n_clicks"),
    Input("auto-refresh-interval", "n_intervals"),
)
def render_page(
    start_date,
    end_date,
    plant,
    search_text,
    refresh_clicks,
    n_intervals,
):
    if ctx.triggered_id in ["refresh-btn", "auto-refresh-interval"]:
        clear_data_cache()

    filters = {
        "start_date": start_date,
        "end_date": end_date,
        "plant": plant or "All",
        "department": "All",
        "machine_type": "All",
        "search_text": search_text or "",
    }

    return overview_page(filters)


if __name__ == "__main__":
    host = "0.0.0.0"
    port = 8050

    print(f"Serving on http://{host}:{port}")

    with make_server(host, port, server) as httpd:
        httpd.serve_forever()
