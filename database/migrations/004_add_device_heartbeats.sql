CREATE TABLE IF NOT EXISTS devices (
    device_id VARCHAR(50) PRIMARY KEY,
    device_type VARCHAR(30) NOT NULL CHECK (device_type IN ('GATE_IN', 'CASHIER_POS')),
    display_name VARCHAR(100) NOT NULL,
    firmware_version VARCHAR(50),
    ip_address INET,
    last_seen TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_devices_last_seen ON devices (last_seen DESC);