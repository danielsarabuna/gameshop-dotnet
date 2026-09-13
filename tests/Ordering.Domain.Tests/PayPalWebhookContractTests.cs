using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ordering.Application.Payments;
using Ordering.Infrastructure.Payments;

namespace Ordering.Domain.Tests;

public sealed class PayPalWebhookContractTests
{
    [Fact]
    public async Task ParseWebhookAsync_OfficialCaptureShapeAndVerifiedSignature_ReturnsSucceededPayment()
    {
        var orderId = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new
        {
            id = "WH-123",
            event_type = "PAYMENT.CAPTURE.COMPLETED",
            resource = new { custom_id = orderId.ToString("D"), amount = new { value = "12.34", currency_code = "EUR" } }
        });
        var handler = new ContractHandler();
        using var client = new HttpClient(handler);
        var provider = new PayPalPaymentProvider("client", "secret", webhookSecret: "WEBHOOK_ID", httpClient: client);
        var result = await provider.ParseWebhookAsync(new WebhookEnvelope(body, new Dictionary<string, string>
        {
            ["PayPal-Auth-Algo"] = "SHA256withRSA",
            ["PayPal-Cert-Url"] = "https://api.sandbox.paypal.com/cert",
            ["PayPal-Transmission-Id"] = "tx-1",
            ["PayPal-Transmission-Sig"] = "signature",
            ["PayPal-Transmission-Time"] = "2026-01-01T00:00:00Z"
        }, null), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(orderId, result!.OrderId);
        Assert.Equal(12.34m, result.Amount);
        Assert.Equal("EUR", result.Currency);
        Assert.True(handler.VerificationRequested);
    }

    private sealed class ContractHandler : HttpMessageHandler
    {
        public bool VerificationRequested { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/v1/oauth2/token")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "token" }) };
            VerificationRequested = request.RequestUri.AbsolutePath == "/v1/notifications/verify-webhook-signature";
            var sent = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            Assert.Equal("WEBHOOK_ID", sent.GetProperty("webhook_id").GetString());
            Assert.Equal("tx-1", sent.GetProperty("transmission_id").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { verification_status = "SUCCESS" }) };
        }
    }
}
