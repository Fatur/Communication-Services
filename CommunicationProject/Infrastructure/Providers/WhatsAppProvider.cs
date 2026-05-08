using CommunicationServices.Application.Interfaces;
using CommunicationServices.Infrastructure.Enum;
using Dapper;
using Microsoft.Extensions.Logging;
using RestSharp;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;

namespace CommunicationServices.Infrastructure.Providers
{
    public class WhatsAppProvider : IWhatsAppProvider
    {
        private readonly IConnectionFactory _connectionFactory;
        private readonly ILogger<WhatsAppProvider> _logger;
        private string Host => "https://msg.soficloud.com";
        public WhatsAppProvider(IConnectionFactory connectionFactory, ILogger<WhatsAppProvider> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        public async Task SendAsync(Requestor requestor, string tenantId, IList<string> to, string body)
        {
            _logger.LogInformation("[WhatsAppProvider] Sending whatsapp to {To}: {Body} using {Host}", to, body, this.Host);

            try
            {
                if (to.Count == 0 || String.IsNullOrEmpty(body))
                    throw new Exception("Phone number and message body are required.");

                string host = requestor switch
                {
                    Requestor.Soficloud => await GetSoficloudWhatsappHost(tenantId).ConfigureAwait(false) ?? this.Host,
                    Requestor.Pisicloud => await GetPisicloudWhatsappHost().ConfigureAwait(false) ?? this.Host,
                    _ => throw new NotSupportedException($"Requestor {requestor} not supported")
                };

                RestClient client = new RestClient(host);
                RestRequest request = new RestRequest($"whatsapp/send", Method.Post);

                request.AddBody(new
                {
                    phone = to.ToArray(),
                    message = body
                });

                RestResponse response = await client.ExecuteAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return;
                }
                else
                {
                    throw new Exception(response.Content);
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending WhatsApp message to {To} using {Host}", to, this.Host);
                throw;
            }
        }

        private async Task<string?> GetSoficloudWhatsappHost(string tenantId)
        {
            try
            {
                using var dbConnection = await _connectionFactory.GetConnection(Requestor.Soficloud, tenantId);
                var sql = "SELECT WhatsappHost FROM tblsetup";
                var cmd = new CommandDefinition(sql, cancellationToken: default);
                var result = await dbConnection.QueryFirstOrDefaultAsync<string>(cmd);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching tenant WhatsApp host from database. Falling back to default host.");
                return null;
            }
        }

        private async Task<string?> GetPisicloudWhatsappHost()
        {
            throw new NotImplementedException("Pisicloud WhatsApp host retrieval not implemented yet.");
        }
    }
}
