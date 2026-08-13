ALTER TABLE branch_management_audit DROP CONSTRAINT ck_branch_management_audit_action;
ALTER TABLE branch_management_audit ADD CONSTRAINT ck_branch_management_audit_action
    CHECK (action IN ('branch.created', 'branch.updated'));
