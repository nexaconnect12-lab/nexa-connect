DELETE FROM authorization_role_permissions permission
USING authorization_roles role
WHERE permission.role_id=role.id
  AND permission.permission_code='order.payment-review.read'
  AND role.code='accountant';
