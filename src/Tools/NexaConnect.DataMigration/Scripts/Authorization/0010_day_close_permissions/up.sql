INSERT INTO authorization_role_permissions(role_id,permission_code)
SELECT role.id,'pos.day-close.read' FROM authorization_roles role
WHERE role.code IN('tenant-admin','store-manager','accountant') ON CONFLICT DO NOTHING;
INSERT INTO authorization_role_permissions(role_id,permission_code)
SELECT role.id,'pos.day-close.prepare' FROM authorization_roles role
WHERE role.code IN('tenant-admin','store-manager') ON CONFLICT DO NOTHING;
