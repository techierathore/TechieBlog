using BlogModels;
using BlogModels.Interfaces;

namespace TechieBlog.Services;

public class ManageService<TEntity> : IManageService<TEntity>
{
    public byte[] DownloadFile(string aRequestUri, long aId)
    {
        throw new NotImplementedException();
    }

    public List<TEntity> GetAllList(string aRequestUri)
    {
        throw new NotImplementedException();
    }

    public TEntity GetIntSingle(string aRequestUri, int aId)
    {
        throw new NotImplementedException();
    }

    public List<TEntity> GetReport(string aRequestUri, ReportInput aObj)
    {
        throw new NotImplementedException();
    }

    public TEntity GetSingle(string aRequestUri, long aId)
    {
        throw new NotImplementedException();
    }

    public List<TEntity> GetSubsList(string aRequestUri, long aId)
    {
        throw new NotImplementedException();
    }

    public List<TEntity> GetSubsListByString(string aRequestUri, string aValue)
    {
        throw new NotImplementedException();
    }

    public TEntity Save(string aRequestUri, TEntity aObj)
    {
        throw new NotImplementedException();
    }

    public TEntity Update(string aRequestUri, TEntity aObj)
    {
        throw new NotImplementedException();
    }

    public bool UploadFile(string aRequestUri, TEntity aObj, Stream aFiles, string aFileName)
    {
        throw new NotImplementedException();
    }
}
