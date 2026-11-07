DELETE FROM authorization_role_permissions WHERE permission_code IN ('pos.manual-refund.approve','pos.manual-refund.payout');
DELETE FROM authorization_user_permission_overrides WHERE permission_code IN ('pos.manual-refund.approve','pos.manual-refund.payout');
