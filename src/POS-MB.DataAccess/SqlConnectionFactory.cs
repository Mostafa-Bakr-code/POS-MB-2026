using System.Data;
using Microsoft.Data.SqlClient;

namespace POS_MB.DataAccess;

public class SqlConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    public IDbConnection CreateConnection() => new SqlConnection(connectionString);
}
