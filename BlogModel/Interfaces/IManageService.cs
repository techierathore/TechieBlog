namespace BlogModels.Interfaces;
/// <summary>
/// THe Interface for implimenting repository. 
/// </summary>
/// <typeparam name="TEntity"></typeparam>
public interface IManageService<TEntity>
{
    Task<List<TEntity>> GetAllListAsync(string aRequestUri);
    Task<List<TEntity>> GetSubsListAsync(string aRequestUri, long aId);
    Task<List<TEntity>> GetAllByStringAsync(string aRequestUri, string aValue);
    Task<TEntity> GetSingleAsync(string aRequestUri, long aId);
    Task<TEntity> GetIntSingleAsync(string aRequestUri, int aId);
    Task<TEntity> SaveAsync(string aRequestUri, TEntity aObj);
    Task<TEntity> UpdateAsync(string aRequestUri, TEntity aObj);
    Task<List<TEntity>> GetReportAsync(string aRequestUri, ReportInput aObj);

    List<TEntity> GetAllList(string aRequestUri);
    List<TEntity> GetSubsList(string aRequestUri, long aId);
    List<TEntity> GetSubsListByString(string aRequestUri, string aValue);
    TEntity GetSingle(string aRequestUri, long aId);
    TEntity GetIntSingle(string aRequestUri, int aId);
    TEntity Save(string aRequestUri, TEntity aObj);
    TEntity Update(string aRequestUri, TEntity aObj);
    List<TEntity> GetReport(string aRequestUri, ReportInput aObj);
    bool UploadFile(string aRequestUri, TEntity aObj, Stream aFiles, string aFileName);
    byte[] DownloadFile(string aRequestUri, long aId);

}
