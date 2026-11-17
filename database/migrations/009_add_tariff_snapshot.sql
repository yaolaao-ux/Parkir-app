ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS grace_period_minutes INTEGER,
    ADD COLUMN IF NOT EXISTS first_hour_rate NUMERIC(12, 2),
    ADD COLUMN IF NOT EXISTS hourly_rate NUMERIC(12, 2),
    ADD COLUMN IF NOT EXISTS lost_ticket_fee NUMERIC(12, 2),
    ADD COLUMN IF NOT EXISTS max_daily_rate NUMERIC(12, 2);
