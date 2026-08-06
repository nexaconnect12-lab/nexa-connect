CREATE TABLE notifications
(
    id uuid PRIMARY KEY,
    channel text NOT NULL,
    recipient text NOT NULL,
    subject text NOT NULL,
    body text NOT NULL,
    status text NOT NULL,
    created_at_utc timestamptz NOT NULL
);
