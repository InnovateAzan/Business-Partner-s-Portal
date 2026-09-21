ALTER TABLE invoice.invoices
ADD COLUMN IF NOT EXISTS description text;

-- Existing remarks can contain Oracle Finance feedback (ATTRIBUTE13), so this
-- migration intentionally does not move or clear them without row-level proof
-- that they were vendor-entered descriptions.
