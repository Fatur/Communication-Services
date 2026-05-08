using CommunicationServices.Application.Interfaces;
using CommunicationServices.Domain.Entities;
using CommunicationServices.Helper;
using CommunicationServices.Infrastructure.Enum;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using System.Data;
using System.Security.Cryptography;
using System.Text;


namespace CommunicationServices.Infrastructure.Factory
{
    public class ConnectionFactory : IConnectionFactory
    {
        private IMemoryCache memoryCache;
        private IConfiguration configuration;

        public ConnectionFactory(IMemoryCache memoryCache, IConfiguration configuration)
        {
            this.memoryCache = memoryCache;
            this.configuration = configuration;
        }

        public async Task<IDbConnection> GetConnectionAsync(Requestor requestor, string tenantId)
        {
            var key = $"{tenantId}_{requestor}";
            if (!memoryCache.TryGetValue<string>(key, out var connectionString))
            {
                var newConnectionString = requestor switch
                {
                    Requestor.Soficloud => await GetSofiConnectionString(tenantId).ConfigureAwait(false),
                    Requestor.Pisicloud => GetPisiConnectionString(tenantId),
                    _ => throw new NotSupportedException($"Requestor {requestor} not supported")
                };
                memoryCache.Set(key, newConnectionString);
            }

            IDbConnection connection = new SqlConnection(connectionString);
            return connection;
        }

        private async Task<string> GetSofiConnectionString(string tenantId)
        {
            var dbConnectionString = configuration.GetConnectionString(Requestor.Soficloud.ToString());
            using var dbConnection = new SqlConnection(dbConnectionString);
            const string sql = "SELECT TenantId, Customer, Server, CloudPassword AS Password, Dbase FROM tblCustLec WHERE TenantId = @TenantId";

            var cmd = new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: default);
            var tenant = await dbConnection.QuerySingleOrDefaultAsync<Tenant>(cmd).ConfigureAwait(false);

            if (tenant == null)
            {
                throw new Exception($"Tenant with ID {tenantId} not found.");
            } else
            {
                string decryptedPassword = Encryptor.DecryptPassword(tenant.Password);
                string connectionString = $"Server={tenant.Server};Database={tenant.Dbase};User Id=sa;Password={decryptedPassword};TrustServerCertificate=True;";
                return connectionString;
            }
        }

        private string GetPisiConnectionString(string tenantId)
        {
            // Implement logic to retrieve connection string for Pisicloud if needed
            throw new NotImplementedException("Pisicloud connection string retrieval not implemented.");
        }
    }
}