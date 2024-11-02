using System.Data;
namespace BlogModels.Interfaces;

public interface IGenericRepository<TEntity>
{
    IDbConnection GetOpenConnection();
    long InsertToGetId(TEntity aEntity);
    void Insert(TEntity aEntity);
    void Update(TEntity aEntityToUpdate);
    TEntity GetSingle(long aSingleId);
    TEntity GetIntSingle(int aSingleId);
    IEnumerable<TEntity> GetAll();
    IEnumerable<TEntity> GetPagedData(int PageSize, int OffSet);
    IEnumerable<TEntity> GetAllById(long aSingleId);
}
