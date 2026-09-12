BEGIN;

-- Depends on 202609120000_webshop_core.sql. Keeps the server-side payment
-- audit and Unity delivery queue update in one database transaction.

CREATE TABLE IF NOT EXISTS public.webshop_purchases
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id uuid NOT NULL REFERENCES public.webshop_orders(id) ON DELETE CASCADE,
    game_user_id uuid NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    payment_method text NOT NULL,
    currency text NOT NULL,
    total numeric(12, 2) NOT NULL CHECK (total >= 0),
    product_id uuid NOT NULL,
    product_type text NOT NULL,
    quantity integer NOT NULL CHECK (quantity > 0),
    unit_price numeric(12, 2) NOT NULL CHECK (unit_price >= 0),
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    paid_at_utc timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (order_id, product_id)
);

ALTER TABLE public.webshop_purchases ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.webshop_purchases FROM PUBLIC, anon, authenticated;
GRANT SELECT, INSERT, UPDATE ON public.webshop_purchases TO service_role;

CREATE OR REPLACE FUNCTION public.record_webshop_paid_order(
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
    p_items jsonb)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = ''
AS $$
DECLARE
    item jsonb;
BEGIN
    IF p_reward_amount <= 0 OR jsonb_typeof(p_items) <> 'array' OR jsonb_array_length(p_items) = 0 THEN
        RAISE EXCEPTION 'A paid webshop order requires a positive reward and at least one item';
    END IF;

    INSERT INTO public.webshop_orders (
        id, user_id, product_id, product_title, currency_type, reward_amount,
        price, price_currency, provider, provider_payment_id, status, paid_at)
    VALUES (
        p_order_id, p_user_id, p_product_id::text, p_product_title, p_currency_type,
        p_reward_amount, p_price, upper(p_price_currency), p_provider,
        COALESCE(NULLIF(p_provider_payment_id, ''), p_order_id::text), 'paid', p_paid_at)
    ON CONFLICT (id) DO UPDATE SET
        product_title = EXCLUDED.product_title,
        reward_amount = EXCLUDED.reward_amount,
        provider_payment_id = EXCLUDED.provider_payment_id,
        paid_at = COALESCE(public.webshop_orders.paid_at, EXCLUDED.paid_at),
        status = CASE
            WHEN public.webshop_orders.status IN ('claiming', 'delivered', 'refunded')
                THEN public.webshop_orders.status
            ELSE 'paid'
        END;

    FOR item IN SELECT value FROM jsonb_array_elements(p_items)
    LOOP
        INSERT INTO public.webshop_purchases (
            order_id, game_user_id, payment_method, currency, total,
            product_id, product_type, quantity, unit_price, metadata, paid_at_utc)
        VALUES (
            p_order_id, p_user_id, p_provider, upper(p_price_currency), p_price,
            (item->>'product_id')::uuid, item->>'product_type',
            (item->>'quantity')::integer, (item->>'unit_price')::numeric,
            COALESCE(item->'metadata', '{}'::jsonb), p_paid_at)
        ON CONFLICT (order_id, product_id) DO NOTHING;
    END LOOP;
END;
$$;

REVOKE ALL ON FUNCTION public.record_webshop_paid_order(
    uuid, uuid, uuid, text, text, integer, numeric, text, text, text,
    timestamptz, jsonb) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.record_webshop_paid_order(
    uuid, uuid, uuid, text, text, integer, numeric, text, text, text,
    timestamptz, jsonb) TO service_role;

COMMIT;
