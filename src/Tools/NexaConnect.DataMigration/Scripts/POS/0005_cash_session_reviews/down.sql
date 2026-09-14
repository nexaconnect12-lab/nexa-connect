DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM cash_session_review_history)
       OR EXISTS (SELECT 1 FROM cash_session_review_states) THEN
        RAISE EXCEPTION 'Cannot downgrade POS migration 5 after cash-session reviews exist.';
    END IF;
END;
$$;

DROP TRIGGER IF EXISTS cash_session_review_history_append_only ON cash_session_review_history;
DROP FUNCTION IF EXISTS deny_cash_session_review_history_mutation();
DROP INDEX IF EXISTS ix_cash_sessions_store_closed_review;
DROP TABLE IF EXISTS cash_session_review_history;
DROP TABLE IF EXISTS cash_session_review_states;
