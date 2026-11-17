ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS original_vehicle_type_id BIGINT REFERENCES vehicle_types(id),
    ADD COLUMN IF NOT EXISTS override_reason VARCHAR(200),
    ADD COLUMN IF NOT EXISTS original_total_fee NUMERIC(12, 2);

CREATE INDEX IF NOT EXISTS ix_transactions_vehicle_class_override
    ON transactions (original_vehicle_type_id)
    WHERE original_vehicle_type_id IS NOT NULL;
