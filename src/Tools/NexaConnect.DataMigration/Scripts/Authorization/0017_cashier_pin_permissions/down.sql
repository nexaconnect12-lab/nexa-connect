DELETE FROM authorization_role_permissions WHERE permission_code IN ('pos.cashier-pin.use','pos.cashier-pin.manage');
DELETE FROM authorization_user_permission_overrides WHERE permission_code IN ('pos.cashier-pin.use','pos.cashier-pin.manage');
