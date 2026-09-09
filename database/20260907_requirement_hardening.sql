BEGIN;

CREATE SCHEMA IF NOT EXISTS invoice;
CREATE SCHEMA IF NOT EXISTS integration;

ALTER TABLE invoice.invoices ADD COLUMN IF NOT EXISTS normalized_invoice_number varchar(200);
ALTER TABLE invoice.invoices ADD COLUMN IF NOT EXISTS fiscal_year integer;

UPDATE invoice.invoices
SET normalized_invoice_number = upper(regexp_replace(trim(invoice_number), '[^A-Za-z0-9]', '', 'g'))
WHERE normalized_invoice_number IS NULL;

UPDATE invoice.invoices
SET fiscal_year = CASE
  WHEN invoice_date IS NULL THEN extract(year from created_at)::int
  WHEN extract(month from invoice_date) >= 7 THEN extract(year from invoice_date)::int
  ELSE extract(year from invoice_date)::int - 1
END
WHERE fiscal_year IS NULL;

CREATE OR REPLACE FUNCTION invoice.set_invoice_normalized_fields() RETURNS trigger LANGUAGE plpgsql AS 3076
BEGIN
  NEW.normalized_invoice_number := upper(regexp_replace(trim(NEW.invoice_number), '[^A-Za-z0-9]', '', 'g'));
  NEW.fiscal_year := CASE WHEN NEW.invoice_date IS NULL THEN extract(year from COALESCE(NEW.created_at, now()))::int WHEN extract(month from NEW.invoice_date) >= 7 THEN extract(year from NEW.invoice_date)::int ELSE extract(year from NEW.invoice_date)::int - 1 END;
  RETURN NEW;
END 3076;
DROP TRIGGER IF EXISTS trg_invoice_normalize ON invoice.invoices;
CREATE TRIGGER trg_invoice_normalize BEFORE INSERT OR UPDATE OF invoice_number, invoice_date ON invoice.invoices FOR EACH ROW EXECUTE FUNCTION invoice.set_invoice_normalized_fields();

CREATE UNIQUE INDEX IF NOT EXISTS ux_invoice_vendor_normalized_fy
ON invoice.invoices(vendor_id, normalized_invoice_number, fiscal_year)
WHERE deleted_at IS NULL AND status <> 'DRAFT';

CREATE TABLE IF NOT EXISTS invoice.invoice_line_grn_allocations (
  id uuid PRIMARY KEY,
  invoice_id uuid NOT NULL REFERENCES invoice.invoices(id) ON DELETE CASCADE,
  vendor_id uuid NOT NULL REFERENCES master.vendors(id),
  po_number varchar(100) NOT NULL,
  grn_number varchar(100) NOT NULL,
  rcv_transaction_id bigint NOT NULL,
  po_header_id bigint NOT NULL,
  po_line_id bigint NOT NULL,
  po_line_number integer NOT NULL,
  po_line_location_id bigint NOT NULL,
  received_quantity numeric(18,6) NOT NULL,
  available_quantity_at_submit numeric(18,6) NOT NULL,
  allocated_quantity numeric(18,6) NOT NULL,
  unit_price numeric(18,6) NOT NULL,
  allocated_amount numeric(18,2) NOT NULL,
  match_option varchar(10),
  created_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_invoice_grn_alloc_qty CHECK (allocated_quantity > 0),
  CONSTRAINT ck_invoice_grn_alloc_amount CHECK (allocated_amount >= 0)
);
CREATE INDEX IF NOT EXISTS ix_invoice_grn_alloc_invoice ON invoice.invoice_line_grn_allocations(invoice_id);
CREATE INDEX IF NOT EXISTS ix_invoice_grn_alloc_rcv ON invoice.invoice_line_grn_allocations(rcv_transaction_id);

CREATE TABLE IF NOT EXISTS integration.idempotency_requests (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL,
  vendor_id uuid,
  idempotency_key varchar(200) NOT NULL,
  operation varchar(80) NOT NULL,
  request_hash varchar(64) NOT NULL,
  aggregate_id uuid,
  response_status integer,
  created_at timestamptz NOT NULL DEFAULT now(),
  expires_at timestamptz NOT NULL,
  UNIQUE(user_id, operation, idempotency_key)
);

CREATE TABLE IF NOT EXISTS integration.integration_inbox (
  id uuid PRIMARY KEY,
  source varchar(50) NOT NULL,
  external_message_id varchar(200) NOT NULL,
  event_type varchar(100) NOT NULL,
  payload jsonb NOT NULL,
  status varchar(30) NOT NULL DEFAULT 'RECEIVED',
  received_at timestamptz NOT NULL DEFAULT now(),
  processed_at timestamptz,
  error_message text,
  UNIQUE(source, external_message_id)
);

-- Vendor isolation defence-in-depth. The API must SET LOCAL app.vendor_id
-- inside a transaction before vendor-owned queries when RLS is enforced.
ALTER TABLE invoice.invoices ENABLE ROW LEVEL SECURITY;
ALTER TABLE invoice.invoice_line_grn_allocations ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS invoices_vendor_isolation ON invoice.invoices;
CREATE POLICY invoices_vendor_isolation ON invoice.invoices
USING (
  current_setting('app.is_internal', true) = 'true'
  OR vendor_id = nullif(current_setting('app.vendor_id', true), '')::uuid
)
WITH CHECK (
  current_setting('app.is_internal', true) = 'true'
  OR vendor_id = nullif(current_setting('app.vendor_id', true), '')::uuid
);

DROP POLICY IF EXISTS allocations_vendor_isolation ON invoice.invoice_line_grn_allocations;
CREATE POLICY allocations_vendor_isolation ON invoice.invoice_line_grn_allocations
USING (
  current_setting('app.is_internal', true) = 'true'
  OR vendor_id = nullif(current_setting('app.vendor_id', true), '')::uuid
)
WITH CHECK (
  current_setting('app.is_internal', true) = 'true'
  OR vendor_id = nullif(current_setting('app.vendor_id', true), '')::uuid
);

COMMIT;
