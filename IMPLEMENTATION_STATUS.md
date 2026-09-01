# Implementation Status

## Implemented in portal code

- Green/white role-based redesign and responsive portal navigation.
- Oracle-backed vendor profile, PO/GRN/QC and invoice/payment reads.
- Server-side vendor isolation through authenticated user -> vendor mapping -> Oracle VENDOR_ID.
- Oracle-verified vendor self-registration with OTP and automatic portal mapping.
- Supply Chain vendor-access enable/disable screen.
- Admin user/role management and audit visibility.
- Integration Support queue/retry/error visibility.
- Multiple-GRN invoice submission.
- Goods-only Receipted Delivery Challan validation.
- Duplicate invoice prevention.
- Draft, submit, returned invoice edit/resubmit and immutable status-history entries.
- Documents/download authorization.
- Notifications and unread count.
- Oracle reconciliation for Finance/payment outcomes.
- Configurable retention worker.
- Outbox/retry architecture for invoice posting to the approved Oracle interface.

## Production dependencies still requiring organization input

- Exact supported Oracle EBS invoice-creation endpoint/procedure and payload contract.
- Production SMTP/enterprise mail settings.
- Final PCL retention duration.
- Any final Finance mapping rules for Oracle approval/tolerance status values.

The application does not fabricate these external contracts. Configure them through environment settings once formally supplied.
