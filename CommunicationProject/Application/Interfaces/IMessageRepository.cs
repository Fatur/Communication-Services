using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunicationServices.Domain.Entities;

namespace CommunicationServices.Application.Interfaces
{
    public interface IMessageRepository
    {
        Task InsertAsync(MessageLog message, CancellationToken ct = default);
        Task<MessageLog?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IEnumerable<MessageLog>> ClaimPendingAsync(int batchSize, CancellationToken ct = default);
        Task UpdateAsync(MessageLog message, CancellationToken ct = default);
    }
}