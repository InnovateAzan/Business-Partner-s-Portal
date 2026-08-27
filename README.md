# Cable Flow Dashboard

Python + Plotly Dash starter folder structure for Pakistan Cables machine monitoring dashboard.

## Features included

- Machine Summary KPI: Total, Up/Running, Down/Stopped
- Idle Machines KPI
- Halt/Stop KPI
- Total Downtime Today KPI
- Machine Status Distribution chart
- Downtime Trend chart
- Idle Position Trend chart
- Stopped Machines Detail table with downtime reasons
- Idle Machines Analysis table with idle reason, from, to, duration
- PostgreSQL connection structure using SQLAlchemy

## Run commands

```bash
cd cable_flow_dashboard
python -m venv venv
venv\Scripts\activate
pip install -r requirements.txt
copy .env.example .env
python app.py
```

Open browser:

```text
http://127.0.0.1:8050
```

## Next step

Replace demo data in `pages/overview.py` with service calls from PostgreSQL.
