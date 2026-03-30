namespace Shopping.Web.State;

public sealed class CartState
{
    private readonly List<CartLine> _lines = [];

    public IReadOnlyList<CartLine> Lines => _lines;

    public int Count => _lines.Sum(l => l.Quantity);

    public decimal Total => _lines.Sum(l => l.UnitPrice * l.Quantity);

    public event Action? Changed;

    public void AddItem(string sku, string title, decimal unitPrice, int quantity = 1)
    {
        if (quantity <= 0 || string.IsNullOrWhiteSpace(sku))
        {
            return;
        }

        var existing = _lines.FirstOrDefault(l => l.Sku == sku);
        if (existing is not null)
        {
            existing.Quantity += quantity;
        }
        else
        {
            _lines.Add(new CartLine(sku, title, unitPrice, quantity));
        }

        Changed?.Invoke();
    }

    public void Clear()
    {
        _lines.Clear();
        Changed?.Invoke();
    }

    public void Remove(string sku)
    {
        _lines.RemoveAll(l => l.Sku == sku);
        Changed?.Invoke();
    }

    public sealed class CartLine
    {
        public CartLine(string sku, string title, decimal unitPrice, int quantity)
        {
            Sku = sku;
            Title = title;
            UnitPrice = unitPrice;
            Quantity = quantity;
        }

        public string Sku { get; }
        public string Title { get; }
        public decimal UnitPrice { get; }
        public int Quantity { get; set; }
    }
}
