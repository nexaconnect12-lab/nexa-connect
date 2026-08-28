INSERT INTO authorization_role_permissions(role_id,permission_code)
SELECT role.id,permission.code FROM authorization_roles role
CROSS JOIN(VALUES('order.payment-review.read'),('order.payment-review.resolve')) AS permission(code)
WHERE role.code IN('tenant-admin','store-manager') ON CONFLICT DO NOTHING;
