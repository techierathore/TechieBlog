using BlogModels.Interfaces;


namespace BlogEngine.DaCore;

/// <summary>
/// The concrete implementation of a IGenericRepository Repository
/// 
/// </summary> 
/// <typeparam name="TEntity"></typeparam>
public abstract class GenericRepository<TEntity> : IGenericRepository<TEntity>
    where TEntity : class
{
    private string _connectionString;
    private EDbConnectionTypes _dbType;

    /// <summary>
    /// Change the DBType here to change the type of DB you
    /// are implimenting that way you don't need to create sperate
    /// DataAccess class or Implimentation of IGenericRepository
    /// </summary>
    /// <param name="connectionString"></param>
    public GenericRepository(string connectionString)
    {
        _dbType = EDbConnectionTypes.MySql;
        _connectionString = connectionString;
    }

    public IDbConnection GetOpenConnection()
    {
        return DbConnectionFactory.GetDbConnection(_dbType, _connectionString);
    }
    public abstract IEnumerable<TEntity> GetAll();
    public abstract IEnumerable<TEntity> GetAllById(long aSingleId);
    public abstract IEnumerable<TEntity> GetPagedData(int PageSize, int OffSet);
    public abstract TEntity GetSingle(long aSingleId);
    public abstract TEntity GetIntSingle(int aSingleId);
    public abstract void Insert(TEntity aEntity);
    public abstract long InsertToGetId(TEntity entity);
    public abstract void Update(TEntity aEntityToUpdate);
}
