-- Business Partner's Portal - incremental upgrade for an existing portal database.
-- Review in staging first and take a backup before production execution.

ALTER TABLE IF EXISTS invoice.invoices ADD COLUMN IF NOT EXISTS po_number varchar(100);
ALTER TABLE IF EXISTS invoice.invoices ADD COLUMN IF NOT EXISTS grn_numbers text;
ALTER TABLE IF EXISTS invoice.invoices ADD COLUMN IF NOT EXISTS invoice_type varchar(20);

CREATE SCHEMA IF NOT EXISTS notification;
CREATE TABLE IF NOT EXISTS notification.notifications (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES security.users(id),
    title varchar(250) NOT NULL,
    message text NOT NULL,
    notification_type varchar(100),
    entity_type varchar(100),
    entity_id uuid,
    is_read boolean NOT NULL DEFAULT false,
    read_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_notifications_user_read_created
    ON notification.notifications(user_id, is_read, created_at DESC);


-- Role-specific portal permissions
INSERT INTO security.permissions(feature_id,code,name) VALUES
(NULL,'VENDOR.MANAGE','Manage Vendor Portal Access'),
(NULL,'INTEGRATION.VIEW','View Oracle Integration Status'),
(NULL,'INTEGRATION.RETRY','Retry Oracle Integration')
ON CONFLICT(code) DO NOTHING;

INSERT INTO security.role_permissions(role_id,permission_id)
SELECT r.id,p.id FROM security.roles r JOIN security.permissions p ON p.code IN('VENDOR.MANAGE')
WHERE r.code='SUPPLY_CHAIN' ON CONFLICT DO NOTHING;

INSERT INTO security.role_permissions(role_id,permission_id)
SELECT r.id,p.id FROM security.roles r JOIN security.permissions p ON p.code IN('INTEGRATION.VIEW','INTEGRATION.RETRY')
WHERE r.code='INTEGRATION_SUPPORT' ON CONFLICT DO NOTHING;
