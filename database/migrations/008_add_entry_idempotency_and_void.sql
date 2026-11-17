ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS entry_idempotency_key VARCHAR(100);

CREATE UNIQUE INDEX IF NOT EXISTS ux_transactions_entry_idempotency
    ON transactions (gate_in_id, entry_idempotency_key)
    WHERE entry_idempotency_key IS NOT NULL;

ALTER TABLE transactions
    DROP CONSTRAINT IF EXISTS transactions_payment_status_check;

ALTER TABLE transactions
    ADD CONSTRAINT transactions_payment_status_check
    CHECK (payment_status IN ('UNPAID', 'PAID', 'MEMBER', 'LOST_TICKET', 'VOID'));
