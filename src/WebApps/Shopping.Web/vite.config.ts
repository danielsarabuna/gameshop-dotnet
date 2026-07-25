import { defineConfig, Plugin } from 'vite';
import react from '@vitejs/plugin-react';

const mockApiPlugin = (): Plugin => {
  return {
    name: 'mock-api-fallback',
    configureServer(server) {
      server.middlewares.use(async (req, res, next) => {
        const reqUrl = req.originalUrl || req.url || '';
        if (!reqUrl.startsWith('/api/') && !req.url?.startsWith('/api/')) {
          return next();
        }

        const requestBody = req.method === 'GET' || req.method === 'HEAD'
          ? undefined
          : Buffer.concat(await Array.fromAsync(req, (chunk) => Buffer.from(chunk)));

        // Check if API Gateway on 5100 is reachable
        try {
          const targetUrl = `http://localhost:5100${req.url}`;
          const controller = new AbortController();
          const timeout = setTimeout(() => controller.abort(), 1000);

          const backendRes = await fetch(targetUrl, {
            method: req.method,
            headers: req.headers as Record<string, string>,
            body: requestBody?.length ? requestBody : undefined,
            signal: controller.signal,
          });
          clearTimeout(timeout);

          if (backendRes) {
            const buffer = Buffer.from(await backendRes.arrayBuffer());
            res.statusCode = backendRes.status;
            res.setHeader('Content-Type', backendRes.headers.get('content-type') || 'application/json');
            res.end(buffer);
            return;
          }
        } catch {
          // Backend offline - proceed to mock response below
        }

        // Handle asset proxy requests in standalone mock mode (return 404 for missing assets or path traversal)
        if (reqUrl.includes('/api/v1/catalog/assets/')) {
          res.statusCode = 404;
          res.setHeader('Content-Type', 'application/json');
          res.end(JSON.stringify({ error: 'Asset not found or path traversal blocked' }));
          return;
        }

        // Mock Fallback endpoints for local dev when backend is offline
        if (reqUrl.includes('/api/v1/auth/resolve-player')) {
          const playerId = JSON.parse(requestBody?.toString('utf8') || '{}').playerId;
          const valid = typeof playerId === 'string' && /^[0-9a-f-]{36}$/i.test(playerId);
          res.statusCode = valid ? 200 : 400;
          res.setHeader('Content-Type', 'application/json');
          res.end(JSON.stringify(valid ? {
            isValid: true,
            userId: playerId,
            playerName: 'Тестовый игрок',
            region: 'global',
            store: 'global',
            gameVersion: 'global',
            deliveryContractVersion: 2,
            accessToken: 'local-preview-token',
            sessionKind: 'recipient',
          } : { isValid: false, errorCode: 'invalid_player', errorMessage: 'Player could not be resolved.' }));
          return;
        }

        if (reqUrl.includes('/api/v1/payment-methods')) {
          res.statusCode = 200;
          res.setHeader('Content-Type', 'application/json');
          res.end(
            JSON.stringify([
              { code: 'card', name: 'Банковская карта (Visa / MasterCard / МИР)' },
              { code: 'sbp', name: 'Система Быстрых Платежей (СБП)' },
              { code: 'telegram', name: 'Telegram Stars / Wallet' },
              { code: 'stripe', name: 'Stripe Payment Gateway' },
              { code: 'paypal', name: 'PayPal Checkout' },
            ])
          );
          return;
        }

        if (reqUrl.includes('/api/v1/catalog/items')) {
          res.statusCode = 200;
          res.setHeader('Content-Type', 'application/json');
          res.end(
            JSON.stringify([
              { id: 'd1a00000-0000-0000-0000-000000000060', title: '60 Алмазов', type: 'Currency', price: 1.23, currency: 'EUR', isActive: true, metadata: { diamonds: '60' } },
              { id: 'd1a00000-0000-0000-0000-000000000150', title: '150 Алмазов', type: 'Currency', price: 4.99, currency: 'EUR', isActive: true, metadata: { diamonds: '150' } },
              { id: 'd1a00000-0000-0000-0000-000000000300', title: '300 Алмазов', type: 'Currency', price: 9.98, currency: 'EUR', isActive: true, metadata: { diamonds: '300', badgeKey: 'hot' } },
              { id: 'd1a00000-0000-0000-0000-000000000450', title: '450 Алмазов', type: 'Currency', price: 14.97, currency: 'EUR', isActive: true, metadata: { diamonds: '450' } },
              { id: 'd1a00000-0000-0000-0000-000000000600', title: '600 Алмазов', type: 'Currency', price: 19.96, currency: 'EUR', isActive: true, metadata: { diamonds: '600', badgeKey: 'best' } },
              { id: 'd1a00000-0000-0000-0000-000000001200', title: '1200 Алмазов', type: 'Currency', price: 34.99, currency: 'EUR', isActive: true, metadata: { diamonds: '1200', badgeKey: 'hot' } },
              { id: 'd1a00000-0000-0000-0000-000000002500', title: '2500 Алмазов', type: 'Currency', price: 69.99, currency: 'EUR', isActive: true, metadata: { diamonds: '2500', badgeKey: 'mega' } },
              { id: 'd1a00000-0000-0000-0000-000000009000', title: '9000 Алмазов', type: 'Currency', price: 199.99, currency: 'EUR', isActive: true, metadata: { diamonds: '9000' } },
              { id: '9aa00000-0000-0000-0000-000000000001', title: 'Premium — 1 месяц', type: 'Subscription', price: 6.99, currency: 'EUR', isActive: true, metadata: { months: '1' } },
              { id: '9aa00000-0000-0000-0000-000000000003', title: 'Premium — 3 месяца', type: 'Subscription', price: 17.99, currency: 'EUR', isActive: true, metadata: { months: '3' } },
              { id: '9aa00000-0000-0000-0000-000000000012', title: 'Premium — 12 месяцев', type: 'Subscription', price: 59.99, currency: 'EUR', isActive: true, metadata: { months: '12', badgeKey: 'best' } },
            ])
          );
          return;
        }

        if (reqUrl.includes('/api/v1/promos/apply')) {
          res.statusCode = 200;
          res.setHeader('Content-Type', 'application/json');
          res.end(JSON.stringify({ isValid: true, discountAmount: 1.0, error: null }));
          return;
        }

        if (reqUrl.includes('/api/v1/orders/create')) {
          res.statusCode = 200;
          res.setHeader('Content-Type', 'application/json');
          res.end(JSON.stringify({ orderId: 'ord_mock_' + Date.now(), status: 'Pending', totalAmount: 9.99 }));
          return;
        }

        if (reqUrl.includes('/api/v1/payments/')) {
          res.statusCode = 200;
          res.setHeader('Content-Type', 'application/json');
          res.end(JSON.stringify({ paymentId: 'pay_mock_' + Date.now(), provider: 'mock', status: 'Success', checkoutUrl: '#' }));
          return;
        }

        res.statusCode = 404;
        res.setHeader('Content-Type', 'application/json');
        res.end(JSON.stringify({ error: 'API endpoint not found' }));
        return;
      });
    },
  };
};

export default defineConfig({
  plugins: [react(), mockApiPlugin()],
  publicDir: 'wwwroot',
  server: {
    port: 5200,
    host: true,
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
});
