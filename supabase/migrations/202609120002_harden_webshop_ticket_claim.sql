BEGIN;

-- Forward-only compatibility migration for projects where tickets were
-- previously created by GameShop SQL or through the Dashboard.

-- The ticket is a short-lived bearer credential. Consume it in one statement so
-- concurrent requests cannot both receive a valid WebShop session.
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

-- Unity creates tickets as the signed-in player. Only the server-side Catalog
-- service may exchange a ticket for a WebShop JWT.
REVOKE ALL ON TABLE public.webshop_tickets FROM PUBLIC, anon, authenticated;
REVOKE ALL ON FUNCTION public.consume_webshop_ticket(uuid) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.consume_webshop_ticket(uuid) TO service_role;

COMMIT;
