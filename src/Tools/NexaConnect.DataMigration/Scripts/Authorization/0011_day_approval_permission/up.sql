INSERT INTO authorization_role_permissions(role_id,permission_code)
SELECT role.id,'pos.day-close.approve' FROM authorization_roles role
WHERE role.code IN('tenant-admin','store-manager') ON CONFLICT DO NOTHING;
