INSERT INTO authorization_role_permissions(role_id,permission_code)
SELECT id,'pos.cashier-pin.use' FROM authorization_roles WHERE code IN ('tenant-admin','store-manager','cashier') ON CONFLICT DO NOTHING;
INSERT INTO authorization_role_permissions(role_id,permission_code)
SELECT id,'pos.cashier-pin.manage' FROM authorization_roles WHERE code='tenant-admin' ON CONFLICT DO NOTHING;
