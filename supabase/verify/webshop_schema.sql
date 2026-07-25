\set ON_ERROR_STOP on

DO $$
DECLARE
    relation_name text;
    function_name text;
BEGIN
    FOREACH relation_name IN ARRAY ARRAY[
        'public.webshop_orders',
        'public.webshop_purchases',
        'public.webshop_tickets'
    ]
    LOOP
        IF to_regclass(relation_name) IS NULL THEN
            RAISE EXCEPTION 'Missing WebShop relation: %', relation_name;
        END IF;
    END LOOP;

    IF EXISTS (
        SELECT 1
        FROM pg_class
        WHERE oid IN (
            'public.webshop_orders'::regclass,
            'public.webshop_purchases'::regclass,
            'public.webshop_tickets'::regclass)
          AND NOT relrowsecurity)
    THEN
        RAISE EXCEPTION 'RLS must be enabled on every WebShop table';
    END IF;

    IF NOT has_table_privilege('authenticated', 'public.webshop_orders', 'SELECT')
       OR has_table_privilege('anon', 'public.webshop_orders', 'SELECT')
       OR has_table_privilege('anon', 'public.webshop_orders', 'INSERT')
       OR has_table_privilege('anon', 'public.webshop_orders', 'UPDATE')
       OR has_table_privilege('anon', 'public.webshop_orders', 'DELETE')
       OR has_table_privilege('authenticated', 'public.webshop_orders', 'INSERT')
       OR has_table_privilege('authenticated', 'public.webshop_orders', 'UPDATE')
       OR has_table_privilege('authenticated', 'public.webshop_orders', 'DELETE')
       OR has_table_privilege('anon', 'public.webshop_purchases', 'SELECT')
       OR has_table_privilege('anon', 'public.webshop_purchases', 'INSERT')
       OR has_table_privilege('anon', 'public.webshop_purchases', 'UPDATE')
       OR has_table_privilege('anon', 'public.webshop_purchases', 'DELETE')
       OR has_table_privilege('authenticated', 'public.webshop_purchases', 'SELECT')
       OR has_table_privilege('authenticated', 'public.webshop_purchases', 'INSERT')
       OR has_table_privilege('authenticated', 'public.webshop_purchases', 'UPDATE')
       OR has_table_privilege('authenticated', 'public.webshop_purchases', 'DELETE')
       OR has_table_privilege('anon', 'public.webshop_tickets', 'SELECT')
       OR has_table_privilege('anon', 'public.webshop_tickets', 'INSERT')
       OR has_table_privilege('anon', 'public.webshop_tickets', 'UPDATE')
       OR has_table_privilege('anon', 'public.webshop_tickets', 'DELETE')
       OR has_table_privilege('authenticated', 'public.webshop_tickets', 'SELECT')
       OR has_table_privilege('authenticated', 'public.webshop_tickets', 'INSERT')
       OR has_table_privilege('authenticated', 'public.webshop_tickets', 'UPDATE')
       OR has_table_privilege('authenticated', 'public.webshop_tickets', 'DELETE')
    THEN
        RAISE EXCEPTION 'Unexpected authenticated table grants';
    END IF;

    IF (SELECT count(*)
        FROM pg_policies
        WHERE schemaname = 'public'
          AND tablename = 'webshop_orders') <> 1
       OR NOT EXISTS (
        SELECT 1
        FROM pg_policies
        WHERE schemaname = 'public'
          AND tablename = 'webshop_orders'
          AND policyname = 'webshop_orders_select_own'
          AND 'authenticated' = ANY (roles))
    THEN
        RAISE EXCEPTION 'WebShop order policies differ from the ownership-only contract';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM pg_policies
        WHERE schemaname = 'public'
          AND tablename IN ('webshop_purchases', 'webshop_tickets'))
    THEN
        RAISE EXCEPTION 'Server-only WebShop tables must not have client RLS policies';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_publication_tables
        WHERE pubname = 'supabase_realtime'
          AND schemaname = 'public'
          AND tablename = 'webshop_orders')
    THEN
        RAISE EXCEPTION 'webshop_orders is not in supabase_realtime publication';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM pg_publication_tables
        WHERE pubname = 'supabase_realtime'
          AND schemaname = 'public'
          AND tablename IN ('webshop_purchases', 'webshop_tickets'))
    THEN
        RAISE EXCEPTION 'Server-only WebShop tables must not be published to Realtime';
    END IF;

    FOREACH function_name IN ARRAY ARRAY[
        'public.release_expired_webshop_claims()',
        'public.reserve_webshop_order(uuid,uuid)',
        'public.complete_webshop_order(uuid,uuid)',
        'public.release_webshop_order(uuid,uuid)',
        'public.create_webshop_ticket(text,text,text)',
        'public.consume_webshop_ticket(uuid)',
        'public.record_webshop_paid_order(uuid,uuid,uuid,text,text,integer,numeric,text,text,text,timestamptz,jsonb)'
    ]
    LOOP
        IF to_regprocedure(function_name) IS NULL THEN
            RAISE EXCEPTION 'Missing WebShop function: %', function_name;
        END IF;
        IF has_function_privilege('anon', function_name, 'EXECUTE') THEN
            RAISE EXCEPTION 'Anonymous role can execute WebShop function: %', function_name;
        END IF;
    END LOOP;

    IF NOT has_function_privilege('authenticated', 'public.create_webshop_ticket(text,text,text)', 'EXECUTE')
       OR NOT has_function_privilege('authenticated', 'public.reserve_webshop_order(uuid,uuid)', 'EXECUTE')
       OR NOT has_function_privilege('authenticated', 'public.complete_webshop_order(uuid,uuid)', 'EXECUTE')
       OR NOT has_function_privilege('authenticated', 'public.release_webshop_order(uuid,uuid)', 'EXECUTE')
       OR NOT has_function_privilege('authenticated', 'public.release_expired_webshop_claims()', 'EXECUTE')
       OR has_function_privilege('authenticated', 'public.consume_webshop_ticket(uuid)', 'EXECUTE')
       OR has_function_privilege('authenticated', 'public.record_webshop_paid_order(uuid,uuid,uuid,text,text,integer,numeric,text,text,text,timestamptz,jsonb)', 'EXECUTE')
       OR NOT has_function_privilege('service_role', 'public.consume_webshop_ticket(uuid)', 'EXECUTE')
       OR NOT has_function_privilege('service_role', 'public.record_webshop_paid_order(uuid,uuid,uuid,text,text,integer,numeric,text,text,text,timestamptz,jsonb)', 'EXECUTE')
    THEN
        RAISE EXCEPTION 'Unexpected WebShop RPC grants';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = 'public'
          AND p.proname IN (
              'release_expired_webshop_claims',
              'reserve_webshop_order',
              'complete_webshop_order',
              'release_webshop_order',
              'create_webshop_ticket',
              'consume_webshop_ticket',
              'record_webshop_paid_order')
          AND (
              NOT p.prosecdef
              OR NOT EXISTS (
                  SELECT 1
                  FROM unnest(COALESCE(p.proconfig, ARRAY[]::text[])) AS setting
                  WHERE setting LIKE 'search_path=%')))
    THEN
        RAISE EXCEPTION 'WebShop SECURITY DEFINER functions require an explicit search_path';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM storage.buckets WHERE id = 'dev' AND public)
       OR NOT EXISTS (SELECT 1 FROM storage.buckets WHERE id = 'prod' AND public)
    THEN
        RAISE EXCEPTION 'WebShop catalog buckets dev/prod are missing or not public';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_policies
        WHERE schemaname = 'storage'
          AND tablename = 'objects'
          AND policyname = 'webshop_catalog_public_read'
          AND cmd = 'SELECT'
          AND 'public' = ANY (roles))
    THEN
        RAISE EXCEPTION 'Missing public read policy for WebShop catalog storage';
    END IF;
END
$$;

SELECT 'WebShop Supabase schema verification passed' AS result;
