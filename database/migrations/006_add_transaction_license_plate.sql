ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS license_plate VARCHAR(20);
