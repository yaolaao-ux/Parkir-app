DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM transactions
        WHERE member_id IS NOT NULL
          AND time_out IS NULL
          AND payment_status = 'UNPAID'
        GROUP BY member_id
        HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION
            'Migration 010 gagal: ada member dengan lebih dari satu sesi aktif. Selesaikan duplikat sebelum membuat unique index anti-passback.';
    END IF;
END $$;

ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS entry_request_fingerprint VARCHAR(64);

CREATE UNIQUE INDEX IF NOT EXISTS ux_transactions_active_member
    ON transactions (member_id)
    WHERE member_id IS NOT NULL
      AND time_out IS NULL
      AND payment_status = 'UNPAID';
