INSERT INTO authorization_role_permissions(role_id,permission_code)
SELECT role.id,'order.payment-review.read'
FROM authorization_roles role
WHERE role.code='accountant'
ON CONFLICT DO NOTHING;
