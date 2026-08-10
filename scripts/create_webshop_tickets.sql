-- =============================================================================
-- WebShop SQL Migration: webshop_tickets table and RPC functions
-- Execute this script in Supabase Dashboard -> SQL Editor
-- =============================================================================

-- 1. Table for single-use WebShop authentication tickets
CREATE TABLE IF NOT EXISTS public.webshop_tickets (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    region TEXT NOT NULL DEFAULT 'russia',
    store_channel TEXT NOT NULL DEFAULT 'ru_store',
    game_version TEXT NOT NULL DEFAULT '0.0.36',
    is_used BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL DEFAULT (NOW() + INTERVAL '10 minutes')
);

CREATE INDEX IF NOT EXISTS idx_webshop_tickets_user ON public.webshop_tickets(user_id);
CREATE INDEX IF NOT EXISTS idx_webshop_tickets_active ON public.webshop_tickets(id, is_used, expires_at);

-- 2. RPC function to create a ticket (called by Unity client for logged-in player)
CREATE OR REPLACE FUNCTION public.create_webshop_ticket(
    p_region TEXT DEFAULT 'russia',
    p_store_channel TEXT DEFAULT 'ru_store',
    p_game_version TEXT DEFAULT '0.0.36'
)
RETURNS UUID
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_user_id UUID;
    v_ticket_id UUID;
BEGIN
    v_user_id := auth.uid();
    IF v_user_id IS NULL THEN
        RAISE EXCEPTION 'User must be authenticated to create a webshop ticket';
    END IF;

    INSERT INTO public.webshop_tickets (user_id, region, store_channel, game_version)
    VALUES (v_user_id, p_region, p_store_channel, p_game_version)
    RETURNING id INTO v_ticket_id;

    RETURN v_ticket_id;
END;
$$;

GRANT EXECUTE ON FUNCTION public.create_webshop_ticket TO authenticated;
GRANT EXECUTE ON FUNCTION public.create_webshop_ticket TO service_role;

-- 3. RPC function to consume/validate a ticket (called by WebShop backend)
CREATE OR REPLACE FUNCTION public.consume_webshop_ticket(
    p_ticket_id UUID
)
RETURNS TABLE (
    is_valid BOOLEAN,
    user_id UUID,
    region TEXT,
    store_channel TEXT,
    game_version TEXT
)
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
    v_rec RECORD;
BEGIN
    SELECT t.user_id, t.region, t.store_channel, t.game_version, t.is_used, t.expires_at
    INTO v_rec
    FROM public.webshop_tickets t
    WHERE t.id = p_ticket_id;

    IF NOT FOUND THEN
        RETURN QUERY SELECT FALSE, NULL::UUID, ''::TEXT, ''::TEXT, ''::TEXT;
        RETURN;
    END IF;

    IF v_rec.is_used OR v_rec.expires_at < NOW() THEN
        RETURN QUERY SELECT FALSE, v_rec.user_id, v_rec.region, v_rec.store_channel, v_rec.game_version;
        RETURN;
    END IF;

    UPDATE public.webshop_tickets
    SET is_used = TRUE
    WHERE id = p_ticket_id;

    RETURN QUERY SELECT TRUE, v_rec.user_id, v_rec.region, v_rec.store_channel, v_rec.game_version;
END;
$$;

GRANT EXECUTE ON FUNCTION public.consume_webshop_ticket TO anon, authenticated, service_role;
