# Pakistan Cables - Business Partner's Portal

Complete React + TypeScript / ASP.NET Core 8 portal package aligned to the supplied Business Partner's Portal scope and the approved green/white dashboard direction.

## Main roles

- Vendor: own Oracle-backed profile, POs, GRNs/QC, invoice creation, multiple GRNs, invoice/DC uploads, drafts, history, returned invoice resubmission, payment visibility, downloads and notifications.
- Supply Chain: vendor portal-access monitoring, enable/disable and user-to-vendor access mapping. Vendor master remains in Oracle EBS.
- Admin / IT: users, roles/permissions, vendor access, audit and integration visibility.
- Integration Support: Oracle interface queue, pending/success/failed status, retry and error/reconciliation visibility.

Finance review/approval remains in Oracle EBS and is synchronized back to the portal.

## Vendor self-registration

The registration flow is Oracle-first:

1. Supplier already exists in `APPS.PORTAL_SUPPLIERS_V`.
2. Vendor selects Sign Up and enters Supplier Number, Vendor Name and the email registered in Oracle.
3. Backend verifies the details against Oracle.
4. OTP is sent only to the Oracle-registered email.
5. After OTP and password verification the portal user is created.
6. PostgreSQL stores portal identity/access and the mapping to the Oracle `VENDOR_ID`.
7. The vendor thereafter sees only records scoped to that Oracle `VENDOR_ID`.

When SMTP is disabled in Development, the registration start response includes a development OTP for local testing. Do not use that behavior in production.

## Oracle read integration

Configured read-only views:

- `APPS.PORTAL_SUPPLIERS_V`
- `APPS.PORTAL_PO_GRN_V`
- `APPS.PORTAL_INVOICES_V`

Vendor scoping uses `VENDOR_ID`, never `VENDOR_CODE`.

PO/GRN QC comes from `INSPECTION_STATUS`. The portal displays configured pending/non-final inspection states as `Pending with QC` and prevents invoicing those GRNs.

## Invoice rules implemented

- Vendor ownership validated server-side.
- Selected PO must belong to the current vendor.
- Every selected GRN must belong to the selected PO/current vendor.
- Multiple GRNs are supported.
- Duplicate invoice number is blocked per vendor.
- Invoice attachment is required on submit.
- Receipted Delivery Challan is mandatory for `GOODS` and not mandatory for `SERVICE`.
- Returned invoices can be edited and resubmitted.
- Status history is preserved in `invoice_status_history`.
- Documents are stored with invoice references and hashes.
- Submit/resubmit creates an integration outbox event.

## Oracle invoice creation

The portal includes a reliable outbox worker and retry path. The actual Oracle invoice-creation transport must use the officially approved Oracle EBS API/middleware/procedure supplied by the Oracle team.

Configure:

- `ORACLE_INVOICE_POST_URL`
- `ORACLE_API_KEY` if required

If the approved endpoint is not configured, the worker marks the interface attempt as failed with a controlled error rather than pretending the invoice was posted successfully.

## Payment / Finance reconciliation

A background reconciliation worker reads `PORTAL_INVOICES_V` and updates portal invoices with Oracle Finance/payment outcomes such as returned/reverted, accepted, cancelled and paid. Vendor notifications are created on meaningful status changes.

## Data retention

`DATA_RETENTION_DAYS=0` disables automatic purge by default. Set it only after PCL's approved retention period is confirmed. The worker then applies the configured retention rule to stored portal documents.

## Setup

1. Create/update PostgreSQL using `database/schema.sql` for a fresh database or `database/upgrade-2026-portal-completion.sql` for an existing database.
2. Copy `backend/BusinessPartnerPortal.Api/.env.example` to `.env` and enter environment-specific secrets.
3. Copy `frontend/.env.example` to `.env` if required.
4. From the repository root run `./start-dev.ps1` in PowerShell, or use the separate backend/frontend scripts.

Never commit `.env` files or Oracle/SMTP/JWT/database secrets.

## Development URLs

- Frontend: `http://localhost:5173`
- Backend HTTP: based on your launch/runtime configuration
- Backend HTTPS used by the provided frontend example: `https://localhost:7044`
- Swagger in Development: `/swagger`

## External production dependencies

These cannot be invented by application code and must be supplied/approved before go-live:

- Official Oracle invoice creation API/procedure/middleware contract and required payload fields.
- Production SMTP/enterprise email configuration for OTP.
- Final PCL data-retention period.
- Finance-confirmed Oracle status/tolerance mappings where values differ from the currently observed view values.

## Validation in this package build

Frontend source was syntax-transpiled successfully. A full dependency build was not possible in the packaging environment because npm dependency installation was unavailable. The packaging environment also does not contain the .NET SDK, so run `dotnet restore` and `dotnet build` on the target development machine before deployment.
