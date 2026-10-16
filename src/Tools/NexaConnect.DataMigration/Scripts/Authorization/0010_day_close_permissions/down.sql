DELETE FROM authorization_role_permissions WHERE permission_code IN('pos.day-close.read','pos.day-close.prepare');

DELETE FROM authorization_user_permission_overrides WHERE permission_code IN('pos.day-close.read','pos.day-close.prepare');
