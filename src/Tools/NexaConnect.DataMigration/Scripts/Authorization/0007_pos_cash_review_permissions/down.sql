DELETE FROM authorization_role_permissions
WHERE permission_code IN ('pos.cash-review.read', 'pos.cash-review.resolve');
