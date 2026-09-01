CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS citext;
CREATE SCHEMA IF NOT EXISTS security; CREATE SCHEMA IF NOT EXISTS master; CREATE SCHEMA IF NOT EXISTS procurement; CREATE SCHEMA IF NOT EXISTS invoice; CREATE SCHEMA IF NOT EXISTS payment; CREATE SCHEMA IF NOT EXISTS integration; CREATE SCHEMA IF NOT EXISTS audit; CREATE SCHEMA IF NOT EXISTS notification;

CREATE TABLE IF NOT EXISTS security.departments(id uuid primary key default gen_random_uuid(),code varchar(50) unique not null,name varchar(150) not null,description varchar(500),is_active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now());
CREATE TABLE IF NOT EXISTS security.users(id uuid primary key default gen_random_uuid(),employee_code varchar(50),full_name varchar(200) not null,email citext unique not null,password_hash text,user_type varchar(20) not null check(user_type in('ADMIN','INTERNAL','VENDOR')),department_id uuid references security.departments(id),phone_number varchar(30),is_super_admin boolean not null default false,is_active boolean not null default true,email_verified boolean not null default false,mfa_enabled boolean not null default false,failed_login_attempts int not null default 0,lockout_until timestamptz,last_login_at timestamptz,password_changed_at timestamptz,created_by uuid references security.users(id),created_at timestamptz not null default now(),updated_at timestamptz not null default now());
CREATE TABLE IF NOT EXISTS security.roles(id uuid primary key default gen_random_uuid(),code varchar(100) unique not null,name varchar(150) not null,description varchar(500),is_system_role boolean not null default false,is_active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now());
CREATE TABLE IF NOT EXISTS security.features(id uuid primary key default gen_random_uuid(),code varchar(100) unique not null,name varchar(150) not null,description varchar(500),route_path varchar(250),icon_name varchar(100),display_order int not null default 0,is_active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now());
CREATE TABLE IF NOT EXISTS security.permissions(id uuid primary key default gen_random_uuid(),feature_id uuid references security.features(id) on delete set null,code varchar(150) unique not null,name varchar(200) not null,description varchar(500),is_active boolean not null default true,created_at timestamptz not null default now());
CREATE TABLE IF NOT EXISTS security.user_roles(user_id uuid references security.users(id) on delete cascade,role_id uuid references security.roles(id) on delete cascade,assigned_by uuid references security.users(id),assigned_at timestamptz not null default now(),primary key(user_id,role_id));
CREATE TABLE IF NOT EXISTS security.role_permissions(role_id uuid references security.roles(id) on delete cascade,permission_id uuid references security.permissions(id) on delete cascade,granted_at timestamptz not null default now(),primary key(role_id,permission_id));
CREATE TABLE IF NOT EXISTS security.user_permission_overrides(user_id uuid references security.users(id) on delete cascade,permission_id uuid references security.permissions(id) on delete cascade,is_allowed boolean not null,granted_by uuid references security.users(id),reason varchar(500),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),primary key(user_id,permission_id));

CREATE TABLE IF NOT EXISTS master.vendors(id uuid primary key default gen_random_uuid(),vendor_code varchar(100) unique not null,vendor_name varchar(250) not null,oracle_vendor_id varchar(100),tax_number varchar(100),email citext,phone_number varchar(50),address text,city varchar(100),country varchar(100) default 'Pakistan',is_active boolean not null default true,source_system varchar(50) not null default 'ORACLE_EBS',last_synced_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now());
CREATE TABLE IF NOT EXISTS master.vendor_users(vendor_id uuid references master.vendors(id) on delete cascade,user_id uuid references security.users(id) on delete cascade,is_primary boolean not null default false,is_active boolean not null default true,created_at timestamptz not null default now(),primary key(vendor_id,user_id));

CREATE TABLE IF NOT EXISTS procurement.purchase_orders(id uuid primary key default gen_random_uuid(),vendor_id uuid not null references master.vendors(id),po_number varchar(100) not null,po_date date,currency_code varchar(10) not null default 'PKR',total_amount numeric(18,2) not null default 0,remaining_amount numeric(18,2) not null default 0,status varchar(30) not null,oracle_po_id varchar(100),source_system varchar(50) not null default 'ORACLE_EBS',last_synced_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),unique(vendor_id,po_number));
CREATE TABLE IF NOT EXISTS procurement.grns(id uuid primary key default gen_random_uuid(),purchase_order_id uuid not null references procurement.purchase_orders(id),grn_number varchar(100) not null,grn_date date,status varchar(30) not null,qc_status varchar(30) not null default 'NOT_REQUIRED',oracle_grn_id varchar(100),source_system varchar(50) not null default 'ORACLE_EBS',last_synced_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),unique(purchase_order_id,grn_number));

