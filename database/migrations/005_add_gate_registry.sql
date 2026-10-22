CREATE TABLE IF NOT EXISTS gates (
    gate_id VARCHAR(20) PRIMARY KEY,
    gate_type VARCHAR(20) NOT NULL CHECK (gate_type IN ('GATE_IN', 'GATE_OUT')),
    display_name VARCHAR(100) NOT NULL,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO gates (gate_id, gate_type, display_name)
VALUES
    ('GATE-IN-01', 'GATE_IN', 'Gerbang Masuk 01'),
    ('GATE-OUT-01', 'GATE_OUT', 'Gerbang Keluar 01')
ON CONFLICT (gate_id) DO NOTHING;