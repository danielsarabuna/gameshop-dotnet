namespace Shopping.Web.State;

public sealed class CartState
{
    private int _count;

    public int Count => _count;

    public event Action? Changed;

    public void Add(int quantity = 1)
    {
        if (quantity <= 0)
        {
            return;
        }

        _count += quantity;
        Changed?.Invoke();
    }

    public void Clear()
    {
        _count = 0;
        Changed?.Invoke();
    }
}

