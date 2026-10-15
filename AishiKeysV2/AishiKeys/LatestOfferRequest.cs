namespace AishiKeys
{
    internal sealed class LatestOfferRequest<T>
        where T : class
    {
        internal T Value { get; private set; }
        internal int Version { get; private set; }
        internal bool HasValue => Value != null;

        internal int Submit(T value)
        {
            Value = value;
            return ++Version;
        }

        internal bool IsCurrent(int version) => HasValue && Version == version;
        internal void Complete(int version)
        {
            if (IsCurrent(version))
                Value = null;
        }

        internal void Cancel()
        {
            Value = null;
            Version++;
        }
    }
}
