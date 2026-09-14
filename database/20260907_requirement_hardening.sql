BEGIN;

-- ============================================================
-- SUPPLY CHAIN DEDICATED NAVIGATION FEATURES
-- ============================================================

INSERT INTO security.features
(
    code,
    name,
    route_path,
    display_order
)
VALUES
(
    'SUPPLY_CHAIN_RECENT_PO',
    'Supply Chain - Recent Purchase Orders',
    '/supply-chain/purchase-orders',
    55
),
(
    'SUPPLY_CHAIN_RECENT_GRN',
    'Supply Chain - Recent GRNs',
    '/supply-chain/grns',
    56
),
(
    'SUPPLY_CHAIN_VENDOR_REQUESTS',
    'Supply Chain - Pending Vendor Requests',
    '/supply-chain/vendor-requests',
    57
),
(
    'SUPPLY_CHAIN_ONBOARDED_VENDORS',
    'Supply Chain - Onboarded Vendors',
    '/supply-chain/onboarded-vendors',
    58
)
ON CONFLICT (code)
DO UPDATE SET
    name = EXCLUDED.name,
    route_path = EXCLUDED.route_path,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

-- ============================================================
-- SUPPLY CHAIN PAGE PERMISSIONS
-- ============================================================

INSERT INTO security.permissions
(
    feature_id,
    code,
    name,
    description,
    is_active
)
VALUES
(
    (SELECT id FROM security.features WHERE code = 'SUPPLY_CHAIN_RECENT_PO'),
    'SUPPLY_CHAIN_PO_VIEW',
    'View Supply Chain Recent Purchase Orders',
    'Allows access to the Supply Chain recent purchase orders page.',
    TRUE
),
(
    (SELECT id FROM security.features WHERE code = 'SUPPLY_CHAIN_RECENT_GRN'),
    'SUPPLY_CHAIN_GRN_VIEW',
    'View Supply Chain Recent GRNs',
    'Allows access to the Supply Chain recent GRNs page.',
    TRUE
),
(
    (SELECT id FROM security.features WHERE code = 'SUPPLY_CHAIN_VENDOR_REQUESTS'),
    'SUPPLY_CHAIN_VENDOR_REQUEST_VIEW',
    'View Pending Vendor Requests',
    'Allows access to the Supply Chain pending vendor requests page.',
    TRUE
),
(
    (SELECT id FROM security.features WHERE code = 'SUPPLY_CHAIN_ONBOARDED_VENDORS'),
    'SUPPLY_CHAIN_ONBOARDED_VENDOR_VIEW',
    'View Recently Onboarded Vendors',
    'Allows access to the Supply Chain recently onboarded vendors page.',
    TRUE
)
ON CONFLICT (code)
DO UPDATE SET
    feature_id = EXCLUDED.feature_id,
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    is_active = TRUE;

-- ============================================================
-- SUPPLY CHAIN ROLE MAPPING
-- ============================================================

INSERT INTO security.role_permissions
(
    role_id,
    permission_id
)
SELECT
    r.id,
    p.id
FROM
    security.roles r
CROSS JOIN
    security.permissions p
WHERE
    r.code = 'SUPPLY_CHAIN'
AND
    p.code IN
    (
        'SUPPLY_CHAIN_PO_VIEW',
        'SUPPLY_CHAIN_GRN_VIEW',
        'SUPPLY_CHAIN_VENDOR_REQUEST_VIEW',
        'SUPPLY_CHAIN_ONBOARDED_VENDOR_VIEW'
    )
ON CONFLICT DO NOTHING;

-- The Supply Chain sidebar no longer uses the generic PO/GRN pages.
-- Remove those generic page permissions from the Supply Chain role only.
DELETE FROM security.role_permissions rp
USING security.roles r,
      security.permissions p
WHERE rp.role_id = r.id
  AND rp.permission_id = p.id
  AND r.code = 'SUPPLY_CHAIN'
  AND p.code IN
  (
      'PO.VIEW',
      'GRN.VIEW'
  );

COMMIT;
