CREATE TABLE IF NOT EXISTS device_credentials (
    device_id VARCHAR(50) PRIMARY KEY,
    key_hash TEXT NOT NULL,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    rotated_at TIMESTAMPTZ,
    revoked_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS ix_device_credentials_active
    ON device_credentials (device_id)
    WHERE enabled AND revoked_at IS NULL;

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT TRUE;
