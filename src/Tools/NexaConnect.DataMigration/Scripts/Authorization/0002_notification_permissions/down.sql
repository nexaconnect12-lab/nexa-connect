DELETE FROM authorization_role_permissions permission
USING authorization_roles role
WHERE permission.role_id = role.id
  AND role.code IN ('tenant-admin', 'store-manager')
  AND permission.permission_code IN ('notification.read', 'notification.send');
