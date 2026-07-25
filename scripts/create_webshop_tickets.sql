-- Canonical Unity/WebShop ticket contract.
-- Execute once in Supabase Dashboard -> SQL Editor for a new environment.
BEGIN;

CREATE TABLE IF NOT EXISTS public.webshop_tickets (
    ticket_id     uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id       uuid NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    region        text NOT NULL DEFAULT 'global',
    store_channel text NOT NULL DEFAULT 'google_play',
    game_version  text NOT NULL DEFAULT '1.0.0',
    expires_at    timestamptz NOT NULL,
    used_at       timestamptz,
    created_at    timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_webshop_tickets_validation
    ON public.webshop_tickets (ticket_id, used_at, expires_at);

ALTER TABLE public.webshop_tickets ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.webshop_tickets FROM anon, authenticated;

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
    v_user_id uuid := auth.uid();
BEGIN
    IF v_user_id IS NULL THEN
        RAISE EXCEPTION 'Active authenticated session is required to generate webshop ticket';
    END IF;

    INSERT INTO public.webshop_tickets (
        user_id, region, store_channel, game_version, expires_at)
    VALUES (
        v_user_id,
        COALESCE(NULLIF(p_region, ''), 'global'),
        COALESCE(NULLIF(p_store_channel, ''), 'google_play'),
        COALESCE(NULLIF(p_game_version, ''), '1.0.0'),
        now() + interval '5 minutes')
    RETURNING ticket_id INTO v_ticket_id;

    RETURN v_ticket_id;
END;
$$;

CREATE OR REPLACE FUNCTION public.consume_webshop_ticket(p_ticket_id uuid)
RETURNS TABLE (
    user_id uuid,
    region text,
    store_channel text,
    game_version text,
    is_valid boolean)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
DECLARE
    claimed public.webshop_tickets%ROWTYPE;
BEGIN
    UPDATE public.webshop_tickets
    SET used_at = now()
    WHERE ticket_id = p_ticket_id
      AND used_at IS NULL
      AND expires_at >= now()
    RETURNING * INTO claimed;

    IF claimed.ticket_id IS NULL THEN
        RETURN QUERY SELECT NULL::uuid, ''::text, ''::text, ''::text, false;
        RETURN;
    END IF;

    RETURN QUERY SELECT
        claimed.user_id,
        claimed.region,
        claimed.store_channel,
        claimed.game_version,
        true;
END;
$$;

REVOKE ALL ON FUNCTION public.create_webshop_ticket(text, text, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.consume_webshop_ticket(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.create_webshop_ticket(text, text, text) TO authenticated, service_role;
GRANT EXECUTE ON FUNCTION public.consume_webshop_ticket(uuid) TO service_role;

COMMIT;
