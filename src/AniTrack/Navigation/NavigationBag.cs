namespace AniTrack.Navigation;

public sealed class NavigationBag
{
    private object? _payload;

    public T? Consume<T>() where T : class
    {
        var v = _payload as T;
        _payload = null;
        return v;
    }

    public void Put(object data) => _payload = data;
}
