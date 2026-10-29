INSERT INTO authorization_role_permissions(role_id,permission_code)
SELECT id,'pos.drawer.manual-open' FROM authorization_roles WHERE code IN('tenant-admin','store-manager') ON CONFLICT DO NOTHING;
INSERT INTO authorization_role_permissions(role_id,permission_code)
SELECT r.id,p.code FROM authorization_roles r CROSS JOIN(VALUES('pos.drawer.cash-sale-open'),('pos.drawer.read')) p(code)
WHERE r.code IN('tenant-admin','store-manager','cashier') ON CONFLICT DO NOTHING;
