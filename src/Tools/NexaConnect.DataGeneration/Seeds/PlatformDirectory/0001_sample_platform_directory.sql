-- requires-schema-version: 1

INSERT INTO organizations
    (id, code, name, status, default_time_zone, created_at_utc, created_by,
     updated_at_utc, updated_by, concurrency_version)
VALUES ('d0000000-0000-0000-0000-000000000001', 'demo_group', 'Demo Hospitality Group',
    'active', 'Asia/Singapore', '2026-01-01T00:00:00Z', 'sample-data',
    '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO organization_memberships
    (id, organization_id, identity_subject_id, status, invited_at_utc, joined_at_utc,
     suspended_at_utc, removed_at_utc, created_at_utc, created_by, updated_at_utc,
     updated_by, concurrency_version)
VALUES ('d0000000-0000-0000-0000-000000000002', 'd0000000-0000-0000-0000-000000000001',
    'sample-user-001', 'active', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z',
    NULL, NULL, '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z',
    'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO applications
    (id, code, name, status, created_at_utc, created_by, updated_at_utc, updated_by,
     concurrency_version)
VALUES ('d0000000-0000-0000-0000-000000000003', 'nexa_connect', 'NexaConnect',
    'active', '2026-01-01T00:00:00Z', 'sample-data', '2026-01-01T00:00:00Z',
    'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO organization_application_access
    (organization_id, application_id, status, enabled_at_utc, suspended_at_utc,
     disabled_at_utc, created_at_utc, created_by, updated_at_utc, updated_by,
     concurrency_version)
VALUES ('d0000000-0000-0000-0000-000000000001', 'd0000000-0000-0000-0000-000000000003',
    'enabled', '2026-01-01T00:00:00Z', NULL, NULL, '2026-01-01T00:00:00Z',
    'sample-data', '2026-01-01T00:00:00Z', 'sample-data', 1)
ON CONFLICT DO NOTHING;

INSERT INTO outbox_messages
    (id, event_type, contract_version, aggregate_type, aggregate_id, payload,
     occurred_at_utc, published_at_utc, retry_count)
VALUES ('d0000000-0000-0000-0000-000000000004', 'platform.sample-data.loaded', 1,
    'organization', 'd0000000-0000-0000-0000-000000000001',
    '{"source":"sample-data"}'::jsonb, '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z', 0)
ON CONFLICT DO NOTHING;
