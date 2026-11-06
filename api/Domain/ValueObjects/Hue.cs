namespace Api.Domain.ValueObjects
{
    public readonly record struct Hue
    {
        public int Value { get; }

        public Hue(int value)
        {
            if (value is < 0 or > 359)
                throw new ArgumentOutOfRangeException(nameof(value));

            Value = value;
        }
    }
}
