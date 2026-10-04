namespace Api.Builders
{
    public abstract class BaseBuilder<T> : IBuilder<T>
    {
        protected abstract T Item { get; set; }

        public T Build() => Item;
    }
}
