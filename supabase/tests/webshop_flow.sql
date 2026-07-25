\set ON_ERROR_STOP on

INSERT INTO auth.users (id)
VALUES
    ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'),
    ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb');

CREATE SCHEMA test_support;
CREATE TABLE test_support.ticket (id uuid NOT NULL);
GRANT USAGE ON SCHEMA test_support TO authenticated, service_role;
GRANT SELECT, INSERT ON test_support.ticket TO authenticated, service_role;

SET ROLE authenticated;
SELECT set_config('request.jwt.claim.sub', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', false);
INSERT INTO test_support.ticket
SELECT public.create_webshop_ticket('russia', 'ru_store', '0.0.1');
RESET ROLE;

SET ROLE service_role;
DO $$
DECLARE
    ticket_id uuid := (SELECT id FROM test_support.ticket LIMIT 1);
    first_valid boolean;
    second_valid boolean;
BEGIN
    SELECT is_valid INTO first_valid
    FROM public.consume_webshop_ticket(ticket_id);
    SELECT is_valid INTO second_valid
    FROM public.consume_webshop_ticket(ticket_id);

    IF first_valid IS DISTINCT FROM true OR second_valid IS DISTINCT FROM false THEN
        RAISE EXCEPTION 'Ticket must be valid exactly once';
    END IF;
END
$$;

INSERT INTO public.webshop_orders (
    id, user_id, product_id, product_title, currency_type, reward_amount,
    price, price_currency, provider, provider_payment_id, status, paid_at)
VALUES (
    '11111111-1111-1111-1111-111111111111',
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    'product-60', '60 Diamonds', 'Diamonds', 60,
    1.99, 'EUR', 'MockProvider', 'mock-one', 'paid', now());
RESET ROLE;

SET ROLE authenticated;
SELECT set_config('request.jwt.claim.sub', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', false);
DO $$
DECLARE
    claim_id uuid := 'cccccccc-cccc-cccc-cccc-cccccccccccc';
BEGIN
    IF (SELECT count(*) FROM public.webshop_orders) <> 1 THEN
        RAISE EXCEPTION 'Owner must see the paid order';
    END IF;
    IF NOT public.reserve_webshop_order('11111111-1111-1111-1111-111111111111', claim_id) THEN
        RAISE EXCEPTION 'Owner could not reserve the paid order';
    END IF;
    IF NOT public.complete_webshop_order('11111111-1111-1111-1111-111111111111', claim_id) THEN
        RAISE EXCEPTION 'Owner could not complete the reserved order';
    END IF;
    IF public.complete_webshop_order('11111111-1111-1111-1111-111111111111', claim_id) THEN
        RAISE EXCEPTION 'Delivered order was completed twice';
    END IF;
END
$$;

SELECT set_config('request.jwt.claim.sub', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', false);
DO $$
BEGIN
    IF (SELECT count(*) FROM public.webshop_orders) <> 0 THEN
        RAISE EXCEPTION 'RLS exposed another player order';
    END IF;
END
$$;
RESET ROLE;

SET ROLE service_role;
SELECT public.record_webshop_paid_order(
    '22222222-2222-2222-2222-222222222222',
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    '33333333-3333-3333-3333-333333333333',
    '75 Diamonds',
    'Diamonds',
    75,
    2.99,
    'EUR',
    'MockProvider',
    'mock-two',
    now(),
    '[{"product_id":"33333333-3333-3333-3333-333333333333","product_type":"Currency","quantity":1,"unit_price":2.99,"metadata":{"diamonds":"75"}}]'::jsonb);

-- A repeated Outbox delivery is intentionally idempotent.
SELECT public.record_webshop_paid_order(
    '22222222-2222-2222-2222-222222222222',
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    '33333333-3333-3333-3333-333333333333',
    '75 Diamonds',
    'Diamonds',
    75,
    2.99,
    'EUR',
    'MockProvider',
    'mock-two',
    now(),
    '[{"product_id":"33333333-3333-3333-3333-333333333333","product_type":"Currency","quantity":1,"unit_price":2.99,"metadata":{"diamonds":"75"}}]'::jsonb);

DO $$
BEGIN
    IF (SELECT count(*) FROM public.webshop_purchases WHERE order_id = '22222222-2222-2222-2222-222222222222') <> 1 THEN
        RAISE EXCEPTION 'Paid order audit row was not recorded';
    END IF;
END
$$;
RESET ROLE;

SELECT 'WebShop Supabase flow verification passed' AS result;
