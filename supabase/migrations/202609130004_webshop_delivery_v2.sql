BEGIN;

ALTER TABLE public.webshop_orders
    ADD COLUMN IF NOT EXISTS reward_items jsonb NOT NULL DEFAULT '[]'::jsonb;
ALTER TABLE public.webshop_tickets
    ADD COLUMN IF NOT EXISTS delivery_contract_version integer NOT NULL DEFAULT 1;
ALTER TABLE public.webshop_player_context
    ADD COLUMN IF NOT EXISTS delivery_contract_version integer NOT NULL DEFAULT 1;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'public.webshop_orders'::regclass
          AND conname = 'webshop_orders_reward_items_array')
    THEN
        ALTER TABLE public.webshop_orders
            ADD CONSTRAINT webshop_orders_reward_items_array
            CHECK (jsonb_typeof(reward_items) = 'array');
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'public.webshop_tickets'::regclass
          AND conname = 'webshop_tickets_delivery_contract_version_valid')
    THEN
        ALTER TABLE public.webshop_tickets
            ADD CONSTRAINT webshop_tickets_delivery_contract_version_valid
            CHECK (delivery_contract_version BETWEEN 1 AND 100);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'public.webshop_player_context'::regclass
          AND conname = 'webshop_player_context_delivery_contract_version_valid')
    THEN
        ALTER TABLE public.webshop_player_context
            ADD CONSTRAINT webshop_player_context_delivery_contract_version_valid
            CHECK (delivery_contract_version BETWEEN 1 AND 100);
    END IF;
END
$$;

UPDATE public.webshop_orders
SET reward_items = jsonb_build_array(jsonb_build_object(
    'product_id', product_id,
    'title', product_title,
    'kind', CASE lower(currency_type)
        WHEN 'diamonds' THEN 'diamonds'
        WHEN 'subscription' THEN 'subscription_days'
        ELSE lower(currency_type)
    END,
    'amount', reward_amount,
    'quantity', 1))
WHERE reward_items = '[]'::jsonb;

CREATE OR REPLACE FUNCTION public.sync_webshop_player_context_v2(
    p_region text,
    p_store_channel text,
    p_game_version text,
    p_delivery_contract_version integer)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
DECLARE
    current_user_id uuid := auth.uid();
BEGIN
    IF p_delivery_contract_version < 2 OR p_delivery_contract_version > 100 THEN
        RAISE EXCEPTION 'Invalid webshop delivery contract version';
    END IF;

    PERFORM public.sync_webshop_player_context(p_region, p_store_channel, p_game_version);
    UPDATE public.webshop_player_context
    SET delivery_contract_version = p_delivery_contract_version,
        updated_at = now()
    WHERE user_id = current_user_id;
END;
$$;

CREATE OR REPLACE FUNCTION public.create_webshop_ticket_v2(
    p_region text,
    p_store_channel text,
    p_game_version text,
    p_delivery_contract_version integer)
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

    PERFORM public.sync_webshop_player_context_v2(
        p_region, p_store_channel, p_game_version, p_delivery_contract_version);

    INSERT INTO public.webshop_tickets (
        user_id, region, store_channel, game_version,
        delivery_contract_version, expires_at)
    SELECT
        user_id, region, store_channel, game_version,
        delivery_contract_version, now() + interval '5 minutes'
    FROM public.webshop_player_context
    WHERE user_id = current_user_id
    RETURNING webshop_tickets.ticket_id INTO v_ticket_id;

    RETURN v_ticket_id;
END;
$$;

DROP FUNCTION public.consume_webshop_ticket(uuid);
CREATE FUNCTION public.consume_webshop_ticket(p_ticket_id uuid)
RETURNS TABLE (
    user_id uuid,
    region text,
    store_channel text,
    game_version text,
    delivery_contract_version integer,
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

    IF NOT FOUND THEN
        RETURN QUERY SELECT NULL::uuid, ''::text, ''::text, ''::text, 1, false;
        RETURN;
    END IF;

    RETURN QUERY SELECT
        claimed.user_id,
        claimed.region,
        claimed.store_channel,
        claimed.game_version,
        claimed.delivery_contract_version,
        true;
END;
$$;

CREATE OR REPLACE FUNCTION public.record_webshop_paid_order_v2(
    p_order_id uuid,
    p_user_id uuid,
    p_product_id uuid,
    p_product_title text,
    p_currency_type text,
    p_reward_amount integer,
    p_price numeric,
    p_price_currency text,
    p_provider text,
    p_provider_payment_id text,
    p_paid_at timestamptz,
    p_reward_items jsonb,
    p_items jsonb)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
BEGIN
    IF jsonb_typeof(p_reward_items) <> 'array'
       OR jsonb_array_length(p_reward_items) = 0
       OR EXISTS (
           SELECT 1
           FROM jsonb_array_elements(p_reward_items) reward
           WHERE jsonb_typeof(reward) <> 'object'
              OR COALESCE(reward->>'kind', '') = ''
              OR COALESCE((reward->>'amount')::integer, 0) <= 0)
    THEN
        RAISE EXCEPTION 'A v2 paid webshop order requires valid reward items';
    END IF;

    PERFORM public.record_webshop_paid_order(
        p_order_id, p_user_id, p_product_id, p_product_title,
        p_currency_type, p_reward_amount, p_price, p_price_currency,
        p_provider, p_provider_payment_id, p_paid_at, p_items);

    UPDATE public.webshop_orders
    SET reward_items = p_reward_items
    WHERE id = p_order_id;
END;
$$;

REVOKE ALL ON FUNCTION public.sync_webshop_player_context_v2(text, text, text, integer)
    FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.create_webshop_ticket_v2(text, text, text, integer)
    FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.consume_webshop_ticket(uuid)
    FROM PUBLIC, anon, authenticated;
REVOKE ALL ON FUNCTION public.record_webshop_paid_order_v2(
    uuid, uuid, uuid, text, text, integer, numeric, text, text, text,
    timestamptz, jsonb, jsonb)
    FROM PUBLIC, anon, authenticated;

GRANT EXECUTE ON FUNCTION public.sync_webshop_player_context_v2(text, text, text, integer)
    TO authenticated, service_role;
GRANT EXECUTE ON FUNCTION public.create_webshop_ticket_v2(text, text, text, integer)
    TO authenticated, service_role;
GRANT EXECUTE ON FUNCTION public.consume_webshop_ticket(uuid)
    TO service_role;
GRANT EXECUTE ON FUNCTION public.record_webshop_paid_order_v2(
    uuid, uuid, uuid, text, text, integer, numeric, text, text, text,
    timestamptz, jsonb, jsonb)
    TO service_role;

COMMIT;
