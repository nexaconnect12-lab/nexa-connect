INSERT INTO authorization_role_permissions(role_id, permission_code)
SELECT role.id, permission.code
FROM authorization_roles role
CROSS JOIN (VALUES ('pos.cash-review.read'), ('pos.cash-review.resolve')) AS permission(code)
WHERE role.code IN ('tenant-admin', 'store-manager')
ON CONFLICT DO NOTHING;

INSERT INTO authorization_role_permissions(role_id, permission_code)
SELECT role.id, 'pos.cash-review.read'
FROM authorization_roles role
WHERE role.code = 'accountant'
ON CONFLICT DO NOTHING;
