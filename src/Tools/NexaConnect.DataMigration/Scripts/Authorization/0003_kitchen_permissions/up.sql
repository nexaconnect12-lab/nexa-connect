INSERT INTO authorization_role_permissions (role_id, permission_code)
SELECT role.id, permission.code
FROM authorization_roles role
CROSS JOIN (VALUES ('kitchen.ticket.read'), ('kitchen.ticket.transition')) AS permission(code)
WHERE role.code IN ('tenant-admin', 'store-manager')
ON CONFLICT DO NOTHING;
