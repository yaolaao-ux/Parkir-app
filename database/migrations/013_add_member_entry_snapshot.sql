ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS member_entry_valid BOOLEAN;
