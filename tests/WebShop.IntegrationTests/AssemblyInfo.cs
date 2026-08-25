using Xunit;

// GatewayAuthTests toggles Auth__Enabled via process environment variables (the only
// reliable way to reach Program.cs config in WebApplicationFactory for minimal APIs),
// so parallel execution could leak that state into unrelated service factories.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
