CREATE TABLE authorization_resource_scopes
(
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    restaurant_id uuid NULL,
    branch_id uuid NULL,
    status text NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_authorization_resource_scopes_status CHECK (status IN ('active', 'suspended', 'closed')),
    CONSTRAINT ck_authorization_resource_scopes_hierarchy CHECK
    (
        (restaurant_id IS NULL AND branch_id IS NULL)
        OR (restaurant_id IS NOT NULL AND branch_id IS NULL)
        OR (restaurant_id IS NOT NULL AND branch_id IS NOT NULL)
    ),
    CONSTRAINT uq_authorization_resource_scopes_organization_restaurant_branch
        UNIQUE NULLS NOT DISTINCT (organization_id, restaurant_id, branch_id)
);

CREATE TABLE authorization_roles
(
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    status text NOT NULL,
    CONSTRAINT uq_authorization_roles_organization_code UNIQUE (organization_id, code),
    CONSTRAINT ck_authorization_roles_status CHECK (status IN ('active', 'inactive'))
);

CREATE TABLE authorization_role_permissions
(
    role_id uuid NOT NULL REFERENCES authorization_roles (id) ON DELETE RESTRICT,
    permission_code text NOT NULL,
    PRIMARY KEY (role_id, permission_code)
);

CREATE TABLE authorization_role_assignments
(
    id uuid PRIMARY KEY,
    role_id uuid NOT NULL REFERENCES authorization_roles (id) ON DELETE RESTRICT,
    subject_id text NOT NULL,
    scope_id uuid NOT NULL REFERENCES authorization_resource_scopes (id) ON DELETE RESTRICT,
    status text NOT NULL,
    assigned_at_utc timestamptz NOT NULL,
    assigned_by_subject_id text NOT NULL,
    CONSTRAINT uq_authorization_role_assignments_role_subject_scope UNIQUE (role_id, subject_id, scope_id),
    CONSTRAINT ck_authorization_role_assignments_status CHECK (status IN ('active', 'revoked'))
);

CREATE TABLE authorization_user_permission_overrides
(
    id uuid PRIMARY KEY,
    subject_id text NOT NULL,
    scope_id uuid NOT NULL REFERENCES authorization_resource_scopes (id) ON DELETE RESTRICT,
    permission_code text NOT NULL,
    effect text NOT NULL,
    status text NOT NULL,
    CONSTRAINT uq_authorization_user_permission_overrides_subject_scope_permission
        UNIQUE (subject_id, scope_id, permission_code),
    CONSTRAINT ck_authorization_user_permission_overrides_effect CHECK (effect IN ('allow', 'deny')),
    CONSTRAINT ck_authorization_user_permission_overrides_status CHECK (status IN ('active', 'revoked'))
);

CREATE TABLE financial_approval_limits
(
    id uuid PRIMARY KEY,
    restaurant_id uuid NOT NULL,
    principal_type text NOT NULL,
    principal_id text NOT NULL,
    action_code text NOT NULL,
    currency char(3) NOT NULL,
    maximum_amount numeric(19,4) NOT NULL,
    status text NOT NULL,
    CONSTRAINT uq_financial_approval_limits_principal_action_currency
        UNIQUE (restaurant_id, principal_type, principal_id, action_code, currency),
    CONSTRAINT ck_financial_approval_limits_principal_type CHECK (principal_type IN ('role', 'subject')),
    CONSTRAINT ck_financial_approval_limits_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_financial_approval_limits_amount CHECK (maximum_amount >= 0),
    CONSTRAINT ck_financial_approval_limits_status CHECK (status IN ('active', 'revoked'))
);

CREATE TABLE authorization_decisions
(
    id uuid PRIMARY KEY,
    subject_id text NOT NULL,
    organization_id uuid NOT NULL,
    restaurant_id uuid NULL,
    branch_id uuid NULL,
    action_code text NOT NULL,
    granted boolean NOT NULL,
    evaluated_limit numeric(19,4) NULL,
    currency char(3) NULL,
    decided_at_utc timestamptz NOT NULL,
    policy_version integer NOT NULL,
    CONSTRAINT ck_authorization_decisions_policy_version CHECK (policy_version > 0)
);

CREATE INDEX ix_authorization_role_assignments_subject_status
    ON authorization_role_assignments (subject_id, status);
CREATE INDEX ix_authorization_decisions_subject_decided_at
    ON authorization_decisions (subject_id, decided_at_utc DESC);