CREATE TABLE IF NOT EXISTS invoice.invoices(id uuid primary key default gen_random_uuid(),vendor_id uuid not null references master.vendors(id),invoice_number varchar(150) not null,invoice_date date,invoice_amount numeric(18,2) not null default 0,currency_code varchar(10) not null default 'PKR',status varchar(40) not null default 'DRAFT',integration_status varchar(30) not null default 'NOT_STARTED',oracle_invoice_id varchar(150),oracle_invoice_number varchar(150),submission_date timestamptz,last_resubmitted_at timestamptz,accepted_at timestamptz,paid_at timestamptz,remarks text,created_by uuid not null references security.users(id),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),deleted_at timestamptz);
CREATE UNIQUE INDEX IF NOT EXISTS ux_invoice_vendor_invoice_number ON invoice.invoices(vendor_id,lower(invoice_number)) WHERE deleted_at IS NULL;
CREATE TABLE IF NOT EXISTS invoice.invoice_grns(invoice_id uuid references invoice.invoices(id) on delete cascade,grn_id uuid references procurement.grns(id),created_at timestamptz not null default now(),primary key(invoice_id,grn_id));
CREATE TABLE IF NOT EXISTS invoice.documents(id uuid primary key default gen_random_uuid(),invoice_id uuid not null references invoice.invoices(id) on delete cascade,document_type varchar(40) not null,original_file_name varchar(500) not null,stored_file_name varchar(500),content_type varchar(150) not null,file_extension varchar(20),file_size bigint not null,file_hash_sha256 varchar(64) not null,file_content bytea not null,uploaded_by uuid not null references security.users(id),uploaded_at timestamptz not null default now(),is_active boolean not null default true);
CREATE TABLE IF NOT EXISTS invoice.invoice_status_history(id bigserial primary key,invoice_id uuid not null references invoice.invoices(id) on delete cascade,old_status varchar(40),new_status varchar(40) not null,remarks text,source varchar(50) not null default 'PORTAL',changed_by uuid references security.users(id),changed_at timestamptz not null default now(),correlation_id uuid);
CREATE TABLE IF NOT EXISTS payment.payments(id uuid primary key default gen_random_uuid(),invoice_id uuid not null references invoice.invoices(id),payment_reference varchar(150),payment_date date,amount numeric(18,2) not null,currency_code varchar(10) not null default 'PKR',status varchar(30) not null,oracle_reference varchar(150),last_synced_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now());
CREATE TABLE IF NOT EXISTS integration.outbox_messages(id uuid primary key default gen_random_uuid(),event_type varchar(150) not null,aggregate_type varchar(100) not null,aggregate_id uuid not null,payload jsonb not null,status varchar(30) not null default 'PENDING',attempt_count int not null default 0,next_attempt_at timestamptz,processed_at timestamptz,last_error text,correlation_id uuid not null default gen_random_uuid(),created_at timestamptz not null default now());
CREATE TABLE IF NOT EXISTS audit.audit_logs(id bigserial primary key,user_id uuid references security.users(id),vendor_id uuid references master.vendors(id),action varchar(150) not null,entity_type varchar(100) not null,entity_id uuid,old_values jsonb,new_values jsonb,additional_data jsonb,ip_address inet,user_agent text,correlation_id uuid,created_at timestamptz not null default now());

