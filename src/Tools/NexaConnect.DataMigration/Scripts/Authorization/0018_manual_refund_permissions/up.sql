INSERT INTO authorization_role_permissions(role_id,permission_code)
SELECT id,'pos.manual-refund.approve' FROM authorization_roles
WHERE code IN ('tenant-admin','store-manager') ON CONFLICT DO NOTHING;
INSERT INTO authorization_role_permissions(role_id,permission_code)
SELECT id,'pos.manual-refund.payout' FROM authorization_roles
WHERE code='cashier' ON CONFLICT DO NOTHING;
