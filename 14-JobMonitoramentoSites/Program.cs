using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Serilog;

namespace JobMonitoramentoSites
{
    class Program
    {
        static void Main()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();

            var builder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
             .AddJsonFile($"appsettings.json")
             .AddEnvironmentVariables();
            var config = builder.Build();

            try
            {
                var serviceConfigurations = new ServiceConfigurations();
                new ConfigureFromConfigurationOptions<ServiceConfigurations>(
                    config.GetSection("ServiceConfigurations"))
                        .Configure(serviceConfigurations);

                var jsonOptions = new JsonSerializerOptions()
                {
                    IgnoreNullValues = true
                };

                /*var storageAccount = CloudStorageAccount
                    .Parse(config["BaseMonitoramento"]);
                var monitoramentoTable = storageAccount
                    .CreateCloudTableClient().GetTableReference("Monitoramento");
                if (monitoramentoTable.CreateIfNotExistsAsync().Result)
                    logger.Information("Criando a tabela de log...");*/

                foreach (string host in serviceConfigurations.Hosts)
                {
                    logger.Information(
                        $"Verificando a disponibilidade do host {host}");

                    var resultado = new ResultadoMonitoramento();
                    resultado.Horario =
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    resultado.Host = host;

                    using (var client = new HttpClient())
                    {
                        client.BaseAddress = new Uri(host);
                        client.DefaultRequestHeaders.Accept.Clear();

                        try
                        {
                            // Envio da requisicao a fim de determinar se
                            // o site esta no ar
                            HttpResponseMessage response =
                                client.GetAsync("").Result;

                            resultado.Status = (int)response.StatusCode + " " +
                                response.StatusCode;
                            if (response.StatusCode != HttpStatusCode.OK)
                                resultado.Exception = response.ReasonPhrase;
                        }
                        catch (Exception ex)
                        {
                            resultado.Status = "Exception";
                            resultado.Exception = ex.Message;
                        }
                    }

                    // Imprimindo o resultado do teste
                    string jsonResultado =
                        JsonSerializer.Serialize(resultado, jsonOptions);

                    if (resultado.Exception == null)
                        logger.Information(jsonResultado);
                    else
                        logger.Error(jsonResultado);


                    using (var clientLogicAppSlack = new HttpClient())
                    {
                        clientLogicAppSlack.BaseAddress = new Uri(
                            config["UrlLogicAppMonitoramento"]);
                        clientLogicAppSlack.DefaultRequestHeaders.Accept.Clear();
                        clientLogicAppSlack.DefaultRequestHeaders.Accept.Add(
                            new MediaTypeWithQualityHeaderValue("application/json"));

                        var requestMessage =
                              new HttpRequestMessage(HttpMethod.Post, String.Empty);

                        requestMessage.Content = new StringContent(
                            JsonSerializer.Serialize(new
                            {
                                site = resultado.Host,
                                horario = resultado.Horario,
                                status = resultado.Status + " " + resultado.Exception?.ToString()
                            }), Encoding.UTF8, "application/json");

                        var respLogicApp = clientLogicAppSlack
                            .SendAsync(requestMessage).Result;
                        respLogicApp.EnsureSuccessStatusCode();

                        logger.Information(
                            "Envio de alerta para Logic App de integração com o Slack");
                    }
                }

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                logger.Error(ex.GetType().FullName + " - " + ex.Message);
                Environment.Exit(1);
            }
        }
    }
}