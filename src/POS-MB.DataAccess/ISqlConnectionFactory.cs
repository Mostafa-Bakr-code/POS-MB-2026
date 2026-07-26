using System.Data;

namespace POS_MB.DataAccess;

public interface ISqlConnectionFactory
{
    IDbConnection CreateConnection();
}
