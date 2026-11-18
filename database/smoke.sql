-- Smoke test skema setelah schema.sql + seluruh migration.
DO $$
BEGIN
    IF to_regclass('public.transactions') IS NULL THEN
        RAISE EXCEPTION 'Tabel transactions tidak ada.';
    END IF;
    IF to_regclass('public.device_credentials') IS NULL THEN
        RAISE EXCEPTION 'Tabel device_credentials tidak ada.';
    END IF;
    IF to_regclass('public.gate_commands') IS NULL THEN
        RAISE EXCEPTION 'Tabel gate_commands tidak ada.';
    END IF;
    IF to_regclass('public.ux_transactions_active_member') IS NULL THEN
        RAISE EXCEPTION 'Index anti-passback tidak ada.';
    END IF;
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'transactions'
          AND column_name = 'member_entry_valid'
    ) THEN
        RAISE EXCEPTION 'Kolom member_entry_valid tidak ada.';
    END IF;
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'transactions'
          AND column_name = 'print_status'
    ) THEN
        RAISE EXCEPTION 'Kolom print_status tidak ada.';
    END IF;
END $$;

SELECT 'schema-smoke-ok' AS result;
