using Humanizer;
using Microsoft.EntityFrameworkCore;
using InCleanHome.API.Shared.Domain.Repositories;

namespace InCleanHome.API.Shared.Domain.Repositories
{
    public interface IUnitOfWork
    {
        Task CompleteAsync();
    }

    public interface IBaseRepository<TEntity>
    {
        Task AddAsync(TEntity entity);
        Task<TEntity?> FindByIdAsync(int id);
        void Update(TEntity entity);
        void Remove(TEntity entity);
        Task<IEnumerable<TEntity>> ListAsync();
    }
}

namespace InCleanHome.API.Shared.Infrastructure.Persistence.EFC.Repositories
{
    public class UnitOfWork(DbContext context) : IUnitOfWork
    {
        public async Task CompleteAsync() => await context.SaveChangesAsync();
    }

    public class BaseRepository<TEntity>(DbContext context) : IBaseRepository<TEntity> where TEntity : class
    {
        protected readonly DbContext Context = context;

        public async Task AddAsync(TEntity entity)        => await Context.Set<TEntity>().AddAsync(entity);
        public async Task<TEntity?> FindByIdAsync(int id) => await Context.Set<TEntity>().FindAsync(id);
        public void Update(TEntity entity)                => Context.Set<TEntity>().Update(entity);
        public void Remove(TEntity entity)                => Context.Set<TEntity>().Remove(entity);
        public async Task<IEnumerable<TEntity>> ListAsync()=> await Context.Set<TEntity>().ToListAsync();
    }
}

namespace InCleanHome.API.Shared.Infrastructure.Persistence.EFC.Configuration.Extensions
{
    public static class StringExtensions
    {
        public static string ToSnakeCase(this string text)
        {
            return new string(Convert(text.GetEnumerator()).ToArray());

            static IEnumerable<char> Convert(CharEnumerator e)
            {
                if (!e.MoveNext()) yield break;
                yield return char.ToLower(e.Current);

                while (e.MoveNext())
                    if (char.IsUpper(e.Current))
                    {
                        yield return '_';
                        yield return char.ToLower(e.Current);
                    }
                    else yield return e.Current;
            }
        }

        public static string ToPlural(this string text) => text.Pluralize(false);
    }

    public static class ModelBuilderExtensions
    {
        public static void UseSnakeCaseNamingConvention(this ModelBuilder builder)
        {
            foreach (var entity in builder.Model.GetEntityTypes())
            {
                var tableName = entity.GetTableName();
                if (!string.IsNullOrEmpty(tableName)) entity.SetTableName(tableName.ToPlural().ToSnakeCase());

                foreach (var property in entity.GetProperties())
                    property.SetColumnName(property.GetColumnName().ToSnakeCase());

                foreach (var key in entity.GetKeys())
                {
                    var keyName = key.GetName();
                    if (!string.IsNullOrEmpty(keyName)) key.SetName(keyName.ToSnakeCase());
                }

                foreach (var foreignKey in entity.GetForeignKeys())
                {
                    var fkName = foreignKey.GetConstraintName();
                    if (!string.IsNullOrEmpty(fkName)) foreignKey.SetConstraintName(fkName.ToSnakeCase());
                }

                foreach (var index in entity.GetIndexes())
                {
                    var idxName = index.GetDatabaseName();
                    if (!string.IsNullOrEmpty(idxName)) index.SetDatabaseName(idxName.ToSnakeCase());
                }
            }
        }
    }
}
