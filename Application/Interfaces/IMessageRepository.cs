using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunicationService.Domain.Entities;

namespace CommunicationService.Application.Interfaces
{
    public interface IMessageRepository
    {
        Task InsertAsync(MessageLog message);
        Task<MessageLog?> GetByIdAsync(Guid id);
        Task<IEnumerable<MessageLog>> ClaimPendingAsync(int batchSize);
        Task UpdateAsync(MessageLog message);
    }
}