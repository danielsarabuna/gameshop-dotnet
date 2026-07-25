BEGIN;

-- WebShop owns only the objects in this migration. Existing GameShop player
-- tables are intentionally neither altered nor read here.

-- Catalog JSON and assets are fetched through Supabase Storage public object
-- URLs. Uploads still require service_role.
INSERT INTO storage.buckets (id, name, public)
VALUES ('dev', 'dev', true), ('prod', 'prod', true)
ON CONFLICT (id) DO UPDATE SET public = EXCLUDED.public;

DROP POLICY IF EXISTS webshop_catalog_public_read ON storage.objects;
CREATE POLICY webshop_catalog_public_read
    ON storage.objects
    FOR SELECT
    TO public
    USING (bucket_id IN ('dev', 'prod'));

CREATE TABLE IF NOT EXISTS public.webshop_orders
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    product_id text NOT NULL,
    product_title text NOT NULL DEFAULT '',
    currency_type text NOT NULL DEFAULT 'Diamonds',
    reward_amount integer NOT NULL CHECK (reward_amount > 0),
    price numeric(12, 2) NOT NULL CHECK (price >= 0),
    price_currency text NOT NULL,
    provider text NOT NULL,
    provider_payment_id text NOT NULL,
    status text NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending', 'paid', 'claiming', 'delivered', 'refunded')),
    paid_at timestamptz,
    claim_token uuid,
    claim_expires_at timestamptz,
    delivered_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    UNIQUE (provider, provider_payment_id)
);

CREATE INDEX IF NOT EXISTS ix_webshop_orders_user_delivery
    ON public.webshop_orders (user_id, status, paid_at);

ALTER TABLE public.webshop_orders ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE public.webshop_orders FROM PUBLIC, anon, authenticated;
GRANT SELECT ON TABLE public.webshop_orders TO authenticated;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.webshop_orders TO service_role;

DROP POLICY IF EXISTS webshop_orders_select_own ON public.webshop_orders;
CREATE POLICY webshop_orders_select_own
    ON public.webshop_orders
    FOR SELECT
    TO authenticated
    USING ((SELECT auth.uid()) = user_id);

-- Postgres Changes must explicitly publish the queue table. RLS is still
-- evaluated for each authenticated subscriber.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_publication WHERE pubname = 'supabase_realtime')
       AND NOT EXISTS (
           SELECT 1
           FROM pg_publication_tables
           WHERE pubname = 'supabase_realtime'
             AND schemaname = 'public'
             AND tablename = 'webshop_orders')
    THEN
        ALTER PUBLICATION supabase_realtime ADD TABLE public.webshop_orders;
    END IF;
END
$$;

CREATE OR REPLACE FUNCTION public.release_expired_webshop_claims()
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
DECLARE
    released_count integer;
BEGIN
    IF auth.uid() IS NULL THEN
        RAISE EXCEPTION 'Active authenticated session is required';
    END IF;

    UPDATE public.webshop_orders
    SET status = 'paid', claim_token = NULL, claim_expires_at = NULL
    WHERE user_id = auth.uid()
      AND status = 'claiming'
      AND claim_expires_at < now();

    GET DIAGNOSTICS released_count = ROW_COUNT;
    RETURN released_count;
END;
$$;

CREATE OR REPLACE FUNCTION public.reserve_webshop_order(
    p_order_id uuid,
    p_claim_token uuid)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
BEGIN
    IF auth.uid() IS NULL THEN
        RAISE EXCEPTION 'Active authenticated session is required';
    END IF;
    IF p_claim_token IS NULL THEN
        RAISE EXCEPTION 'Claim token is required';
    END IF;

    UPDATE public.webshop_orders
    SET status = 'claiming',
        claim_token = p_claim_token,
        claim_expires_at = now() + interval '10 minutes'
    WHERE id = p_order_id
      AND user_id = auth.uid()
      AND status = 'paid';

    RETURN FOUND;
