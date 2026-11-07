CREATE TABLE manual_refund_releases (
    operation_id uuid PRIMARY KEY REFERENCES manual_refund_approvals(operation_id),
    manager_subject varchar(200) NOT NULL,
    authorization_decision_id uuid NOT NULL,
    released_at_utc timestamptz NOT NULL
);
CREATE TRIGGER manual_refund_releases_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON manual_refund_releases
    FOR EACH STATEMENT EXECUTE FUNCTION prevent_manual_refund_history_mutation();

CREATE TABLE manual_refund_cashier_readiness (
    shift_id uuid PRIMARY KEY REFERENCES shifts(id),
    organization_id uuid NOT NULL,
    cashier_subject varchar(200) NOT NULL,
    checked_at_utc timestamptz NOT NULL,
    expires_at_utc timestamptz NOT NULL CHECK (expires_at_utc > checked_at_utc)
);
CREATE INDEX ix_manual_refund_readiness_org ON manual_refund_cashier_readiness(organization_id, expires_at_utc);
