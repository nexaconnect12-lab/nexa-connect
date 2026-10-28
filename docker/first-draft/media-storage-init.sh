#!/bin/sh
set -eu
# Secrets remain private container environment; do not echo commands/provider replies.
mc alias set owned http://minio:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null
mc mb --ignore-existing owned/nexa-draft-media >/dev/null
mc anonymous set none owned/nexa-draft-media >/dev/null
mc admin user add owned "$MEDIA_ACCESS_KEY" "$MEDIA_SECRET_KEY" >/dev/null
mc admin policy create owned draft-media /opt/media-policy.json >/dev/null
mc admin policy attach owned draft-media --user "$MEDIA_ACCESS_KEY" >/dev/null
