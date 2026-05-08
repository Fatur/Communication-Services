using CommunicationServices.Infrastructure.Enum;
using System.Data;

namespace CommunicationServices.Application.Interfaces
{
    public interface IConnectionFactory
    {
        Task<IDbConnection> GetConnectionAsync(Requestor requestor, string tenantId);
    }
}
