-- Oracle AP invoice outbox diagnostics and safe recovery.
-- Run each SELECT first.  Do not bulk-reset PROCESSING rows: Oracle may have
-- committed an AP invoice while the portal process was unavailable.

-- A. Invoice submission events currently stuck or overdue for monitoring.
SELECT o.id AS outbox_id, o.aggregate_id AS portal_invoice_id,
       i.invoice_number, o.event_type, o.status, o.attempt_count,
       o.next_attempt_at, o.external_reference, o.oracle_request_id,
       o.last_error, o.created_at
FROM integration.outbox_messages o
JOIN invoice.invoices i ON i.id = o.aggregate_id
WHERE o.event_type IN ('InvoiceSubmitted', 'InvoiceResubmitted')
  AND o.status IN ('PROCESSING', 'RETRYING')
  AND (o.next_attempt_at IS NULL OR o.next_attempt_at <= now())
ORDER BY o.created_at;

-- B. Safely force ONE known stale PROCESSING event into reconciliation.  The
-- worker will look up AP_INVOICES_ALL and AP_INVOICES_INTERFACE first.
-- Replace the UUID and keep the status predicate.
-- UPDATE integration.outbox_messages
-- SET next_attempt_at = now()
-- WHERE id = 'c31e75af-e778-4c5c-ab90-b875cfaecbdb'::uuid
--   AND status = 'PROCESSING';

-- C. Pending invoice events that should already be eligible to be claimed.
SELECT o.id, o.aggregate_id AS portal_invoice_id, i.invoice_number,
       o.status, o.attempt_count, o.next_attempt_at, o.created_at, o.last_error
FROM integration.outbox_messages o
JOIN invoice.invoices i ON i.id = o.aggregate_id
WHERE o.event_type IN ('InvoiceSubmitted', 'InvoiceResubmitted')
  AND o.status = 'PENDING'
  AND o.next_attempt_at IS NULL
ORDER BY o.created_at;

-- D. Duplicate active submission events for the same portal invoice.
SELECT aggregate_id AS portal_invoice_id, COUNT(*) AS active_event_count,
       array_agg(id ORDER BY created_at) AS outbox_ids,
       array_agg(status ORDER BY created_at) AS statuses
FROM integration.outbox_messages
WHERE event_type IN ('InvoiceSubmitted', 'InvoiceResubmitted')
  AND status IN ('PENDING', 'PROCESSING', 'RETRYING')
GROUP BY aggregate_id
HAVING COUNT(*) > 1;

-- E. Portal invoices that have an Oracle invoice ID but whose submission event
-- was not finalized. These require reconciliation, not blind resubmission.
SELECT i.id AS portal_invoice_id, i.invoice_number, i.oracle_invoice_id,
       o.id AS outbox_id, o.status, o.attempt_count, o.next_attempt_at
FROM invoice.invoices i
JOIN integration.outbox_messages o ON o.aggregate_id = i.id
WHERE NULLIF(TRIM(i.oracle_invoice_id), '') IS NOT NULL
  AND o.event_type IN ('InvoiceSubmitted', 'InvoiceResubmitted')
  AND o.status <> 'PROCESSED'
ORDER BY o.created_at;
