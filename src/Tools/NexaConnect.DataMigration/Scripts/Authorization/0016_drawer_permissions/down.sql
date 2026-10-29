DELETE FROM authorization_role_permissions WHERE permission_code IN('pos.drawer.manual-open','pos.drawer.cash-sale-open','pos.drawer.read');
DELETE FROM authorization_user_permission_overrides WHERE permission_code IN('pos.drawer.manual-open','pos.drawer.cash-sale-open','pos.drawer.read');
