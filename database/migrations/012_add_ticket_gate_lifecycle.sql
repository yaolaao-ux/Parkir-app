ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS print_status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    ADD COLUMN IF NOT EXISTS reprint_count INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS last_reprint_at TIMESTAMPTZ;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'transactions_print_status_check'
    ) THEN
        ALTER TABLE transactions
            ADD CONSTRAINT transactions_print_status_check
            CHECK (print_status IN ('PENDING', 'PRINT_SENT', 'PRINT_CONFIRMED', 'PRINT_FAILED', 'NOT_REQUIRED'));
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS gate_commands (
    command_id UUID PRIMARY KEY,
    ticket_id VARCHAR(50) NOT NULL REFERENCES transactions(ticket_id) ON DELETE CASCADE,
    gate_id VARCHAR(20) NOT NULL REFERENCES gates(gate_id),
    action VARCHAR(20) NOT NULL CHECK (action IN ('OPEN', 'CLOSE')),
    status VARCHAR(20) NOT NULL CHECK (status IN ('SENT', 'ACKNOWLEDGED', 'EXECUTED', 'FAILED')),
    expires_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP + INTERVAL '30 seconds',
    detail VARCHAR(200),
    actor_user_id BIGINT REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_gate_commands_ticket
    ON gate_commands (ticket_id, created_at DESC);
