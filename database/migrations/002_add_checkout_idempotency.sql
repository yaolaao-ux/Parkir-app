ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS checkout_id UUID;

CREATE UNIQUE INDEX IF NOT EXISTS ux_transactions_checkout_id
    ON transactions (checkout_id)
    WHERE checkout_id IS NOT NULL;