INSERT INTO security.roles(code,name,is_system_role) VALUES ('ADMIN','Administrator',true),('FINANCE','Finance / AP',true),('SUPPLY_CHAIN','Supply Chain',true),('INTEGRATION_SUPPORT','Integration Support',true),('VENDOR','Vendor',true) ON CONFLICT(code) DO NOTHING;
INSERT INTO security.features(code,name,route_path,display_order) VALUES ('DASHBOARD','Dashboard','/dashboard',10),('PURCHASE_ORDERS','Purchase Orders','/purchase-orders',20),('GRNS','GRNs','/grns',30),('INVOICES','Invoices','/invoices',40),('PAYMENTS','Payments','/payments',50),('USER_MANAGEMENT','User Management','/admin/users',60),('AUDIT','Audit','/audit',70) ON CONFLICT(code) DO NOTHING;
INSERT INTO security.permissions(feature_id,code,name) VALUES
((SELECT id FROM security.features WHERE code='DASHBOARD'),'DASHBOARD.VIEW','View Dashboard'),
((SELECT id FROM security.features WHERE code='PURCHASE_ORDERS'),'PO.VIEW','View Purchase Orders'),
((SELECT id FROM security.features WHERE code='GRNS'),'GRN.VIEW','View GRNs'),
((SELECT id FROM security.features WHERE code='INVOICES'),'INVOICE.CREATE','Create Invoice'),
((SELECT id FROM security.features WHERE code='INVOICES'),'INVOICE.VIEW_OWN','View Own Invoices'),
((SELECT id FROM security.features WHERE code='INVOICES'),'INVOICE.VIEW_ALL','View All Invoices'),
((SELECT id FROM security.features WHERE code='INVOICES'),'INVOICE.RESUBMIT','Resubmit Invoice'),
((SELECT id FROM security.features WHERE code='INVOICES'),'DOCUMENT.UPLOAD','Upload Documents'),
((SELECT id FROM security.features WHERE code='INVOICES'),'DOCUMENT.DOWNLOAD','Download Documents'),
((SELECT id FROM security.features WHERE code='PAYMENTS'),'PAYMENT.VIEW','View Payments'),
((SELECT id FROM security.features WHERE code='USER_MANAGEMENT'),'USER.MANAGE','Manage Users'),
((SELECT id FROM security.features WHERE code='AUDIT'),'AUDIT.VIEW','View Audit'),
(NULL,'VENDOR.MANAGE','Manage Vendor Portal Access'),
(NULL,'INTEGRATION.VIEW','View Oracle Integration Status'),
(NULL,'INTEGRATION.RETRY','Retry Oracle Integration') ON CONFLICT(code) DO NOTHING;
INSERT INTO security.role_permissions(role_id,permission_id) SELECT r.id,p.id FROM security.roles r CROSS JOIN security.permissions p WHERE r.code='ADMIN' ON CONFLICT DO NOTHING;
INSERT INTO security.role_permissions(role_id,permission_id) SELECT r.id,p.id FROM security.roles r JOIN security.permissions p ON p.code IN('DASHBOARD.VIEW','PO.VIEW','GRN.VIEW','INVOICE.CREATE','INVOICE.VIEW_OWN','INVOICE.RESUBMIT','DOCUMENT.UPLOAD','DOCUMENT.DOWNLOAD','PAYMENT.VIEW') WHERE r.code='VENDOR' ON CONFLICT DO NOTHING;
INSERT INTO security.role_permissions(role_id,permission_id) SELECT r.id,p.id FROM security.roles r JOIN security.permissions p ON p.code IN('DASHBOARD.VIEW','PO.VIEW','GRN.VIEW','INVOICE.VIEW_ALL','DOCUMENT.DOWNLOAD','PAYMENT.VIEW') WHERE r.code='FINANCE' ON CONFLICT DO NOTHING;
INSERT INTO security.role_permissions(role_id,permission_id) SELECT r.id,p.id FROM security.roles r JOIN security.permissions p ON p.code IN('DASHBOARD.VIEW','PO.VIEW','GRN.VIEW','VENDOR.MANAGE') WHERE r.code='SUPPLY_CHAIN' ON CONFLICT DO NOTHING;
INSERT INTO security.role_permissions(role_id,permission_id) SELECT r.id,p.id FROM security.roles r JOIN security.permissions p ON p.code IN('DASHBOARD.VIEW','INTEGRATION.VIEW','INTEGRATION.RETRY') WHERE r.code='INTEGRATION_SUPPORT' ON CONFLICT DO NOTHING;

-- 2026 portal completion additions
ALTER TABLE invoice.invoices ADD COLUMN IF NOT EXISTS po_number varchar(100);
ALTER TABLE invoice.invoices ADD COLUMN IF NOT EXISTS grn_numbers text;
ALTER TABLE invoice.invoices ADD COLUMN IF NOT EXISTS invoice_type varchar(20) NOT NULL DEFAULT 'GOODS';

CREATE SCHEMA IF NOT EXISTS notification;
CREATE TABLE IF NOT EXISTS notification.notifications(
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references security.users(id) on delete cascade,
  title varchar(250) not null,
  message text not null,
  notification_type varchar(80),
  entity_type varchar(80),
  entity_id uuid,
  is_read boolean not null default false,
  read_at timestamptz,
  created_at timestamptz not null default now(),
  type varchar(80),
  action_url varchar(300),
  updated_at timestamptz not null default now()
);
CREATE INDEX IF NOT EXISTS ix_notifications_user_read_created ON notification.notifications(user_id,is_read,created_at desc);