END;
$$;

CREATE OR REPLACE FUNCTION public.complete_webshop_order(
    p_order_id uuid,
    p_claim_token uuid)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
BEGIN
    IF auth.uid() IS NULL THEN
        RAISE EXCEPTION 'Active authenticated session is required';
    END IF;

    UPDATE public.webshop_orders
    SET status = 'delivered',
        delivered_at = now(),
        claim_token = NULL,
        claim_expires_at = NULL
    WHERE id = p_order_id
      AND user_id = auth.uid()
      AND status = 'claiming'
      AND claim_token = p_claim_token;

    RETURN FOUND;
END;
$$;

CREATE OR REPLACE FUNCTION public.release_webshop_order(
    p_order_id uuid,
    p_claim_token uuid)
RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
BEGIN
    IF auth.uid() IS NULL THEN
        RAISE EXCEPTION 'Active authenticated session is required';
    END IF;

    UPDATE public.webshop_orders
    SET status = 'paid', claim_token = NULL, claim_expires_at = NULL
    WHERE id = p_order_id
      AND user_id = auth.uid()
      AND status = 'claiming'
      AND claim_token = p_claim_token;

    RETURN FOUND;
END;
$$;

REVOKE ALL ON FUNCTION public.release_expired_webshop_claims() FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.reserve_webshop_order(uuid, uuid) FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.complete_webshop_order(uuid, uuid) FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.release_webshop_order(uuid, uuid) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.release_expired_webshop_claims() TO authenticated;
GRANT EXECUTE ON FUNCTION public.reserve_webshop_order(uuid, uuid) TO authenticated;
GRANT EXECUTE ON FUNCTION public.complete_webshop_order(uuid, uuid) TO authenticated;
GRANT EXECUTE ON FUNCTION public.release_webshop_order(uuid, uuid) TO authenticated;

CREATE TABLE IF NOT EXISTS public.webshop_tickets
(
    ticket_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    region text NOT NULL DEFAULT 'global',
    store_channel text NOT NULL DEFAULT 'google_play',
    game_version text NOT NULL DEFAULT '1.0.0',
    expires_at timestamptz NOT NULL,
    used_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_webshop_tickets_validation
    ON public.webshop_tickets (ticket_id, used_at, expires_at);

ALTER TABLE public.webshop_tickets ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE public.webshop_tickets FROM PUBLIC, anon, authenticated;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.webshop_tickets TO service_role;

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

    INSERT INTO public.webshop_tickets (
        user_id, region, store_channel, game_version, expires_at)
    VALUES (
        current_user_id,
        COALESCE(NULLIF(trim(p_region), ''), 'global'),
        COALESCE(NULLIF(trim(p_store_channel), ''), 'google_play'),
        COALESCE(NULLIF(trim(p_game_version), ''), '1.0.0'),
        now() + interval '5 minutes')
    RETURNING webshop_tickets.ticket_id INTO v_ticket_id;

    RETURN v_ticket_id;
END;
$$;

DROP FUNCTION IF EXISTS public.consume_webshop_ticket(uuid);
CREATE FUNCTION public.consume_webshop_ticket(p_ticket_id uuid)
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
    IF p_ticket_id IS NULL THEN
        RETURN QUERY SELECT NULL::uuid, ''::text, ''::text, ''::text, false;
        RETURN;
    END IF;

    UPDATE public.webshop_tickets
    SET used_at = now()
    WHERE ticket_id = p_ticket_id
      AND used_at IS NULL
      AND expires_at >= now()
    RETURNING * INTO claimed;

    IF NOT FOUND THEN
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

REVOKE ALL ON FUNCTION public.create_webshop_ticket(text, text, text) FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.consume_webshop_ticket(uuid) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.create_webshop_ticket(text, text, text) TO authenticated, service_role;
GRANT EXECUTE ON FUNCTION public.consume_webshop_ticket(uuid) TO service_role;

COMMIT;
