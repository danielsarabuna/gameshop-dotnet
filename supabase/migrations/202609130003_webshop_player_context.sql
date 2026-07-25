BEGIN;

CREATE TABLE IF NOT EXISTS public.webshop_player_context
(
    user_id uuid PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    region text NOT NULL,
    store_channel text NOT NULL,
    game_version text NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT webshop_player_context_region_valid CHECK (region ~ '^[A-Za-z0-9._-]{1,64}$'),
    CONSTRAINT webshop_player_context_store_valid CHECK (store_channel ~ '^[A-Za-z0-9._-]{1,64}$'),
    CONSTRAINT webshop_player_context_version_valid CHECK (game_version ~ '^[A-Za-z0-9._-]{1,64}$')
);

ALTER TABLE public.webshop_player_context ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE public.webshop_player_context FROM PUBLIC, anon, authenticated;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.webshop_player_context TO service_role;

INSERT INTO public.webshop_player_context (user_id, region, store_channel, game_version, updated_at)
SELECT DISTINCT ON (user_id)
    user_id,
    CASE WHEN region ~ '^[A-Za-z0-9._-]{1,64}$' THEN region ELSE 'global' END,
    CASE WHEN store_channel ~ '^[A-Za-z0-9._-]{1,64}$' THEN store_channel ELSE 'global' END,
    CASE WHEN game_version ~ '^[A-Za-z0-9._-]{1,64}$' THEN game_version ELSE 'global' END,
    created_at
FROM public.webshop_tickets
ORDER BY user_id, created_at DESC
ON CONFLICT (user_id) DO NOTHING;

CREATE OR REPLACE FUNCTION public.sync_webshop_player_context(
    p_region text,
    p_store_channel text,
    p_game_version text)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
DECLARE
    current_user_id uuid := auth.uid();
    normalized_region text := COALESCE(NULLIF(trim(p_region), ''), 'global');
    normalized_store text := COALESCE(NULLIF(trim(p_store_channel), ''), 'global');
    normalized_version text := COALESCE(NULLIF(trim(p_game_version), ''), 'global');
BEGIN
    IF current_user_id IS NULL THEN
        RAISE EXCEPTION 'Active authenticated session is required to synchronize webshop context';
    END IF;

    IF normalized_region !~ '^[A-Za-z0-9._-]{1,64}$'
       OR normalized_store !~ '^[A-Za-z0-9._-]{1,64}$'
       OR normalized_version !~ '^[A-Za-z0-9._-]{1,64}$'
    THEN
        RAISE EXCEPTION 'Invalid webshop context';
    END IF;

    INSERT INTO public.webshop_player_context (
        user_id, region, store_channel, game_version, updated_at)
    VALUES (
        current_user_id, normalized_region, normalized_store, normalized_version, now())
    ON CONFLICT (user_id) DO UPDATE SET
        region = EXCLUDED.region,
        store_channel = EXCLUDED.store_channel,
        game_version = EXCLUDED.game_version,
        updated_at = EXCLUDED.updated_at;
END;
$$;

CREATE OR REPLACE FUNCTION public.create_webshop_ticket(
    p_region text,
    p_store_channel text,
    p_game_version text)
RETURNS uuid
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
DECLARE
    v_ticket_id uuid;
    current_user_id uuid := auth.uid();
BEGIN
    IF current_user_id IS NULL THEN
        RAISE EXCEPTION 'Active authenticated session is required to generate webshop ticket';
    END IF;

    PERFORM public.sync_webshop_player_context(p_region, p_store_channel, p_game_version);

    INSERT INTO public.webshop_tickets (
        user_id, region, store_channel, game_version, expires_at)
    SELECT
        user_id, region, store_channel, game_version, now() + interval '5 minutes'
    FROM public.webshop_player_context
    WHERE user_id = current_user_id
    RETURNING webshop_tickets.ticket_id INTO v_ticket_id;

    RETURN v_ticket_id;
END;
$$;

REVOKE ALL ON FUNCTION public.sync_webshop_player_context(text, text, text) FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.create_webshop_ticket(text, text, text) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.sync_webshop_player_context(text, text, text) TO authenticated, service_role;
GRANT EXECUTE ON FUNCTION public.create_webshop_ticket(text, text, text) TO authenticated, service_role;

COMMIT;
