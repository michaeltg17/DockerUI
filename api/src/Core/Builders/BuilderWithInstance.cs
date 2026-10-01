namespace Core.Builders
{
    public abstract class BuilderWithInstance<TBuilder, TEntity> : BuilderWithValues<TBuilder, TEntity>
        where TBuilder : BuilderWithInstance<TBuilder, TEntity>
        where TEntity : new()
    {
        protected override TEntity Item { get; set; } = new TEntity();
    }
}
