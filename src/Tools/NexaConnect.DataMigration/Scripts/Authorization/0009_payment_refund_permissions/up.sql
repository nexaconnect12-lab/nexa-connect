INSERT INTO authorization_role_permissions(role_id,permission_code)
SELECT role.id,'payment.refund.create' FROM authorization_roles role
WHERE role.code IN('tenant-admin','store-manager') ON CONFLICT DO NOTHING;
INSERT INTO authorization_role_permissions(role_id,permission_code)
SELECT role.id,'payment.refund.read' FROM authorization_roles role
WHERE role.code IN('tenant-admin','store-manager','accountant','cashier') ON CONFLICT DO NOTHING;
